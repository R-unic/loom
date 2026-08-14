using Loom.Core.Modules;
using Loom.Core.Parsing.AST;
using Loom.Core.Pipeline;
using Loom.Core.Text;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Location = Loom.Core.Text.Location;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace Loom.LanguageServer;

/// <summary>A file the editor is about to move, and where it is moving it to.</summary>
public sealed record ModuleRename(string OldPath, string NewPath);

/// <summary>
///     Keeps imports pointing at what they named when a file moves. A relative specifier is a path from the
///     importing file's directory, so moving either end of an import silently breaks it - and moving a
///     directory breaks every import that crossed it at once, which is the case nobody wants to fix by hand.
/// </summary>
/// <remarks>
///     Package specifiers are left alone. One names the root publishing it rather than a location, so it goes
///     on meaning the same thing wherever the file that wrote it ends up - unless the move crosses a project
///     boundary, which is not a rename the editor can make sense of either.
/// </remarks>
public static class ModuleRenames
{
    public static IReadOnlyDictionary<DocumentUri, IReadOnlyList<TextEdit>> EditsFor(
        IReadOnlyList<CompiledProject> projects,
        IReadOnlyList<ModuleRename> renames)
    {
        var edits = new Dictionary<DocumentUri, IReadOnlyList<TextEdit>>();
        if (renames.Count == 0)
            return edits;

        foreach (var project in projects)
        {
            var resolver = new ModuleResolver(project.Unit.SourceFiles, project.Unit.Roots);
            foreach (var file in project.Files)
            {
                var fileEdits = EditsIn(file, resolver, renames);
                if (fileEdits.Count > 0 && Path.IsPathRooted(file.SourceFile.AbsolutePath))
                    // the old URI: a will-rename edit is applied before the files move, so the document the
                    // client has to edit is still the one at the path it is about to leave
                    edits[DocumentUri.FromFileSystemPath(file.SourceFile.AbsolutePath)] = fileEdits;
            }
        }

        return edits;
    }

    private static IReadOnlyList<TextEdit> EditsIn(CompiledFile file, ModuleResolver resolver, IReadOnlyList<ModuleRename> renames)
    {
        var importer = file.SourceFile;
        var movedImporter = After(importer.AbsolutePath, renames);
        var edits = new List<TextEdit>();

        foreach (var (specifier, path) in ModuleSpecifiersOf(file.Tree))
        {
            if (path == null || !ModuleResolver.IsRelativeSpecifier(path))
                continue;

            // resolved from where the file still is, since that is where the specifier was written
            if (Resolve(resolver, importer, path) is not { } target)
                continue;

            var movedTarget = After(target.AbsolutePath, renames);
            if (movedImporter == null && movedTarget == null)
                continue;

            var updated = SpecifierOf(resolver, movedImporter ?? importer, movedTarget ?? target);
            if (updated == null || updated == path)
                continue;

            edits.Add(new TextEdit { Range = QuotedRange(importer, specifier), NewText = updated });
        }

        return edits;
    }

    /// <summary>
    ///     Where the file at <paramref name="path" /> will be, or null when nothing moves it. A rename of a
    ///     directory arrives as the directory itself, so a file is moved by any rename it sits under as well as
    ///     by one naming it outright.
    /// </summary>
    private static SourceFile? After(string path, IReadOnlyList<ModuleRename> renames)
    {
        foreach (var rename in renames)
        {
            if (FilePaths.Same(path, rename.OldPath))
                return new SourceFile(rename.NewPath, "");

            if (!PathComparison.IsUnder(path, rename.OldPath))
                continue;

            var withinDirectory = Path.GetRelativePath(rename.OldPath, path);
            return new SourceFile(Path.GetFullPath(Path.Combine(rename.NewPath, withinDirectory)), "");
        }

        return null;
    }

    /// <summary>Every module specifier written in the file, imports and re-exports alike.</summary>
    internal static IEnumerable<(Literal Specifier, string? Path)> ModuleSpecifiersOf(Tree tree)
    {
        foreach (var statement in tree.Statements)
            switch (statement)
            {
                case ImportDeclaration import:
                    yield return (import.ModuleSpecifier, import.ModulePath);
                    break;
                case NamespaceImport import:
                    yield return (import.ModuleSpecifier, import.ModulePath);
                    break;
                case IReExport { IsReExport: true, ModuleSpecifier: { } specifier } reExport:
                    yield return (specifier, reExport.ModulePath);
                    break;
            }
    }

    /// <summary>The span inside the quotes, so the edit replaces the path without disturbing how it was quoted.</summary>
    private static LspRange QuotedRange(SourceFile file, Literal specifier)
    {
        var span = specifier.Span;
        var inside = TextSpan.FromStartEnd(span.Position + 1, Math.Max(span.Position + 1, span.End - 1));
        return new LspRange(
            Conversion.ToPosition(new Location(file, inside.Position)),
            Conversion.ToPosition(new Location(file, inside.End))
        );
    }

    internal static SourceFile? Resolve(ModuleResolver resolver, SourceFile importingFile, string specifier)
    {
        try
        {
            return resolver.Resolve(importingFile, specifier).File;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <remarks>Null when the move lands somewhere no specifier could name, which is not a rename to rewrite imports for.</remarks>
    private static string? SpecifierOf(ModuleResolver resolver, SourceFile importingFile, SourceFile module)
    {
        try
        {
            var specifier = resolver.SpecifierOf(importingFile, module);
            return ModuleResolver.IsRelativeSpecifier(specifier) ? specifier : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
