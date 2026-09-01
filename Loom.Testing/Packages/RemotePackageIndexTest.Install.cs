using System.Net;
using Loom.Config;
using Loom.Packages;
using Version = Loom.Config.Version;

namespace Loom.Testing.Packages;

/// <summary>
///     Installing from a registry: fetch, measure against what the registry states, and only then unpack. Bytes off a
///     network are only as good as what they are checked against, which is the one difference from copying a
///     directory that already exists on the machine.
/// </summary>
public partial class RemotePackageIndexTest
{
    [Fact]
    public void Install_UnpacksWhatTheRegistryServes()
    {
        using var directory = new TemporaryDirectory();
        var archive = Archive(directory);
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Bytes(HttpStatusCode.OK, archive));
        var destination = directory.At("packages", "math");

        Assert.True(Index(handler).Install(Published(PackageChecksum.Of(archive)), destination, out var diagnostics));

        Assert.Empty(diagnostics);
        Assert.Equal("/v1/packages/math/1.0.0/download", handler.LastRequest.Path);
        Assert.Equal("export let pi = 3;", File.ReadAllText(Path.Combine(destination, "src", "init.loom")));
        Assert.Equal(Version.Parse("1.0.0"), ConfigReader.LocateFromDirectory(destination, out _)!.Package!.Version);
    }

    /// <remarks>
    ///     The other half of the yank asymmetry: withdrawing a version stops it being chosen, and a lock that already
    ///     pins one installs it exactly as before — a yank is not a reason to break the builds already on it.
    /// </remarks>
    [Fact]
    public void Install_AVersionTheRegistryHasWithdrawn()
    {
        using var directory = new TemporaryDirectory();
        var archive = Archive(directory);
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Bytes(HttpStatusCode.OK, archive));

        Assert.True(Index(handler).Install(Published(PackageChecksum.Of(archive), yanked: true), directory.At("packages", "math"), out var diagnostics));

        Assert.Empty(diagnostics);
    }

    /// <remarks>
    ///     Installing it anyway would be installing unverified bytes, which is the one failure a checksum exists to
    ///     catch. A directory on disk is its own evidence and states none; a registry that states none has not said
    ///     what it served.
    /// </remarks>
    [Fact]
    public void Install_RefusesAVersionTheRegistryStatesNoChecksumFor()
    {
        using var directory = new TemporaryDirectory();
        var handler = StubHttpMessageHandler.Answering(HttpStatusCode.OK);

        Assert.False(Index(handler).Install(Published(null), directory.At("packages", "math"), out var diagnostics));

        Assert.Contains("states no checksum", Assert.Single(diagnostics).Message);
        Assert.Empty(handler.Requests);
    }

    /// <remarks>Half a package where the compiler reads a whole one is an installed package as far as anything downstream can tell.</remarks>
    [Fact]
    public void Install_RefusesWhatDoesNotMatchTheStatedChecksum_AndLeavesNothingBehind()
    {
        using var directory = new TemporaryDirectory();
        var archive = Archive(directory);
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Bytes(HttpStatusCode.OK, archive));
        var destination = directory.At("packages", "math");

        Assert.False(Index(handler).Install(Published(PackageChecksum.Of("something else"u8)), destination, out var diagnostics));

        Assert.Contains("does not match the checksum", Assert.Single(diagnostics).Message);
        Assert.False(Directory.Exists(destination));

        // nor a half-unpacked one beside it under whatever name the staging directory took
        var packages = directory.At("packages");
        Assert.Empty(Directory.Exists(packages) ? Directory.GetFileSystemEntries(packages) : Array.Empty<string>());
    }

    [Fact]
    public void Install_ReportsARefusalToServeTheVersion_InTheRegistrysOwnWords()
    {
        using var directory = new TemporaryDirectory();
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Refusal(HttpStatusCode.Gone, "that version was removed for licensing reasons"));

        Assert.False(Index(handler).Install(Published("sha256:aa"), directory.At("packages", "math"), out var diagnostics));

        Assert.Equal("that version was removed for licensing reasons", Assert.Single(diagnostics).Message);
    }

    [Fact]
    public void Install_ReportsARegistryThatCouldNotBeReached()
    {
        using var directory = new TemporaryDirectory();

        Assert.False(Index(StubHttpMessageHandler.Unreachable()).Install(Published("sha256:aa"), directory.At("packages", "math"), out var diagnostics));

        Assert.Contains("could not reach 'https://registry.test'", Assert.Single(diagnostics).Message);
    }

    /// <remarks>
    ///     An archive is read as though it were written to escape, whoever served it — so the refusal is the same one
    ///     a hostile archive gets anywhere else, and nothing is left where a package would be.
    /// </remarks>
    [Fact]
    public void Install_RefusesAnArchiveThatIsNotOne()
    {
        using var directory = new TemporaryDirectory();
        var served = "not a gzip stream"u8.ToArray();
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Bytes(HttpStatusCode.OK, served));
        var destination = directory.At("packages", "math");

        Assert.False(Index(handler).Install(Published(PackageChecksum.Of(served)), destination, out var diagnostics));

        Assert.NotEmpty(diagnostics);
        Assert.False(Directory.Exists(destination));
    }

    /// <summary>The bytes a registry serves for a version, which is what a publish of the same project would have sent.</summary>
    private static byte[] Archive(TemporaryDirectory directory, string version = "1.0.0")
    {
        var content = PackageArchive.Create(Payload(directory, version: version), out var diagnostics);
        Assert.Empty(diagnostics);
        Assert.NotNull(content);
        return content;
    }

    private static PublishedPackage Published(string? checksum, string version = "1.0.0", bool yanked = false) =>
        new(PackageName.Parse("math"), Version.Parse(version), [], checksum, Registry, yanked);
}
