using Loom.Packages;

namespace Loom.Testing.Packages;

/// <summary>
///     The tokens a publisher has signed in with. Every case here is given a directory of its own — nothing may
///     write to the real configuration directory, which is somebody's actual sign-in.
/// </summary>
[Collection("Assembly")]
public class RegistryCredentialsTest
{
    [Fact]
    public void Stores_AToken_AndReadsItBack()
    {
        using var directory = new TemporaryDirectory();
        using var supplied = SuppliedToken.None;
        var credentials = new RegistryCredentials(directory.Path);

        Assert.True(credentials.Store(new Uri("https://registry.test"), "token-abc", out var diagnostics));

        Assert.Empty(diagnostics);
        Assert.Equal("token-abc", new RegistryCredentials(directory.Path).TokenFor(new Uri("https://registry.test")));
    }

    /// <remarks>A token belongs to the registry that issued it: there is no spelling of an index that reaches another.</remarks>
    [Fact]
    public void Keeps_OneRegistrysToken_OutOfAnothersReach()
    {
        using var directory = new TemporaryDirectory();
        using var supplied = SuppliedToken.None;
        var credentials = new RegistryCredentials(directory.Path);
        Assert.True(credentials.Store(new Uri("https://registry.test"), "token-abc", out _));
        Assert.True(credentials.Store(new Uri("https://elsewhere.test"), "token-xyz", out _));

        Assert.Equal("token-abc", credentials.TokenFor(new Uri("https://registry.test")));
        Assert.Equal("token-xyz", credentials.TokenFor(new Uri("https://elsewhere.test")));
        Assert.Null(credentials.TokenFor(new Uri("https://third.test")));
    }

    /// <remarks>
    ///     A registry being developed against on a port is a different registry from the one in production, which is
    ///     exactly what it is — so the port is part of how one is named among the credentials.
    /// </remarks>
    [Fact]
    public void Keeps_APortedRegistry_ApartFromTheSameHostOnTheDefaultPort()
    {
        using var directory = new TemporaryDirectory();
        using var supplied = SuppliedToken.None;
        var credentials = new RegistryCredentials(directory.Path);
        Assert.True(credentials.Store(new Uri("http://localhost:8080"), "token-local", out _));

        Assert.Equal("token-local", credentials.TokenFor(new Uri("http://localhost:8080")));
        Assert.Null(credentials.TokenFor(new Uri("http://localhost")));
    }

    /// <remarks>The path a registry is served under is no part of which registry it is, and neither is the case of its host.</remarks>
    [Fact]
    public void Reads_ATokenBackForTheSameHost_HoweverTheIndexIsSpelled()
    {
        using var directory = new TemporaryDirectory();
        using var supplied = SuppliedToken.None;
        var credentials = new RegistryCredentials(directory.Path);
        Assert.True(credentials.Store(new Uri("https://Registry.Test/packages/"), "token-abc", out _));

        Assert.Equal("token-abc", credentials.TokenFor(new Uri("https://registry.test/v1/publish")));
    }

    [Fact]
    public void Replaces_TheTokenStoredForAHost_WithoutTouchingTheOthers()
    {
        using var directory = new TemporaryDirectory();
        using var supplied = SuppliedToken.None;
        var credentials = new RegistryCredentials(directory.Path);
        Assert.True(credentials.Store(new Uri("https://registry.test"), "token-old", out _));
        Assert.True(credentials.Store(new Uri("https://elsewhere.test"), "token-xyz", out _));

        Assert.True(credentials.Store(new Uri("https://registry.test"), "token-new", out _));

        Assert.Equal("token-new", credentials.TokenFor(new Uri("https://registry.test")));
        Assert.Equal("token-xyz", credentials.TokenFor(new Uri("https://elsewhere.test")));
    }

    /// <remarks>A build machine has no business writing a file to hold a token.</remarks>
    [Fact]
    public void Prefers_TheTokenTheEnvironmentSupplies_ToTheOneStored()
    {
        using var directory = new TemporaryDirectory();
        using var supplied = new SuppliedToken("token-from-the-environment");
        var credentials = new RegistryCredentials(directory.Path);
        Assert.True(credentials.Store(new Uri("https://registry.test"), "token-from-the-file", out _));

        Assert.Equal("token-from-the-environment", credentials.TokenFor(new Uri("https://registry.test")));
    }

