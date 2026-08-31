using System.Net;
using Loom.Config;
using Loom.Packages;
using Version = Loom.Config.Version;

namespace Loom.Testing.Packages;

/// <summary>
///     A registry spoken to over HTTP, answering the same shape a directory of versions does. Nothing here reaches a
///     network: every request is answered by <see cref="StubHttpMessageHandler" />, which is also what lets a case
///     assert on what was <em>sent</em> — the entity tag, the bearer token, the path a scope is routed under.
/// </summary>
/// <remarks>
///     These cases are what an index publishes; installing is in <c>RemotePackageIndexTest.Install.cs</c> and
///     publishing in <c>RemotePackageIndexTest.Publish.cs</c>.
/// </remarks>
[Collection("Assembly")]
public partial class RemotePackageIndexTest
{
    private const string Registry = "https://registry.test";

    /// <remarks>
    ///     <c>Publications</c> promises newest last and <c>LockResolver</c> reads the newest match off the end, so a
    ///     registry answering in another order would resolve to an older version with nothing anywhere saying so.
    ///     The sort is defensive, which is exactly why a wrong answer here would be invisible.
    /// </remarks>
    [Fact]
    public void Publications_ComeBackNewestLast_WhateverOrderTheRegistryStatesThem()
    {
        var handler = StubHttpMessageHandler.Answering(
            HttpStatusCode.OK,
            Document("math", Publication("1.10.0"), Publication("1.2.0"), Publication("1.0.0-beta.1"))
        );

        var publications = Index(handler).Publications(PackageName.Parse("math"), out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(["1.0.0-beta.1", "1.2.0", "1.10.0"], publications.Select(publication => publication.Version.ToString()));
    }

    /// <remarks>The one answer that is a resolution failure rather than a registry failure, and the only empty one.</remarks>
    [Fact]
    public void Publications_OfAPackageTheRegistryDoesNotPublish_AreNoneAndNoFailure()
    {
        var publications = Index(StubHttpMessageHandler.Answering(HttpStatusCode.NotFound)).Publications(PackageName.Parse("math"), out var diagnostics);

        Assert.Empty(publications);
        Assert.Empty(diagnostics);
    }

    /// <remarks>
    ///     The failure a directory on disk has no way of having. Read as an empty result it would send somebody whose
    ///     network is down looking for a package that exists, which is the whole reason the out-parameter is there.
    /// </remarks>
    [Fact]
    public void Publications_ReportARegistryThatCouldNotBeReached()
    {
        Assert.Empty(Index(StubHttpMessageHandler.Unreachable()).Publications(PackageName.Parse("math"), out var diagnostics));

        Assert.Contains("could not reach 'https://registry.test'", Assert.Single(diagnostics).Message);
    }

    [Fact]
    public void Publications_ReportARefusal_InTheRegistrysOwnWords()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Refusal(HttpStatusCode.ServiceUnavailable, "the index is being rebuilt"));

        Assert.Empty(Index(handler).Publications(PackageName.Parse("math"), out var diagnostics));

