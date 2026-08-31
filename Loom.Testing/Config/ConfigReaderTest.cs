using Loom.Config;
using Version = Loom.Config.Version;

namespace Loom.Testing.Config;

public class ConfigReaderTest
{
    private static readonly string[] SingleAuthor = ["alternativelua"];

    private static string CreateTempProjectDirectory(string? tomlContent = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "loom-test-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        if (tomlContent != null)
            File.WriteAllText(Path.Combine(dir, "loom-config.toml"), tomlContent);

        return dir;
    }

    [Fact]
    public void LocateFromDirectory_NullPath_ReturnsNull() => Assert.Null(ConfigReader.LocateFromDirectory(null!));

    [Fact]
    public void LocateFromDirectory_EmptyPath_ReturnsNull() => Assert.Null(ConfigReader.LocateFromDirectory(""));

    [Fact]
    public void LocateFromDirectory_NonexistentDirectory_ReturnsNull()
    {
        var dir = Path.Combine(Path.GetTempPath(), "loom-test-nonexistent-" + Guid.NewGuid());
        Assert.Null(ConfigReader.LocateFromDirectory(dir));
    }

    [Fact]
    public void LocateFromDirectory_DirectoryMissingConfigFile_ReturnsNull()
    {
        var dir = CreateTempProjectDirectory();
        try
        {
            Assert.Null(ConfigReader.LocateFromDirectory(dir));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void LocateFromDirectory_ValidConfig_JoinsProjectDirectoryIntoSourceAndOutputPaths()
    {
        var dir = CreateTempProjectDirectory("project_type = \"game\"\n[files]\nsource_directory = \"src\"\noutput_directory = \"dist\"\n");
        try
        {
            var config = ConfigReader.LocateFromDirectory(dir);

            Assert.NotNull(config);
            Assert.Equal(dir, config.ProjectDirectory);
            Assert.Equal(dir + Path.DirectorySeparatorChar + "src", config.Files.SourceDirectory);
            Assert.Equal(dir + Path.DirectorySeparatorChar + "dist", config.Files.OutputDirectory);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData("game", ProjectType.Game)]
    [InlineData("GAME", ProjectType.Game)]
    [InlineData("library", ProjectType.Library)]
    [InlineData("plugin", ProjectType.Plugin)]
    public void ProjectType_ParsesKnownValues_CaseInsensitive(string toml, ProjectType expected) =>
        Assert.Equal(expected, ReadValid($"project_type = \"{toml}\"\n").ProjectType);

    [Fact]
    public void ProjectType_UnknownValue_ReportsADiagnosticInsteadOfThrowing() =>
        Assert.Contains("unknown project type 'nonsense'", ReadInvalid("project_type = \"nonsense\"\n").Message);

    /// <summary>Reads a manifest the way the compiler does, so validation runs; returns the config and its diagnostics.</summary>
    private static (LoomConfig? Config, IReadOnlyList<ConfigDiagnostic> Diagnostics) Read(string tomlContent)
    {
        var dir = CreateTempProjectDirectory(tomlContent);
        try
        {
            var config = ConfigReader.LocateFromDirectory(dir, out var diagnostics);
            return (config, diagnostics);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static LoomConfig ReadValid(string tomlContent)
    {
        var (config, diagnostics) = Read(tomlContent);
        Assert.Empty(diagnostics);
        Assert.NotNull(config);
        return config;
    }

    private static ConfigDiagnostic ReadInvalid(string tomlContent)
    {
        var (config, diagnostics) = Read(tomlContent);
        Assert.Null(config);
        return Assert.Single(diagnostics);
    }

    [Fact]
    public void LocateFromDirectory_FullManifest_RoundTripsEveryPackageField()
    {
        var config = ReadValid(
            """
            [package]
            name = "alternativelua/tether"
            version = "0.3.1"
            edition = "2026"
            license = "Apache-2.0"
            authors = ["alternativelua"]
            description = "Message-based networking with binary serialization"
            repository = "https://github.com/alternativelua/tether"
            realm = "shared"

            [dependencies]
            serio = "^1.2"
            runit = { version = "^0.4", dev = true }

            [registry]
            index = "https://packages.orrinengine.com"
            """
        );

        var package = config.Package;
        Assert.NotNull(package);
        Assert.Equal(PackageName.Parse("alternativelua/tether"), package.Name);
        Assert.Equal(Version.Parse("0.3.1"), package.Version);
        Assert.Equal("2026", package.Edition);
        Assert.Equal("Apache-2.0", package.License);
        Assert.Equal(SingleAuthor, package.Authors);
        Assert.Equal("Message-based networking with binary serialization", package.Description);
        Assert.Equal("https://github.com/alternativelua/tether", package.Repository);
        Assert.Equal(Realm.Shared, package.Realm);

        Assert.Equal(2, config.Dependencies.Count);
        var serio = config.Dependencies[PackageName.Parse("serio")];
        Assert.Equal(PackageName.Parse("serio"), serio.Name);
        Assert.Equal("^1.2", serio.VersionRequirement.ToString());
        Assert.False(serio.IsDevelopmentOnly);

        var runit = config.Dependencies[PackageName.Parse("runit")];
        Assert.Equal(PackageName.Parse("runit"), runit.Name);
        Assert.Equal("^0.4", runit.VersionRequirement.ToString());
        Assert.True(runit.IsDevelopmentOnly);

        Assert.NotNull(config.Registry);
        Assert.Equal("https://packages.orrinengine.com", config.Registry.Index);
    }

    [Fact]
    public void LocateFromDirectory_ManifestWithoutPackageFields_ParsesWithThemAbsent()
    {
        var config = ReadValid("project_type = \"game\"\n[files]\nsource_directory = \"src\"\n");

        Assert.Null(config.Package);
        Assert.Null(config.Registry);
        Assert.Empty(config.Dependencies);
    }

    [Fact]
    public void Package_MinimalTable_DefaultsTheOptionalFields()
    {
        var config = ReadValid("[package]\nname = \"tether\"\nversion = \"0.3.1\"\n");

        var package = config.Package;
        Assert.NotNull(package);
        Assert.Equal(PackageName.Parse("tether"), package.Name);
        Assert.Null(package.Edition);
        Assert.Null(package.License);
        Assert.Empty(package.Authors);
        Assert.Null(package.Description);
        Assert.Null(package.Repository);
        Assert.Equal(Realm.Shared, package.Realm);
    }

    [Theory]
    [InlineData("shared", Realm.Shared)]
    [InlineData("CLIENT", Realm.Client)]
    [InlineData("server", Realm.Server)]
    public void Package_Realm_ParsesKnownValuesCaseInsensitively(string written, Realm expected)
    {
        var config = ReadValid($"[package]\nname = \"tether\"\nversion = \"0.3.1\"\nrealm = \"{written}\"\n");

        Assert.NotNull(config.Package);
        Assert.Equal(expected, config.Package.Realm);
    }

    [Fact]
    public void Registry_WithoutIndex_DefaultsToTheStaticIndex()
    {
        var config = ReadValid("[registry]\n");

        Assert.NotNull(config.Registry);
        Assert.Equal(RegistryConfig.DefaultIndex, config.Registry.Index);
    }

    [Fact]
    public void Dependencies_TableForm_ParsesWithoutDev()
    {
        var config = ReadValid("[dependencies.serio]\nversion = \">=1.2.0\"\n");

        Assert.Equal(VersionRequirement.Parse(">=1.2.0"), config.Dependencies[PackageName.Parse("serio")].VersionRequirement);
        Assert.False(config.Dependencies[PackageName.Parse("serio")].IsDevelopmentOnly);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("1")]
    [InlineData("1.2")]
    [InlineData("^1.2")]
    [InlineData("~1.2.3")]
    [InlineData("=1.2.3")]
    [InlineData(">=1.0.0")]
    [InlineData("<2.0.0-beta.1")]
    [InlineData(">=1.0.0, <2.0.0")]
    public void Dependencies_AcceptsVersionRequirementForms(string requirement)
    {
        var config = ReadValid($"[dependencies]\nserio = \"{requirement}\"\n");
        Assert.Equal(requirement, config.Dependencies[PackageName.Parse("serio")].VersionRequirement.ToString());
    }

    [Theory]
    [InlineData("[package]\nname = \"te ther\"\nversion = \"0.3.1\"\n", "may only contain letters")]
    [InlineData("[package]\nname = \"scope/name/extra\"\nversion = \"0.3.1\"\n", "at most one '/'")]
    [InlineData("[package]\nname = \"tether\"\nversion = \"0.3\"\n", "exactly three components")]
    [InlineData("[package]\nname = \"tether\"\nversion = \"1.0.0-01\"\n", "leading zeroes")]
    [InlineData("[package]\nname = \"tether\"\nversion = \"0.3.1\"\nrealm = \"studio\"\n", "unknown realm 'studio'")]
    [InlineData("[package]\nversion = \"0.3.1\"\n", "must specify a 'name'")]
    [InlineData("[package]\nname = \"tether\"\n", "must specify a 'version'")]
    [InlineData("[package]\nname = \"tether\"\nversion = \"0.3.1\"\nedition = \"twenty-six\"\n", "four-digit year")]
    [InlineData("[dependencies]\n\"te ther\" = \"^1.2\"\n", "invalid dependency specifier 'te ther'")]
    [InlineData("[dependencies]\nserio = \"not-a-version\"\n", "invalid version requirement 'not-a-version'")]
    [InlineData("[dependencies]\nserio = 3\n", "must be a version requirement string or a table")]
    [InlineData("[dependencies.serio]\ndev = true\n", "must specify a 'version' requirement")]
    [InlineData("[dependencies.serio]\nversion = \"^1.2\"\nbranch = \"main\"\n", "has an unknown key 'branch'")]
    [InlineData("[dependencies.serio]\nversion = 3\n", "written as a string")]
    [InlineData("[dependencies.serio]\nversion = \"^1.2\"\ndev = 1\n", "non-boolean 'dev'")]
    [InlineData("[dependencies]\nserio = \"^1.2\"\nSerio = \"^1.3\"\n", "listed more than once")]
    [InlineData("[registry]\nindex = \"   \"\n", "expected an http or https URL, or a path to a local index")]
    [InlineData("[files]\nsource_directory = \"\"\n", "[files] 'source_directory' cannot be empty")]
    [InlineData("[files]\noutput_directory = \"   \"\n", "[files] 'output_directory' cannot be empty")]
    [InlineData("[files]\nsource_directory = \"/usr/src\"\n", "[files] 'source_directory' must be relative to the project directory, but '/usr/src' is absolute")]
    [InlineData("project_type = \"nonsense\"\n", "unknown project type 'nonsense'")]
    [InlineData("[package\nname = \"tether\"\n", "Expected `]`")]
    public void LocateFromDirectory_MalformedManifest_ReportsADiagnosticInsteadOfThrowing(string tomlContent, string expectedMessage)
    {
        var diagnostic = ReadInvalid(tomlContent);
        Assert.Contains(expectedMessage, diagnostic.Message);
    }

    /// <remarks>
    ///     A trailing separator is the one thing about these paths that is only a matter of writing style, so
    ///     it is trimmed rather than rejected.
    /// </remarks>
    [Fact]
    public void Files_AcceptsARelativeDirectory_WrittenWithATrailingSeparator()
    {
        var config = ReadValid("[files]\nsource_directory = \"source/\"");
        Assert.EndsWith($"{Path.DirectorySeparatorChar}source", config.Files.SourceDirectory);
    }

    /// <remarks>
    ///     Position tracking only survives for a genuine TOML syntax error: a semantic one, like an invalid
    ///     'version', is caught by <see cref="ConfigReader" /> after the whole file has already deserialized, so
    ///     it carries no position (see <see cref="LocateFromDirectory_MalformedManifest_ReportsADiagnosticInsteadOfThrowing" />).
    /// </remarks>
    [Fact]
    public void LocateFromDirectory_MalformedManifest_ReportsWhereTheProblemIs()
    {
        var diagnostic = ReadInvalid("[package\nname = \"tether\"\n");

        Assert.Equal(1, diagnostic.Line);
        Assert.Equal(9, diagnostic.Column);
        Assert.StartsWith("(1,9): ", diagnostic.ToString());
    }

    [Fact]
    public void LocateFromDirectory_ManifestMissingRequiredPackageFields_ReportsEachOne()
    {
        var (config, diagnostics) = Read("[package]\n");

        Assert.Null(config);
        Assert.Equal(2, diagnostics.Count);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("'name'"));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("'version'"));
    }

    [Fact]
    public void LocateFromDirectory_ValidManifest_ReportsNoDiagnostics()
    {
        var dir = CreateTempProjectDirectory("project_type = \"game\"\n");
        try
        {
            Assert.NotNull(ConfigReader.LocateFromDirectory(dir, out var diagnostics));
            Assert.Empty(diagnostics);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void LocateFromDirectory_MissingManifest_ReportsNoDiagnostics()
    {
        var dir = CreateTempProjectDirectory();
        try
        {
            Assert.Null(ConfigReader.LocateFromDirectory(dir, out var diagnostics));
            Assert.Empty(diagnostics);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ConfigDiagnostic_WithoutAPosition_PrintsOnlyItsMessage() =>
        Assert.Equal("[package] must specify a 'name'.", new ConfigDiagnostic("[package] must specify a 'name'.").ToString());

    [Fact]
    public void Dependency_ToString_MarksDevelopmentOnlyDependencies()
    {
        var config = ReadValid("[dependencies]\nserio = \"^1.2\"\nrunit = { version = \"^0.4\", dev = true }\n");

        Assert.Equal("^1.2", config.Dependencies[PackageName.Parse("serio")].ToString());
        Assert.Equal("^0.4 (dev)", config.Dependencies[PackageName.Parse("runit")].ToString());
    }

    [Fact]
    public void Realms_ReadEachDirectory()
    {
        var config = ReadValid("[realms]\nclient = \"client\"\nserver = \"server\"\nnet = \"shared\"\n");

        Assert.Equal(Realm.Client, config.Realms["client"]);
        Assert.Equal(Realm.Server, config.Realms["server"]);
        Assert.Equal(Realm.Shared, config.Realms["net"]);
    }

    /// <remarks>Two spellings of one directory are one entry, or a lookup finds whichever was written.</remarks>
    [Theory]
    [InlineData("\"net/server\"")]
    [InlineData("\"net\\\\server\"")]
    [InlineData("\"/net/server/\"")]
    public void Realms_NormalizeTheDirectoryTheyAreKeyedBy(string written)
    {
        var config = ReadValid($"[realms]\n{written} = \"server\"\n");

        Assert.Equal(Realm.Server, config.Realms["net/server"]);
    }

    [Fact]
    public void Realms_RejectAnUnknownRealm() =>
        Assert.Contains("expected \"shared\", \"client\" or \"server\"", ReadInvalid("[realms]\nclient = \"clientside\"\n").ToString());

    [Fact]
    public void Realms_RejectANonStringRealm() =>
        Assert.Contains("must name a realm", ReadInvalid("[realms]\nclient = 3\n").ToString());

    /// <remarks>
    ///     A separator on its own normalises to nothing, which would otherwise key the source directory itself
    ///     and put every file in one realm by accident. Rooted paths are rejected by the same guard, but only
    ///     a drive letter is rooted on every OS this runs on, so what is asserted here is the portable case.
    /// </remarks>
    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"/\"")]
    public void Realms_RejectADirectoryThatNamesNothing(string written) =>
        Assert.Contains("non-empty path relative to the source directory", ReadInvalid($"[realms]\n{written} = \"client\"\n").ToString());

    [Fact]
    public void Realms_RejectTheSameDirectoryTwice() =>
        Assert.Contains("more than once", ReadInvalid("[realms]\n\"net/server\" = \"server\"\n\"net\\\\server\" = \"client\"\n").ToString());

    /// <remarks>
    ///     Answered without Path.IsPathRooted, which reads a drive letter as rooted on Windows and as an
    ///     ordinary directory name everywhere else - so the same manifest would be rejected on one CI leg
    ///     and read as a directory called 'C:' on the next.
    /// </remarks>
    [Fact]
    public void Realms_RejectADriveLetterOnEveryPlatform() =>
        Assert.Contains("relative to the source directory", ReadInvalid("[realms]\n\"C:/game/client\" = \"client\"\n").ToString());

    [Fact]
    public void Realms_DefaultToNone() => Assert.Empty(ReadValid("project_type = \"game\"\n").Realms);
}