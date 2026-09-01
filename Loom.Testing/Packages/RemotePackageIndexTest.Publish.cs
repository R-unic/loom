using System.Net;
using Loom.Packages;

namespace Loom.Testing.Packages;

/// <summary>
///     Publishing to a registry: the archive, and the token that says who is entitled to put it there. A token is a
///     password in a header, so where it comes from and whether it may be sent at all is decided before anything
///     leaves the machine.
/// </summary>
public partial class RemotePackageIndexTest
{
    [Fact]
    public void Publish_SendsTheArchive_UnderTheTokenStoredForTheRegistry()
    {
        using var directory = new TemporaryDirectory();
        using var supplied = SuppliedToken.None;
        var credentials = new RegistryCredentials(directory.At("config"));
        Assert.True(credentials.Store(new Uri(Registry), "token-abc", out _));
        var handler = new StubHttpMessageHandler(
            request => StubHttpMessageHandler.Json(HttpStatusCode.Created, $$"""{"checksum":"{{PackageChecksum.Of(request.Body)}}"}""")
        );

        Assert.True(Index(handler, credentials: credentials).Publish(Payload(directory), out var diagnostics));

        Assert.Empty(diagnostics);
        var request = handler.LastRequest;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/v1/publish", request.Path);
        Assert.Equal("Bearer token-abc", request.Authorization);
        Assert.Equal("application/gzip", request.ContentType);

        // the archive is the whole of what is sent: the registry reads the manifest out of it rather than being told
        Assert.True(PackageArchive.Extract(request.Body, directory.At("unpacked"), out _));
        Assert.Equal("export let pi = 3;", File.ReadAllText(directory.At("unpacked", "src", "init.loom")));
    }

    /// <remarks>A build machine has no business writing a file to hold a token, so the environment is read first.</remarks>
    [Fact]
    public void Publish_UsesTheTokenTheEnvironmentSupplies_OverAnythingStored()
    {
        using var directory = new TemporaryDirectory();
        using var supplied = new SuppliedToken("token-from-the-environment");
        var credentials = new RegistryCredentials(directory.At("config"));
        Assert.True(credentials.Store(new Uri(Registry), "token-from-the-file", out _));
        var handler = StubHttpMessageHandler.Answering(HttpStatusCode.Created);

        Assert.True(Index(handler, credentials: credentials).Publish(Payload(directory), out _));

        Assert.Equal("Bearer token-from-the-environment", handler.LastRequest.Authorization);
    }

    [Fact]
    public void Publish_WithNoToken_NamesTheCommandThatStoresOne()
    {
        using var directory = new TemporaryDirectory();
        using var supplied = SuppliedToken.None;
        var handler = StubHttpMessageHandler.Answering(HttpStatusCode.Created);

        Assert.False(Index(handler, credentials: new RegistryCredentials(directory.At("config"))).Publish(Payload(directory), out var diagnostics));

        Assert.Contains("loom login", Assert.Single(diagnostics).Message);
        Assert.Empty(handler.Requests);
    }

    /// <remarks>
    ///     A bearer token is a password in a header, so cleartext carries it to everyone on the path. Refused before
    ///     the archive is built, since there is nothing to send it with.
    /// </remarks>
    [Fact]
    public void Publish_OverCleartext_IsRefusedBeforeAnythingIsSent()
    {
        using var directory = new TemporaryDirectory();
        using var supplied = new SuppliedToken("token-abc");
        var handler = StubHttpMessageHandler.Answering(HttpStatusCode.Created);

        Assert.False(Index(handler, "http://registry.test").Publish(Payload(directory), out var diagnostics));

        Assert.Contains("is not served over https", Assert.Single(diagnostics).Message);
        Assert.Empty(handler.Requests);
    }

    /// <remarks>The exception is a registry someone is developing against, whose traffic never leaves the machine.</remarks>
    [Fact]
    public void Publish_ToALoopbackRegistry_MayCarryATokenOverCleartext()
    {
        using var directory = new TemporaryDirectory();
        using var supplied = new SuppliedToken("token-abc");
        var handler = StubHttpMessageHandler.Answering(HttpStatusCode.Created);

        Assert.True(Index(handler, "http://localhost:8080").Publish(Payload(directory), out var diagnostics));

        Assert.Empty(diagnostics);
        Assert.Equal("Bearer token-abc", handler.LastRequest.Authorization);
    }

    [Fact]
    public void Publish_ReportsARefusal_InTheRegistrysOwnWords()
    {
        using var directory = new TemporaryDirectory();
        using var supplied = new SuppliedToken("token-abc");
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Refusal(HttpStatusCode.Forbidden, "the scope 'alternativelua' belongs to somebody else"));

        Assert.False(Index(handler).Publish(Payload(directory), out var diagnostics));

        Assert.Equal("the scope 'alternativelua' belongs to somebody else", Assert.Single(diagnostics).Message);
    }

    /// <remarks>
    ///     The version is published either way. This is not a failure to publish but a failure to agree about what
    ///     was published, and a publisher who is not told would go on believing they shipped what they built.
    /// </remarks>
    [Fact]
    public void Publish_ReportsARegistryStatingAnotherChecksum_WhileSayingTheVersionWasAccepted()
    {
        using var directory = new TemporaryDirectory();
        using var supplied = new SuppliedToken("token-abc");
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(HttpStatusCode.Created, """{"checksum":"sha256:00"}"""));

        Assert.False(Index(handler).Publish(Payload(directory), out var diagnostics));

        var message = Assert.Single(diagnostics).Message;
        Assert.Contains("accepted", message);
        Assert.Contains("sha256:00", message);
    }

    /// <remarks>A registry stating nothing about what it took has not disagreed with it.</remarks>
    [Fact]
    public void Publish_AcceptsARegistryThatStatesNoChecksum()
    {
        using var directory = new TemporaryDirectory();
        using var supplied = new SuppliedToken("token-abc");

        Assert.True(Index(StubHttpMessageHandler.Answering(HttpStatusCode.Created, "{}")).Publish(Payload(directory), out var diagnostics));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Publish_ReportsAPayloadWhoseFilesAreNotThere()
    {
        using var directory = new TemporaryDirectory();
        using var supplied = new SuppliedToken("token-abc");
        var payload = Payload(directory);
        File.Delete(Path.Combine(payload.Root, "src", "init.loom"));
        var handler = StubHttpMessageHandler.Answering(HttpStatusCode.Created);

        Assert.False(Index(handler).Publish(payload, out var diagnostics));

        Assert.Contains("could not read", Assert.Single(diagnostics).Message);
        Assert.Empty(handler.Requests);
    }
}
