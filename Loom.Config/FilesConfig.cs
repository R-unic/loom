using Tomlyn.Serialization;

namespace Loom.Config;

public sealed class FilesConfig
{
    [TomlPropertyName("source_directory")] public string SourceDirectory { get; set; } = "src";

    [TomlPropertyName("output_directory")] public string OutputDirectory { get; set; } = "dist";
}