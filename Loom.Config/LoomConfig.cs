using Tomlyn.Serialization;

namespace Loom.Config;

// Tomlyn constructs this and fills its collections by reflection, so nothing here is instantiated or
// added to anywhere an inspection can see.
// ReSharper disable file ClassNeverInstantiated.Global
// ReSharper disable file CollectionNeverUpdated.Global
public sealed class LoomConfig
{
    [TomlIgnore] public string ProjectDirectory { get; set; } = "?";

    [TomlPropertyName("no_emit")] public bool NoEmit { get; set; }

    [TomlPropertyName("project_type")]
    [TomlConverter(typeof(ProjectTypeConverter))]
    public ProjectType ProjectType { get; init; }

    [TomlPropertyName("files")] public FilesConfig Files { get; init; } = new();

    /// <summary>Identity this project is published under; <see langword="null" /> for a project that is never published.</summary>
    [TomlPropertyName("package")] public PackageConfig? Package { get; set; }

    /// <summary>
    ///     The <c>[dependencies]</c> table as written: specifier → either a version requirement string or a table of
    ///     one. <see cref="Dependencies" /> is the read form; this one exists because a TOML value here is not of one
    ///     type. Only <see cref="ConfigReader" /> should need it.
    /// </summary>
    [TomlPropertyName("dependencies")] public Dictionary<string, object> DependencyEntries { get; init; } = [];

    /// <summary>Every package this project depends on, keyed by the specifier it is written under.</summary>
    [TomlIgnore] public Dictionary<PackageName, Dependency> Dependencies { get; } = [];

    /// <summary>Where dependency specifiers are looked up; <see langword="null" /> when the manifest names no registry.</summary>
    [TomlPropertyName("registry")] public RegistryConfig? Registry { get; set; }
}
