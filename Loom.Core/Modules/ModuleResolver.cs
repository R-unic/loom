using System.Diagnostics.CodeAnalysis;
using Loom.Config;
using Loom.Core.Pipeline;
using Loom.Core.Text;

namespace Loom.Core.Modules;

/// <summary>
///     Maps a module specifier as written in source (<c>"./math"</c>, <c>"serio"</c>) onto a
///     <see cref="SourceFile" /> of the compilation unit. Specifiers are extension-less; a relative one is read
///     from the importing file's directory and a bare one from the source directory of the root publishing the
///     package it names. Either way <c>"./math"</c> matches <c>math.loom</c> or <c>math/init.loom</c>,
///     mirroring how Rojo folds an <c>init</c> file into its folder.
/// </summary>
/// <remarks>
///     Paths are compared case-sensitively even where the file system is not, because Roblox requires are.
///     Declaration files are ambient globals rather than modules, so they are never importable.
///     A relative specifier may not leave the importing file's own root: reaching out of one project and into
///     another is what a package specifier is for, not what <c>"../"</c> is for.
/// </remarks>
public sealed class ModuleResolver
{
    private const string IndexFileName = "init";

    /// <summary>How many leading segments of a specifier may name the package: <c>name</c> or <c>scope/name</c>.</summary>
    private const int MaximumPackageSegments = 2;

    private readonly Dictionary<string, SourceFile> _modulesByPath;

    /// <summary>The same modules keyed without regard to case, to tell a typo from a casing mistake.</summary>
    private readonly Dictionary<string, SourceFile> _modulesByPathIgnoringCase;

    private readonly SourceRootSet _roots;

    public ModuleResolver(IEnumerable<SourceFile> files, SourceRootSet roots)
    {
        _modulesByPath = new Dictionary<string, SourceFile>(StringComparer.Ordinal);
        _modulesByPathIgnoringCase = new Dictionary<string, SourceFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files.Where(file => !file.IsDeclaration))
        {
            var path = Path.GetFullPath(file.AbsolutePath);
            _modulesByPath.TryAdd(path, file);
            _modulesByPathIgnoringCase.TryAdd(path, file);
        }