    /// <remarks>
    ///     A stored token is not even returned for a connection that may not carry it: a bearer token is a password
    ///     in a header, and cleartext hands it to everyone on the path.
    /// </remarks>
    [Fact]
    public void Withholds_AStoredToken_FromACleartextRegistry()
    {
        using var directory = new TemporaryDirectory();
        using var supplied = SuppliedToken.None;
        var credentials = new RegistryCredentials(directory.Path);
        Assert.True(credentials.Store(new Uri("http://registry.test"), "token-abc", out _));

        Assert.Null(credentials.TokenFor(new Uri("http://registry.test")));
    }

    /// <remarks>Even the environment's, since the objection is to the connection rather than to where the token came from.</remarks>
    [Fact]
    public void Withholds_TheEnvironmentsToken_FromACleartextRegistry()
    {
        using var directory = new TemporaryDirectory();
        using var supplied = new SuppliedToken("token-abc");

        Assert.Null(new RegistryCredentials(directory.Path).TokenFor(new Uri("http://registry.test")));
    }

    /// <remarks>The exception is a registry someone is developing against, whose traffic never leaves the machine.</remarks>
    [Fact]
    public void MayCarryToken_ToALoopbackRegistry_OverCleartext()
    {
        Assert.True(RegistryCredentials.MayCarryToken(new Uri("http://localhost:8080")));
        Assert.True(RegistryCredentials.MayCarryToken(new Uri("http://127.0.0.1:8080")));
        Assert.True(RegistryCredentials.MayCarryToken(new Uri("https://registry.test")));
        Assert.False(RegistryCredentials.MayCarryToken(new Uri("http://registry.test")));
    }

    /// <remarks>
    ///     Anyone who can read the file can publish as whoever wrote it, and a file created without this is readable
    ///     by everyone on the machine.
    /// </remarks>
    [Fact]
    public void Writes_AFileOnlyItsOwnerCanRead()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "file modes are the profile's access-control list on Windows.");
        using var directory = new TemporaryDirectory();
        using var supplied = SuppliedToken.None;
        var credentials = new RegistryCredentials(directory.Path);

        Assert.True(credentials.Store(new Uri("https://registry.test"), "token-abc", out _));

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, ModeOf(credentials.FilePath));
    }

    /// <remarks>
    ///     Reported rather than treated as empty and written over: overwriting would throw away every other
    ///     registry's token in order to store one.
    /// </remarks>
    [Fact]
    public void Reports_AFileThatCannotBeRead_RatherThanReplacingIt()
    {
        using var directory = new TemporaryDirectory();
        using var supplied = SuppliedToken.None;
        var credentials = new RegistryCredentials(directory.Path);
        File.WriteAllText(credentials.FilePath, "[[registry]\nhost = \"registry.test\"\n");

        Assert.False(credentials.Store(new Uri("https://registry.test"), "token-abc", out var diagnostics));

        Assert.Contains("cannot be read", Assert.Single(diagnostics).Message);
        Assert.Equal("[[registry]\nhost = \"registry.test\"\n", File.ReadAllText(credentials.FilePath));
    }

    /// <remarks>Nothing has been stored yet, and a file that is not there is not a file that could not be read.</remarks>
    [Fact]
    public void Reads_NoTokenAtAll_BeforeAnybodyHasSignedIn()
    {
        using var directory = new TemporaryDirectory();
        using var supplied = SuppliedToken.None;

        Assert.Null(new RegistryCredentials(directory.At("nothing-here")).TokenFor(new Uri("https://registry.test")));
    }

    /// <summary>The file's mode, or none at all on the platform this is not asked about — which is where it is skipped.</summary>
    private static UnixFileMode ModeOf(string path) => OperatingSystem.IsWindows() ? UnixFileMode.None : File.GetUnixFileMode(path);

    [Fact]
    public void HostOf_NamesARegistry_ByItsHostAndAnyPortThatIsNotTheSchemesOwn()
    {
        Assert.Equal("registry.test", RegistryCredentials.HostOf(new Uri("https://Registry.Test/v1/publish")));
        Assert.Equal("registry.test", RegistryCredentials.HostOf(new Uri("https://registry.test:443")));
        Assert.Equal("localhost:8080", RegistryCredentials.HostOf(new Uri("http://localhost:8080")));
    }
}
