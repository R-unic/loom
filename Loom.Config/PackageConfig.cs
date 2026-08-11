using Tomlyn.Serialization;

namespace Loom.Config;

/// <summary>
///     The <c>[package]</c> table: the identity a source-distributed package is published and depended upon under.
///     Absent for projects that are never published, such as games.
/// </summary>
// Tomlyn constructs this and fills its collections by reflection, so nothing here is instantiated or
// added to anywhere an inspection can see.
// ReSharper disable file ClassNeverInstantiated.Global
// ReSharper disable file CollectionNeverUpdated.Global
public sealed class PackageConfig
{
    [TomlPropertyName("name")]
    [TomlConverter(typeof(PackageNameConverter))]
    public PackageName? Name { get; set; }

    [TomlPropertyName("version")]
    [TomlConverter(typeof(VersionConverter))]
    public Version? Version { get; set; }

    /// <summary>Pins the compiler and language semantics this package was written against, e.g. <c>"2026"</c>.</summary>
    [TomlPropertyName("edition")] public string? Edition { get; set; }

    [TomlPropertyName("license")] public string? License { get; set; }

    [TomlPropertyName("authors")] public List<string> Authors { get; init; } = [];

    [TomlPropertyName("description")] public string? Description { get; set; }

    [TomlPropertyName("repository")] public string? Repository { get; set; }

    /// <summary>Governs which Rojo tree(s) this package is vendored into; <see cref="Config.Realm.Shared" /> when unspecified.</summary>
    [TomlPropertyName("realm")]
    [TomlConverter(typeof(RealmConverter))]
    public Realm Realm { get; set; }
}
