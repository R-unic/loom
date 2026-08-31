using Tomlyn.Serialization;

namespace Loom.Packages;

/// <summary>
///     The credentials file exactly as written. <see cref="RegistryCredentials" /> is the read form; this one
///     exists because Tomlyn's Native-AOT source generator binds a document rather than a dictionary of tables,
///     the same reason <c>LockFileDocument</c> exists beside <c>LockFile</c>.
/// </summary>
/// <remarks>
///     One entry per registry rather than a table keyed by host, so a host — which carries dots and sometimes a
///     colon — is a value rather than a key that has to be quoted and unquoted to mean itself.
/// </remarks>
// Tomlyn constructs this and fills its collection through CredentialsContext.
// ReSharper disable file ClassNeverInstantiated.Global
// ReSharper disable file CollectionNeverUpdated.Global
internal sealed class CredentialsDocument
{
    [TomlPropertyName("registry")] public List<CredentialsEntry> Registries { get; init; } = [];
}

/// <summary>One registry's token, under the host it belongs to.</summary>
internal sealed class CredentialsEntry
{
    [TomlPropertyName("host")] public string? Host { get; set; }

    [TomlPropertyName("token")] public string? Token { get; set; }
}
