using Tomlyn.Serialization;

namespace Loom.Config;

/// <summary>
///     The <c>[package]</c> table: the identity a source-distributed package is published and depended upon under.
///     Absent for projects that are never published, such as games.
/// </summary>
// Tomlyn constructs this and fills its collections through LoomConfigContext, so nothing here is
// instantiated or added to anywhere an inspection can see.
// ReSharper disable file ClassNeverInstantiated.Global
// ReSharper disable file CollectionNeverUpdated.Global
public sealed class PackageConfig
{
    /// <summary>
    ///     <c>name</c> as written. <see cref="Name" /> is the read form; this one exists because Tomlyn's
    ///     Native-AOT source generator does not honor a per-member <c>TomlConverter</c>, so the identity is read
    ///     as text and parsed by <see cref="ConfigReader" /> instead. Only <see cref="ConfigReader" /> should
    ///     need it.
    /// </summary>
    [TomlPropertyName("name")] public string? NameEntry { get; set; }

    [TomlIgnore] public PackageName? Name { get; set; }

    /// <summary>
    ///     <c>version</c> as written; parsed the same way and for the same reason as <see cref="NameEntry" />.
    /// </summary>
    [TomlPropertyName("version")] public string? VersionEntry { get; set; }

    [TomlIgnore] public Version? Version { get; set; }

    /// <summary>Pins the compiler and language semantics this package was written against, e.g. <c>"2026"</c>.</summary>
    [TomlPropertyName("edition")] public string? Edition { get; set; }

    [TomlPropertyName("license")] public string? License { get; set; }

    [TomlPropertyName("authors")] public List<string> Authors { get; init; } = [];

    [TomlPropertyName("description")] public string? Description { get; set; }

    [TomlPropertyName("repository")] public string? Repository { get; set; }

    /// <summary>
    ///     <c>realm</c> as written; parsed the same way and for the same reason as <see cref="NameEntry" />.
    /// </summary>
    [TomlPropertyName("realm")] public string? RealmEntry { get; set; }

    /// <summary>Governs which Rojo tree(s) this package is vendored into; <see cref="Realm.Shared" /> when unspecified.</summary>
    [TomlIgnore] public Realm Realm { get; set; }
}