        _roots = roots;
    }

    public static bool IsRelativeSpecifier(string specifier) =>
        specifier.StartsWith("./", StringComparison.Ordinal) || specifier.StartsWith("../", StringComparison.Ordinal);

    public ModuleResolution Resolve(SourceFile importingFile, string specifier) =>
        IsRelativeSpecifier(specifier) ? ResolveRelative(importingFile, specifier) : ResolvePackage(importingFile, specifier);

    /// <summary>The specifier that would have named <paramref name="module" />, written from <paramref name="importingFile" />.</summary>
    /// <remarks>
    ///     A module of another root is named by its package rather than by a path, since no relative path
    ///     reaches it: a relative specifier stops at the edge of the root it is written in.
    /// </remarks>
    public string SpecifierOf(SourceFile importingFile, SourceFile module)
    {
        var moduleRoot = _roots.Of(module);
        if (moduleRoot != _roots.Of(importingFile) && moduleRoot.Package?.Name is { } packageName)
        {
            var subpath = ModulePath(moduleRoot.SourceDirectory, module);
            return subpath.Length == 0 ? packageName.ToString() : $"{packageName}/{subpath}";
        }

        var importingDirectory = Path.GetDirectoryName(Path.GetFullPath(importingFile.AbsolutePath)) ?? "";
        var specifier = ModulePath(importingDirectory, module);
        return specifier.StartsWith("./", StringComparison.Ordinal) || specifier.StartsWith("../", StringComparison.Ordinal) ? specifier : "./" + specifier;
    }

    private ModuleResolution ResolveRelative(SourceFile importingFile, string specifier)
    {
        var importingDirectory = Path.GetDirectoryName(Path.GetFullPath(importingFile.AbsolutePath));
        if (importingDirectory == null)
            return ModuleResolution.Failed(ModuleResolutionStatus.NotFound);

        var basePath = Path.GetFullPath(Path.Combine(importingDirectory, specifier));
        return _roots.Of(importingFile).Contains(basePath)
            ? ResolveAt(importingFile, basePath)
            : ModuleResolution.Failed(ModuleResolutionStatus.OutsideSourceDirectory);
    }

    /// <summary>
    ///     Resolves <c>"pkg"</c> or <c>"pkg/nested/module"</c> against the root publishing <c>pkg</c>: the bare
    ///     package names that root's entry module, <c>init.loom</c> at the top of its source directory, and
    ///     anything after the package name is a path within that same source directory.
    /// </summary>
    /// <remarks>
    ///     A package is only importable by the projects that actually declare it, so that a package reachable
    ///     only because something else in the build depends on it cannot be imported by accident. The root's
    ///     own files are exempt: a package always refers to itself by name.
    /// </remarks>
    private ModuleResolution ResolvePackage(SourceFile importingFile, string specifier)
    {
        if (!TrySplitPackageSpecifier(specifier, out var package, out var subpath))
            return ModuleResolution.Failed(ModuleResolutionStatus.UnsupportedSpecifier);

        var root = _roots.WithPackage(package);
        if (root == null)
            return ModuleResolution.Failed(ModuleResolutionStatus.PackageNotFound, package);

        var importingRoot = _roots.Of(importingFile);
        if (root != importingRoot && !importingRoot.Config.Dependencies.ContainsKey(package))
            return ModuleResolution.Failed(ModuleResolutionStatus.UndeclaredDependency, package);

        if (subpath.Length == 0)
            return ResolveAt(importingFile, root.SourceDirectory, package, indexOnly: true);

        var basePath = Path.GetFullPath(Path.Combine(root.SourceDirectory, subpath));
        return root.Contains(basePath)
            ? ResolveAt(importingFile, basePath, package)
            : ModuleResolution.Failed(ModuleResolutionStatus.OutsideSourceDirectory, package);
    }

    /// <param name="package"></param>
    /// <param name="indexOnly">
    ///     Set when <paramref name="basePath" /> is a directory rather than a module path, as it is for a bare
    ///     package specifier: only the <c>init</c> file inside it can be meant, never a sibling file beside it.
    /// </param>
    /// <param name="importingFile"></param>
    /// <param name="basePath"></param>
    private ModuleResolution ResolveAt(SourceFile importingFile, string basePath, PackageName? package = null, bool indexOnly = false)
    {
        SourceFile? caseInsensitiveMatch = null;
        foreach (var candidate in GetCandidatePaths(basePath, indexOnly))
        {
            if (_modulesByPath.TryGetValue(candidate, out var file))
                return file == importingFile
                    ? ModuleResolution.Failed(ModuleResolutionStatus.SelfImport, package)
                    : ModuleResolution.Resolved(file);

            caseInsensitiveMatch ??= _modulesByPathIgnoringCase.GetValueOrDefault(candidate);
        }

        return ModuleResolution.NotFound(caseInsensitiveMatch, package);
    }

    /// <summary>
    ///     Splits a bare specifier into the package it names and the path within that package's source
    ///     directory. <c>"scope/name/module"</c> is ambiguous — a scoped package with a module under it, or an
    ///     unscoped one with two path segments — so the scoped reading wins when a root actually publishes that
    ///     scoped name, and the unscoped reading is what an unresolvable specifier is reported as, that being
    ///     the far likelier thing to have been meant.
    /// </summary>
    private bool TrySplitPackageSpecifier(string specifier, [NotNullWhen(true)] out PackageName? package, out string subpath)
    {
        package = null;
        subpath = "";

        var segments = specifier.Split('/');
        if (Array.Exists(segments, segment => segment.Length == 0))
            return false;

        for (var count = Math.Min(MaximumPackageSegments, segments.Length); count >= 1; count--)
        {
            if (!PackageName.TryParse(string.Join('/', segments.Take(count)), out var candidate))
                continue;

            package = candidate;
            subpath = string.Join('/', segments.Skip(count));
            if (_roots.WithPackage(candidate) != null)
                return true;
        }

        return package != null;
    }

    /// <summary>The path naming <paramref name="module" /> from <paramref name="directory" />, extension-less and <c>/</c>-separated, an <c>init</c> file folded into its folder.</summary>
    private static string ModulePath(string directory, SourceFile module)
    {
        var relativePath = Path.GetRelativePath(directory, Path.GetFullPath(module.AbsolutePath));
        var withoutExtension = relativePath[..^FileManager.LoomExtension.Length];
        if (Path.GetFileName(withoutExtension) == IndexFileName)
            withoutExtension = Path.GetDirectoryName(withoutExtension) ?? withoutExtension;

        return withoutExtension == "." ? "" : withoutExtension.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static IEnumerable<string> GetCandidatePaths(string basePath, bool indexOnly)
    {
        if (!indexOnly)
            yield return basePath + FileManager.LoomExtension;

        yield return Path.Combine(basePath, IndexFileName + FileManager.LoomExtension);
    }
}
