using System.Reflection;
using Loom.Config;
using Loom.Core.Text;

namespace Loom.Core.Pipeline;

public static class FileManager
{
    public const string LoomExtension = ".loom";
    private const string IncludeFolderName = "include";
    private const string IncludeResourcePrefix = "Include/";

    private static readonly Assembly _resourceAssembly = typeof(FileManager).Assembly;

    /// <summary>
    ///     Writes the bundled Luau runtime support into the project's include folder, overwriting whatever is
    ///     already there. The sources are embedded resources rather than files beside the executable: the compiler
    ///     is installed as a lone binary by a toolchain manager, so nothing may be assumed to sit next to it.
    /// </summary>
    public static void WriteIncludeFolder(string projectDirectory)
    {
        var destination = Path.Combine(projectDirectory, IncludeFolderName);
        Directory.CreateDirectory(destination);

        foreach (var resourceName in _resourceAssembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(IncludeResourcePrefix, StringComparison.Ordinal)) continue;

            var relativePath = resourceName[IncludeResourcePrefix.Length..].Replace('/', Path.DirectorySeparatorChar);
            var destinationFile = Path.Combine(destination, relativePath);

            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);

            using var stream = _resourceAssembly.GetManifestResourceStream(resourceName)!;
            using var file = File.Create(destinationFile);
            stream.CopyTo(file);
        }
    }

    /// <summary>Writes the file's rendered Luau, skipping the write entirely when it would be byte-identical to what's already on disk.</summary>
    /// <returns>Whether the file was actually written.</returns>
    public static bool WriteCompiledFile(CompiledFile file)
    {
        if (File.Exists(file.Path) && File.ReadAllText(file.Path) == file.RenderedLuau)
            return false;

        var directory = Path.GetDirectoryName(file.Path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(file.Path, file.RenderedLuau);
        Log.Info($"Wrote {file.Path}");
        return true;
    }

    public static string GetOutputPath(SourceFile file, LoomConfig config)
    {
        var relativePath = Path.GetRelativePath(config.Files.SourceDirectory, file.AbsolutePath);
        var outputPath = Path.Combine(config.Files.OutputDirectory, relativePath);
        return Path.ChangeExtension(outputPath, ".luau");
    }

    public static bool IsLoomFile(string path) => Path.GetExtension(path) == LoomExtension;

    public static SourceFile LoadSingle(string path) => new(Path.GetFullPath(path));

    public static List<SourceFile> LoadDirectory(string directoryPath) => LoadDirectory(directoryPath, SearchOption.AllDirectories);

    private static List<SourceFile> LoadDirectory(string directoryPath, SearchOption searchOption) =>
        !string.IsNullOrWhiteSpace(directoryPath) && Directory.Exists(directoryPath)
            ? [.. Directory.GetFiles(directoryPath, $"*{LoomExtension}", searchOption).Select(LoadSingle)]
            : [];
}