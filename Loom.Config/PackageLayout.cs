namespace Loom.Config;

/// <summary>
///     Where a project's dependencies are installed: <c>&lt;project&gt;/packages/&lt;scope&gt;/&lt;name&gt;</c>, one
///     directory per package, a scope being simply a folder above the name. The same shape their compiled output
///     takes inside the output directory, so a package is in the same place in both of a build's trees.
/// </summary>
/// <remarks>
///     Not a setting, for the reason <see cref="FilesConfig.PackagesDirectoryName" /> is not one: a package manager
///     writes these directories and the compiler reads them, so the two have to agree without either asking. What
///     the layout deliberately does not encode is the version — that is <see cref="LockFile" />'s single answer, and
///     a directory named by version would let one build compile two copies of a package, which nothing downstream
///     supports.
/// </remarks>
public static class PackageLayout
{
    /// <summary>The directory every package <paramref name="project" /> installs lives under.</summary>
    public static string DirectoryOf(LoomConfig project) => Path.Combine(project.ProjectDirectory, FilesConfig.PackagesDirectoryName);

    /// <summary>Where <paramref name="package" /> is installed for <paramref name="project" />.</summary>
    public static string DirectoryOf(LoomConfig project, PackageName package) =>
        Path.Combine(DirectoryOf(project), package.Scope ?? string.Empty, package.Name);

    /// <summary>
    ///     Where every package <paramref name="lockFile" /> locks is installed. This is the whole lock, not the part
    ///     a build reaches: which of them it actually needs is answered by walking the manifests, and a lock
    ///     covering more than one build uses is a package manager's business rather than an error.
    /// </summary>
    public static Dictionary<PackageName, string> DirectoriesOf(LoomConfig project, LockFile lockFile) =>
        lockFile.Packages.ToDictionary(package => package.Name, package => DirectoryOf(project, package.Name));
}
