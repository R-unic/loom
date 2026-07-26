using Loom.Config;
using Tomlyn;

namespace Loom.Testing;

public class ConfigReaderTest
{
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
    public void ProjectTypeConverter_ParsesKnownValues_CaseInsensitive(string toml, ProjectType expected)
    {
        var config = TomlSerializer.Deserialize<LoomConfig>($"project_type = \"{toml}\"");
        Assert.NotNull(config);
        Assert.Equal(expected, config.ProjectType);
    }

    [Fact]
    public void ProjectTypeConverter_UnknownValue_Throws()
    {
        var ex = Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<LoomConfig>("project_type = \"nonsense\""));
        Assert.Contains("unknown project type 'nonsense'", ex.Message);
    }

    [Fact]
    public void Debug_DefaultsToFalse()
    {
        var config = TomlSerializer.Deserialize<LoomConfig>("project_type = \"game\"");
        Assert.NotNull(config);
        Assert.False(config.Debug);
    }

    [Fact]
    public void Debug_ParsesTrue()
    {
        var config = TomlSerializer.Deserialize<LoomConfig>("project_type = \"game\"\ndebug = true");
        Assert.NotNull(config);
        Assert.True(config.Debug);
    }
}