using Tomlyn.Serialization;

namespace Loom.Config;

/// <summary>The <c>[registry]</c> table: where dependency specifiers are looked up.</summary>
// ReSharper disable once ClassNeverInstantiated.Global
public sealed class RegistryConfig
{
    /// <summary>
    ///     What a <c>[registry]</c> table naming no index of its own means. Only that table reaches this: a manifest
    ///     writing no <c>[registry]</c> at all leaves <see cref="LoomConfig.Registry" /> null, which is a project
    ///     with no index rather than a project with this one.
    /// </summary>
    /// <remarks>
    ///     This has to name something that answers the registry API, not merely something that answers. A URL that
    ///     is not a registry is not reported as one: every endpoint under it comes back 404, which is exactly how an
    ///     index says a package is not published — so a default pointing somewhere plausible would tell everyone who
    ///     never wrote an <c>index</c> that the package they asked for does not exist.
    /// </remarks>
    public const string DefaultIndex = "https://packages.orrinengine.com";

    /// <summary>
    ///     Where the index is: a registry's base URL, a static index served over http, or a directory on disk. A
    ///     directory is as legitimate an index as a registry is — one vendored into a repository, or a test's
    ///     fixtures — so nothing here asks for a URL, and <c>IndexLocation</c> is what reads which of the two it is.
    /// </summary>
    [TomlPropertyName("index")] public string Index { get; set; } = DefaultIndex;
}
