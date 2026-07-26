using Tomlyn.Serialization;

namespace Loom.Config;

// ReSharper disable file ClassNeverInstantiated.Global
public sealed class LoomConfig
{
    [TomlIgnore] public string ProjectDirectory { get; set; } = "?";

    [TomlPropertyName("no_emit")] public bool NoEmit { get; set; }

    [TomlPropertyName("debug")] public bool Debug { get; set; }

    [TomlPropertyName("project_type")]
    [TomlConverter(typeof(ProjectTypeConverter))]
    public ProjectType ProjectType { get; init; }

    [TomlPropertyName("files")] public FilesConfig Files { get; init; } = new();
}