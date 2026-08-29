using Tomlyn.Serialization;

namespace Loom.Config;

/// <summary>The <c>[registry]</c> table: where dependency specifiers are looked up.</summary>
// ReSharper disable once ClassNeverInstantiated.Global
public sealed class RegistryConfig
{
    /// <summary>The public registry, which is what a project that names no index of its own resolves from.</summary>
    public const string DefaultIndex = "https://registry.rbx-loom.dev";

    /// <summary>
    ///     Where the index is: the base URL of a registry, or a directory on disk. A directory is as legitimate an
    ///     index as a registry is — one vendored into a repository, or a test's fixtures — so nothing here asks for
    ///     a URL, and <c>IndexLocation</c> is what reads which of the two this is.
    /// </summary>
    [TomlPropertyName("index")] public string Index { get; set; } = DefaultIndex;
}
