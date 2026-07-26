using Loom.Core.Text;

namespace Loom.Core.Modules;

/// <summary>
///     Maps a module specifier as written in source (<c>"./math"</c>) onto a <see cref="SourceFile" /> of the
///     compilation unit. Specifiers are extension-less and relative to the importing file's directory;
///     <c>"./math"</c> matches either <c>math.loom</c> or <c>math/init.loom</c>, mirroring how Rojo folds an
///     <c>init</c> file into its folder.
/// </summary>
/// <remarks>
///     Paths are compared case-sensitively even where the file system is not, because Roblox requires are.
///     Declaration files are ambient globals rather than modules, so they are never importable.
/// </remarks>
public sealed class ModuleResolver
{
    private const string IndexFileName = "init";

    private readonly Dictionary<string, SourceFile> _modulesByPath;
    private readonly string _sourceDirectory;

    public ModuleResolver(IEnumerable<SourceFile> files, string sourceDirectory)
    {
        _modulesByPath = new Dictionary<string, SourceFile>(StringComparer.Ordinal);
        foreach (var file in files.Where(file => !file.IsDeclaration))
            _modulesByPath.TryAdd(Path.GetFullPath(file.AbsolutePath), file);

        _sourceDirectory = Path.GetFullPath(sourceDirectory);
    }

    public static bool IsRelativeSpecifier(string specifier) =>
        specifier.StartsWith("./", StringComparison.Ordinal) || specifier.StartsWith("../", StringComparison.Ordinal);

    public ModuleResolution Resolve(SourceFile importingFile, string specifier)
    {
        if (!IsRelativeSpecifier(specifier))
            return ModuleResolution.Failed(ModuleResolutionStatus.UnsupportedSpecifier);

        var importingDirectory = Path.GetDirectoryName(Path.GetFullPath(importingFile.AbsolutePath));
        if (importingDirectory == null)
            return ModuleResolution.Failed(ModuleResolutionStatus.NotFound);

        var basePath = Path.GetFullPath(Path.Combine(importingDirectory, specifier));
        if (!IsInsideSourceDirectory(basePath))
            return ModuleResolution.Failed(ModuleResolutionStatus.OutsideSourceDirectory);

        foreach (var candidate in GetCandidatePaths(basePath))
        {
            if (!_modulesByPath.TryGetValue(candidate, out var file))
                continue;

            return file == importingFile
                ? ModuleResolution.Failed(ModuleResolutionStatus.SelfImport)
                : ModuleResolution.Resolved(file);
        }

        return ModuleResolution.Failed(ModuleResolutionStatus.NotFound);
    }

    private static IEnumerable<string> GetCandidatePaths(string basePath)
    {
        yield return basePath + FileManager.LoomExtension;
        yield return Path.Combine(basePath, IndexFileName + FileManager.LoomExtension);
    }

    private bool IsInsideSourceDirectory(string path) =>
        path == _sourceDirectory
        || path.StartsWith(_sourceDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal);
}