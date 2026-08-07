using Tomlyn.Serialization;

namespace Loom.Config;

public sealed class FilesConfig
{
    /// <summary>
    ///     The folder inside <see cref="OutputDirectory" /> a build writes its dependencies' compiled output
    ///     to, each package under a folder named by its identity. Not a setting: the package manager and the
    ///     compiler have to agree on it, and a project sharing the name for its own code can rename its own.
    /// </summary>
    public const string PackagesDirectoryName = "packages";

    [TomlPropertyName("source_directory")] public string SourceDirectory { get; set; } = "src";

    [TomlPropertyName("output_directory")] public string OutputDirectory { get; set; } = "dist";
}