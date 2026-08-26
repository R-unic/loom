using Loom.Core.Modules;
using Loom.Core.Parsing.AST;
using Loom.Core.Pipeline;
using Loom.Core.Resolving;
using Loom.Core.Resolving.Symbols;
using Loom.Core.Text;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Location = Loom.Core.Text.Location;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace Loom.LanguageServer;

/// <summary>A name another module exports, and the specifier that would bring it in.</summary>
public sealed record ImportCandidate(string Name, string Specifier, Symbol Symbol, SemanticModel Model);

/// <summary>
///     What a file could import but has not. A name is only offered from a module the importing file is
///     allowed to name - its own root's siblings, and the packages its root declares - so completing one never
///     writes an import the compiler would then reject.
/// </summary>
public static class ImportCatalog
{
    public static IReadOnlyList<ImportCandidate> For(SourceFile importingFile, CompilationUnit unit, ModuleResolver resolver)
    {
        var importingRoot = RootOf(importingFile, unit);
        var candidates = new List<ImportCandidate>();

        foreach (var (module, model) in unit.AnalyzedModules)
        {
            if (module == importingFile || module.IsDeclaration || module.IsIntrinsic)
                continue;

            var moduleRoot = RootOf(module, unit);
            if (moduleRoot == null || (moduleRoot != importingRoot && moduleRoot.Package?.Name == null))
                continue;

            var specifier = SpecifierOf(resolver, importingFile, module);
            if (specifier == null)
                continue;

            // an interface is exported as a type and as a value under one name; both are candidates, since
            // which one is wanted depends on whether the cursor is writing a type or a value. An 'internal'
            // export never completes across a root boundary - offering it would write an import the
            // compiler then rejects, same as offering a name the module never exports at all.
            var visible = moduleRoot == importingRoot ? model.Exports : model.Exports.FindAll(export => !export.IsInternal);
            var exports = visible.GroupBy(export => (export.Name, export.Symbol.IsTypeSymbol)).Select(group => group.First());
            foreach (var export in exports)
                candidates.Add(new ImportCandidate(export.Name, specifier, export.Symbol, model));
        }

        return candidates;
    }

    private static string? SpecifierOf(ModuleResolver resolver, SourceFile importingFile, SourceFile module)
    {
        try
        {
            var specifier = resolver.SpecifierOf(importingFile, module);
            return string.IsNullOrEmpty(specifier) ? null : specifier;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static SourceRoot? RootOf(SourceFile file, CompilationUnit unit)
    {
        try
        {
            return unit.Roots.Of(file);
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>
///     Builds the edit that brings a name into a file. An import for the same module is extended rather than
///     duplicated - two <c>import</c> statements naming one module is not an error, but it is not what anyone
///     would have written by hand.
/// </summary>
public static class ImportEdits
{
    public static TextEdit? Add(CompiledFile file, string name, string specifier)
    {
        var existing = file.Tree.Statements
            .OfType<ImportDeclaration>()
            .FirstOrDefault(import => import.ModulePath == specifier && !import.IsTypeOnly);

        return existing == null ? NewStatement(file, name, specifier) : ExtendedList(existing, name);
    }

    /// <summary>Adds the name to an import already naming the module, after the last specifier it lists.</summary>
    private static TextEdit? ExtendedList(ImportDeclaration import, string name)
    {
        if (import.Specifiers.Exists(specifier => specifier.Name.Text == name))
            return null;

        var (position, text) = import.Specifiers.Count == 0
            ? (import.LeftBrace.Span.End, $" {name} ")
            : (import.Specifiers[^1].Span.End, $", {name}");

        return new TextEdit { Range = EmptyRangeAt(import.File, position), NewText = text };
    }

    /// <summary>
    ///     Writes a new import under the ones the file already opens with, or at the very top when it has
    ///     none - which is where the next one would have gone anyway.
    /// </summary>
    private static TextEdit NewStatement(CompiledFile file, string name, string specifier)
    {
        var leading = file.Tree.Statements.TakeWhile(statement => statement is ImportDeclaration).ToArray();
        var statement = $"import {{ {name} }} from \"{specifier}\";\n";

        if (leading.Length == 0)
            return new TextEdit { Range = EmptyRangeAt(file.SourceFile, 0), NewText = statement };

        var after = EndOfLine(file.SourceFile, leading[^1].Span.End);
        return new TextEdit { Range = EmptyRangeAt(file.SourceFile, after), NewText = statement };
    }

    /// <summary>The offset just past the end of the line the position is on, which is where a whole statement may be inserted.</summary>
    private static int EndOfLine(SourceFile file, int position)
    {
        var text = file.SourceText;
        while (position < text.Length && text[position] != '\n')
            position++;

        return Math.Min(position + 1, text.Length);
    }

    private static LspRange EmptyRangeAt(SourceFile file, int position)
    {
        var at = Conversion.ToPosition(new Location(file, position));
        return new LspRange(at, at);
    }
}
