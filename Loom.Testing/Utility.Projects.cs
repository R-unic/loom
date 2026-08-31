using Loom.Config;
using Loom.Core.Diagnostics;
using Loom.Core.Pipeline;

namespace Loom.Testing;

internal static partial class Utility
{
    public static IEnumerable<TheoryDataRow<string, string>> GetSnapshotFiles(string folderName, string targetExtension) =>
        Directory.EnumerateFiles(AssemblyFixture.Snapshots + '/' + folderName, $"*{FileManager.LoomExtension}")
            .Select(path => new TheoryDataRow<string, string>(path, path.Replace(FileManager.LoomExtension, targetExtension)));

    /// <summary>
    ///     Compiles a throwaway project whose source directory holds <paramref name="files" />, keyed by path
    ///     relative to that directory so nested modules can be written as <c>"util/init.loom"</c>.
    /// </summary>
    public static void WithTempProject(
        IEnumerable<(string Path, string Source)> files,
        Action<CompilationUnit, CompilationResult> assert,
        string? rojoProject = null,
        DiagnosticOptions? diagnosticOptions = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "loom-test-" + Guid.NewGuid());
        var sourceDirectory = Path.Combine(directory, "src");
        Directory.CreateDirectory(sourceDirectory);
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "loom-config.toml"),
                "project_type = \"game\"\n[files]\nsource_directory = \"src\"\noutput_directory = \"dist\"\n"
            );

            if (rojoProject != null)
                File.WriteAllText(Path.Combine(directory, RojoResolver.ProjectFileName), rojoProject);

            foreach (var (path, source) in files)
            {
                var filePath = Path.Combine(sourceDirectory, path);
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                File.WriteAllText(filePath, source);
            }

            // emits for real: the output goes into the throwaway directory this deletes on the way out, and
            // 'no_emit' now also skips generating the Luau these cases assert on
            var config = ConfigReader.LocateFromDirectory(directory);
            Assert.NotNull(config);

            var compilationUnit = new CompilationUnit(config, diagnosticOptions);
            assert(compilationUnit, compilationUnit.Compile());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
