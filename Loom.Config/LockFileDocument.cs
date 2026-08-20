using Tomlyn.Serialization;

namespace Loom.Config;

/// <summary>
///     A lock file exactly as written, before its identities are parsed. <see cref="LockFile" /> is the read form;
///     this one exists for the same reason <see cref="PackageConfig.NameEntry" /> does — Tomlyn's Native-AOT source
///     generator does not honor a per-member <c>TomlConverter</c>, so names and versions are read as text and
///     parsed by <see cref="LockFileReader" />. Only <see cref="LockFileReader" /> should need it.
/// </summary>
/// <remarks>
///     A <c>[[package]]</c> entry stays a table of raw values rather than becoming a class of its own, for the
///     same reason <see cref="LoomConfig.DependencyEntries" /> does: a key this compiler has never heard of is a
///     lock file it cannot claim to have read, and a typed shell would drop one silently.
/// </remarks>
// Tomlyn constructs this and fills its collections through LockFileContext, so nothing here is instantiated or
// added to anywhere an inspection can see.
// ReSharper disable file ClassNeverInstantiated.Global
// ReSharper disable file CollectionNeverUpdated.Global
internal sealed class LockFileDocument
{
    /// <summary>The lock format the file was written in; absent is a problem rather than a default.</summary>
    [TomlPropertyName("version")] public int? FormatVersion { get; set; }

    [TomlPropertyName("package")] public List<Dictionary<string, object>> Packages { get; init; } = [];
}