        Assert.Equal("the index is being rebuilt", Assert.Single(diagnostics).Message);
    }

    /// <remarks>Only where there is no envelope does this side invent wording, since there is none to pass on.</remarks>
    [Fact]
    public void Publications_ReportARefusalWithNoEnvelope_ByWhatItAnswered()
    {
        Assert.Empty(Index(StubHttpMessageHandler.Answering(HttpStatusCode.BadGateway)).Publications(PackageName.Parse("math"), out var diagnostics));

        Assert.Contains("answered 502", Assert.Single(diagnostics).Message);
    }

    [Fact]
    public void Publications_ReportABodyThatCannotBeRead()
    {
        Assert.Empty(Index(StubHttpMessageHandler.Answering(HttpStatusCode.OK, "not json at all")).Publications(PackageName.Parse("math"), out var diagnostics));

        Assert.Contains("answered something this cannot read about 'math'", Assert.Single(diagnostics).Message);
    }

    /// <remarks>
    ///     Once the name is dropped and only the versions are kept, a registry answering about another package cannot
    ///     be told apart from one answering about this one.
    /// </remarks>
    [Fact]
    public void Publications_ReportADocumentNamingAnotherPackage()
    {
        var handler = StubHttpMessageHandler.Answering(HttpStatusCode.OK, Document("geometry", Publication("1.0.0")));

        Assert.Empty(Index(handler).Publications(PackageName.Parse("math"), out var diagnostics));

        Assert.Contains("it names", Assert.Single(diagnostics).Message);
    }

    /// <remarks>
    ///     Dropping the unreadable entry instead would leave a shorter list that still looks like an answer, and
    ///     resolution reading the newest version off the end of it would quietly choose an older one.
    /// </remarks>
    [Fact]
    public void Publications_ReportAVersionThatCannotBeRead_RatherThanAnsweringWithoutIt()
    {
        var handler = StubHttpMessageHandler.Answering(HttpStatusCode.OK, Document("math", Publication("1.0.0"), Publication("not-a-version")));

        Assert.Empty(Index(handler).Publications(PackageName.Parse("math"), out var diagnostics));

        Assert.Contains("'not-a-version' is not a version", Assert.Single(diagnostics).Message);
    }

    /// <remarks>
    ///     Resolution asks about the same package once a round for as many rounds as it takes to settle, and over a
    ///     network that is a request each.
    /// </remarks>
    [Fact]
    public void Publications_AreAskedForOnce_HoweverOftenTheyAreRead()
    {
        var handler = StubHttpMessageHandler.Answering(HttpStatusCode.OK, Document("math", Publication("1.0.0")));
        var index = Index(handler);

        Assert.Single(index.Publications(PackageName.Parse("math"), out _));
        Assert.Single(index.Publications(PackageName.Parse("math"), out _));

        Assert.Single(handler.Requests);
    }

    /// <remarks>A package the registry does not publish is memoized too; asking again would answer the same 404.</remarks>
    [Fact]
    public void Publications_OfAPackageTheRegistryDoesNotPublish_AreAskedForOnce()
    {
        var handler = StubHttpMessageHandler.Answering(HttpStatusCode.NotFound);
        var index = Index(handler);

        Assert.Empty(index.Publications(PackageName.Parse("math"), out _));
        Assert.Empty(index.Publications(PackageName.Parse("math"), out _));

        Assert.Single(handler.Requests);
    }

    /// <remarks>
    ///     Publishing is the one thing that makes the memo knowably wrong and the one thing that clears it — keeping
    ///     the entity tag, so what follows is a revalidation rather than a refetch, and a registry with nothing else
    ///     to say answers it without sending the list again.
    /// </remarks>
    [Fact]
    public void Publications_AfterAPublish_AreRevalidated_AndANotModifiedIsAnsweredFromWhatWasAlreadyRead()
    {
        using var directory = new TemporaryDirectory();
        using var supplied = new SuppliedToken("token-abc");
        var handler = new StubHttpMessageHandler(
            request => request.Method == HttpMethod.Post
                ? StubHttpMessageHandler.Json(HttpStatusCode.Created)
                : request.IfNoneMatch == null
                    ? StubHttpMessageHandler.Json(HttpStatusCode.OK, Document("math", Publication("1.0.0")), "\"one\"")
                    : StubHttpMessageHandler.Json(HttpStatusCode.NotModified)
        );

        var index = Index(handler, credentials: new RegistryCredentials(directory.At("config")));
        Assert.Single(index.Publications(PackageName.Parse("math"), out _));
        Assert.True(index.Publish(Payload(directory), out _));
        var publications = index.Publications(PackageName.Parse("math"), out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(Version.Parse("1.0.0"), Assert.Single(publications).Version);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal("\"one\"", handler.LastRequest.IfNoneMatch);
    }

    /// <remarks>A scope is a segment of the path rather than part of a name, the way a local index gives it a directory.</remarks>
    [Fact]
    public void Publications_OfAScopedPackage_AreAskedForUnderItsScope()
    {
        var handler = StubHttpMessageHandler.Answering(HttpStatusCode.OK, Document("alternativelua/tether", Publication("0.3.1")));

        Assert.Single(Index(handler).Publications(PackageName.Parse("alternativelua/tether"), out _));

        Assert.Equal("/v1/index/alternativelua/tether", handler.LastRequest.Path);
    }

    [Fact]
    public void Publications_ReadTheRequirementsAVersionDeclares()
    {
        var dependencies = """[{"name":"math","requirement":"^1.2"},{"name":"runit","requirement":"^0.4","dev":true}]""";
        var handler = StubHttpMessageHandler.Answering(HttpStatusCode.OK, Document("geometry", Publication("1.0.0", dependencies: dependencies)));

        var publication = Assert.Single(Index(handler).Publications(PackageName.Parse("geometry"), out _));

        // development-only requirements are no part of compiling the package for someone else
        var dependency = Assert.Single(publication.Dependencies);
        Assert.Equal(PackageName.Parse("math"), dependency.Name);
        Assert.Equal(VersionRequirement.Parse("^1.2"), dependency.VersionRequirement);
    }

    [Fact]
    public void Publications_ReportADependencyThatCannotBeRead()
    {
        var handler = StubHttpMessageHandler.Answering(
            HttpStatusCode.OK,
            Document("geometry", Publication("1.0.0", dependencies: """[{"name":"math","requirement":">=2, <1"}]"""))
        );

        Assert.Empty(Index(handler).Publications(PackageName.Parse("geometry"), out var diagnostics));

        Assert.Contains("requires '>=2, <1'", Assert.Single(diagnostics).Message);
    }

    /// <remarks>
    ///     Build metadata is a spelling of a version rather than part of which version it is, so a lock pinning
    ///     <c>1.0.0</c> installs what a registry publishes as <c>1.0.0+ci.7</c>. Guarded here because a registry is
    ///     free to state one and nothing else on this path would notice.
    /// </remarks>
    [Fact]
    public void Publications_StatingBuildMetadata_AreTheVersionWithoutIt()
    {
        var handler = StubHttpMessageHandler.Answering(HttpStatusCode.OK, Document("math", Publication("1.0.0+ci.7")));

        var publication = Assert.Single(Index(handler).Publications(PackageName.Parse("math"), out _));

        Assert.Equal(Version.Parse("1.0.0"), publication.Version);
    }

    /// <remarks>What a lock records as where a version came from is the index as the manifest spells it.</remarks>
    [Fact]
    public void Publications_RecordTheRegistryTheyCameFrom()
    {
        var handler = StubHttpMessageHandler.Answering(HttpStatusCode.OK, Document("math", Publication("1.0.0", "sha256:aa")));

        var publication = Assert.Single(Index(handler).Publications(PackageName.Parse("math"), out _));

        Assert.Equal(Registry, publication.Source);
        Assert.Equal("sha256:aa", publication.Checksum);
        Assert.False(publication.Yanked);
    }

    [Fact]
    public void Publications_StateWhichVersionsTheRegistryHasWithdrawn()
    {
        var handler = StubHttpMessageHandler.Answering(HttpStatusCode.OK, Document("math", Publication("1.0.0"), Publication("1.1.0", yanked: true)));

        var publications = Index(handler).Publications(PackageName.Parse("math"), out _);

        Assert.Equal([false, true], publications.Select(publication => publication.Yanked));
    }

    private static RemotePackageIndex Index(StubHttpMessageHandler handler, string registry = Registry, RegistryCredentials? credentials = null) =>
        new(registry, registry, handler, credentials);

    /// <summary>
    ///     A version about to be published, written into <paramref name="directory" /> so that what is archived and
    ///     sent is a real project rather than a name and a byte array.
    /// </summary>
    private static PackagePayload Payload(TemporaryDirectory directory, string name = "math", string version = "1.0.0")
    {
        var root = directory.At("source");
        directory.Write(
            Path.Combine("source", ConfigReader.ConfigFileName),
            $"project_type = \"library\"\n[package]\nname = \"{name}\"\nversion = \"{version}\"\n"
            + "[files]\nsource_directory = \"src\"\noutput_directory = \"dist\"\n"
        );

        directory.Write(Path.Combine("source", "src", "init.loom"), "export let pi = 3;");
        return new PackagePayload(
            PackageName.Parse(name),
            Version.Parse(version),
            root,
            [ConfigReader.ConfigFileName, Path.Combine("src", "init.loom")]
        );
    }

    /// <summary>The body a registry's index endpoint answers with, its versions in whatever order they are given.</summary>
    private static string Document(string name, params string[] versions) => $$"""{"name":"{{name}}","versions":[{{string.Join(",", versions)}}]}""";

    /// <summary>One version as the index endpoint states it.</summary>
    private static string Publication(string version, string? checksum = null, bool yanked = false, string dependencies = "[]")
    {
        var fields = new List<string> { $"\"version\":\"{version}\"", $"\"dependencies\":{dependencies}" };
        if (checksum != null)
            fields.Add($"\"checksum\":\"{checksum}\"");

        if (yanked)
            fields.Add("\"yanked\":true");

        return $"{{{string.Join(",", fields)}}}";
    }
}
