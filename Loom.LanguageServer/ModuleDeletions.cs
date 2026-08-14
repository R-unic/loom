using Loom.Core.Modules;
using Loom.Core.Pipeline;
using Loom.Core.Text;

namespace Loom.LanguageServer;

/// <summary>One relative import that would stop resolving if the delete it was found against went ahead.</summary>
public sealed record BrokenImport(string ImportingPath, string Specifier);

/// <summary>
///     What a delete would break. A relative specifier is a path from the importing file, and unlike a move
///     there is nowhere for it to be rewritten to point at afterward - the best the server can do is say so
///     before the file is gone, since <c>willDeleteFiles</c> has no answer that means "don't."
/// </summary>
public static class ModuleDeletions
{
    public static IReadOnlyList<BrokenImport> Broken(IReadOnlyList<CompiledProject> projects, IReadOnlyList<string> deletedPaths)
    {
        if (deletedPaths.Count == 0)
            return [];

        var broken = new List<BrokenImport>();
        foreach (var project in projects)
        {
            var resolver = new ModuleResolver(project.Unit.SourceFiles, project.Unit.Roots);
            foreach (var file in project.Files)
            {
                // the importing file is itself going away, so there is nothing left for it to be warned about
                if (IsDeleted(file.SourceFile.AbsolutePath, deletedPaths))
                    continue;

                foreach (var (_, path) in ModuleRenames.ModuleSpecifiersOf(file.Tree))
                {
                    if (path == null || !ModuleResolver.IsRelativeSpecifier(path))
                        continue;

                    var target = ModuleRenames.Resolve(resolver, file.SourceFile, path);
                    if (target != null && IsDeleted(target.AbsolutePath, deletedPaths))
                        broken.Add(new BrokenImport(file.SourceFile.AbsolutePath, path));
                }
            }
        }

        return broken;
    }

    /// <summary>Whether the path is one of the files being deleted, or sits inside a directory that is.</summary>
    private static bool IsDeleted(string path, IReadOnlyList<string> deletedPaths) =>
        deletedPaths.Any(deleted => FilePaths.Same(path, deleted) || PathComparison.IsUnder(path, deleted));

    public static string Describe(IReadOnlyList<BrokenImport> broken)
    {
        const int shown = 5;
        var lines = broken.Take(shown)
            .Select(entry => $"'{entry.Specifier}' in {Path.GetFileName(entry.ImportingPath)}");

        var summary = $"Deleting this would break {broken.Count} import{(broken.Count == 1 ? "" : "s")}: {string.Join(", ", lines)}";
        return broken.Count > shown ? $"{summary}, and {broken.Count - shown} more." : $"{summary}.";
    }
}
