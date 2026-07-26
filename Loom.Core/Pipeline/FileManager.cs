using Loom.Config;
using Loom.Core.Text;

namespace Loom.Core.Pipeline;

public static class FileManager
{
    public const string LoomExtension = ".loom";

    public static void WriteCompiledFile(CompiledFile file)
    {
        var directory = Path.GetDirectoryName(file.Path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(file.Path, file.RenderedLuau);
    }

    public static string GetOutputPath(SourceFile file, LoomConfig config) =>
        file.AbsolutePath
            .Replace(
                Path.GetFileName(config.Files.SourceDirectory) + Path.DirectorySeparatorChar,
                Path.GetFileName(config.Files.OutputDirectory) + Path.DirectorySeparatorChar
            )
            .Replace(LoomExtension, ".luau");

    public static bool IsLoomFile(string path) => Path.GetExtension(path) == LoomExtension;

    public static SourceFile LoadSingle(string path) => new(Path.GetFullPath(path));

    public static List<SourceFile> LoadDirectory(string directoryPath) => LoadDirectory(directoryPath, SearchOption.AllDirectories);

    private static List<SourceFile> LoadDirectory(string directoryPath, SearchOption searchOption) =>
        !string.IsNullOrWhiteSpace(directoryPath) && Directory.Exists(directoryPath)
            ? Directory.GetFiles(directoryPath, $"*{LoomExtension}", searchOption).Select(LoadSingle).ToList()
            : [];
}