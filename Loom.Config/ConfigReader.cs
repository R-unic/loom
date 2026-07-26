using Tomlyn;

namespace Loom.Config;

public static class ConfigReader
{
    private const string ConfigFileName = "loom-config.toml";

    public static LoomConfig? LocateFromDirectory(string directoryPath)
    {
        if (string.IsNullOrEmpty(directoryPath))
            return null;

        if (!Directory.Exists(directoryPath))
            return null;

        var configPath = Path.Combine(directoryPath, ConfigFileName);
        if (!File.Exists(configPath))
            return null;

        var config = ReadFile(configPath);
        if (config == null)
            return null;

        config.ProjectDirectory = directoryPath;
        return config;
    }

    private static LoomConfig? ReadFile(string path)
    {
        if (!File.Exists(path))
            throw new Exception($"Could not find Loom configuration file at {path}.");

        var tomlContent = File.ReadAllText(path);
        var config = TomlSerializer.Deserialize<LoomConfig>(tomlContent);
        if (config == null)
            return null;

        config.ProjectDirectory = Path.GetDirectoryName(path) ?? "?";
        config.Files.SourceDirectory = config.ProjectDirectory + Path.DirectorySeparatorChar + config.Files.SourceDirectory.TrimEnd(Path.DirectorySeparatorChar);
        config.Files.OutputDirectory = config.ProjectDirectory + Path.DirectorySeparatorChar + config.Files.OutputDirectory.TrimEnd(Path.DirectorySeparatorChar);
        return config;
    }
}