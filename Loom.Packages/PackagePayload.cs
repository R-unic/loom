using Loom.Config;

namespace Loom.Packages;

/// <summary>
///     One version of a package as it is about to be published: the identity it publishes under, and the files that
///     make it up.
/// </summary>
/// <remarks>
///     A Loom package is distributed as source — an index's versions are Loom projects, which is what lets the
///     compiler read one as a source root — so what a publish transfers is a file list rather than a built artifact.
///     Which files those are is <see cref="PackagePublisher" />'s answer and where they go is the index's, so neither
///     has to know the other's half.
/// </remarks>
/// <param name="root">The directory <see cref="Files" /> are relative to, which is the project being published.</param>
/// <param name="files">
///     Every file to publish, as a path relative to <paramref name="root" />. Ordered, so two publishes of the same
///     project describe themselves the same way.
/// </param>
public sealed class PackagePayload(PackageName name, Version version, string root, IReadOnlyList<string> files)
{
    public PackageName Name { get; } = name;

    public Version Version { get; } = version;

    public string Root { get; } = root;

    public IReadOnlyList<string> Files { get; } = files;

    public override string ToString() => $"{Name} {Version} ({Files.Count} file{(Files.Count == 1 ? "" : "s")})";
}
