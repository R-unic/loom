using Tomlyn.Serialization;

namespace Loom.Config;

// Tomlyn constructs this and fills its collections through LoomConfigContext, so nothing here is
// instantiated or added to anywhere an inspection can see.
// ReSharper disable file ClassNeverInstantiated.Global
// ReSharper disable file CollectionNeverUpdated.Global
public sealed class LoomConfig
{
    [TomlIgnore] public string ProjectDirectory { get; set; } = "?";

    [TomlPropertyName("no_emit")] public bool NoEmit { get; set; }

    /// <summary>
    ///     <c>project_type</c> as written. <see cref="ProjectType" /> is the read form; this one exists because
    ///     Tomlyn's Native-AOT source generator does not honor a per-member <c>TomlConverter</c>, so the enum is
    ///     read as text and matched by <see cref="ConfigReader" /> instead. Only <see cref="ConfigReader" /> should
    ///     need it.
    /// </summary>
    [TomlPropertyName("project_type")] public string? ProjectTypeEntry { get; init; }

    /// <summary>The project's kind; <see cref="Config.ProjectType.Game" /> when the manifest names none.</summary>
    [TomlIgnore] public ProjectType ProjectType { get; set; }

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

    /// <summary>
    ///     The <c>[realms]</c> table as written: a directory under the source directory → the realm the code in
    ///     it runs in. <see cref="Realms" /> is the read form; this one exists so a value that is not a realm is
    ///     reported as one bad line rather than as a deserialization failure. Only <see cref="ConfigReader" />
    ///     should need it.
    /// </summary>
    [TomlPropertyName("realms")] public Dictionary<string, object> RealmEntries { get; init; } = [];

    /// <summary>
    ///     Which realm each of the project's directories runs in, keyed by a path relative to the source
    ///     directory. A file takes the realm of the longest directory naming it, and <see cref="Realm.Shared" />
    ///     when none does - so a project that declares nothing here has one realm and no boundary to cross.
    /// </summary>
    [TomlIgnore] public Dictionary<string, Realm> Realms { get; } = [];
}
