using Loom.Core.Pipeline;
using Loom.Core.Resolving.Symbols;
using Loom.Core.Text;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.LanguageServer;

/// <summary>
///     Renders what is known about a symbol as the markdown an editor shows on hover and in completion. Every
///     section is optional and the order is fixed, so the same symbol reads the same way wherever it appears:
///     signature, deprecation, origin, then the prose its author wrote.
/// </summary>
public static class SymbolMarkdown
{
    /// <summary>The full hover body: the declaration as code, followed by everything said about it.</summary>
    public static string Describe(Symbol symbol, Type? type, SourceFile viewedFrom, CompilationUnit unit)
    {
        var attributes = DeclarationDisplay.AttributesOf(symbol);
        var signature = DeclarationDisplay.Of(symbol, type);
        return Compose($"{(attributes == null ? "" : attributes + '\n')}{signature}", Notes(symbol, viewedFrom, unit), Documentation(symbol));
    }

    /// <summary>The same body for an interface member, which has a type and a name but no declaration to read a keyword off.</summary>
    public static string DescribeMember(string name, Type type) => Code($"{name}: {type}");

    private static string Compose(string code, string notes, string? documentation)
    {
        var body = Code(code);
        if (notes.Length > 0)
            body += '\n' + notes;

        return documentation == null ? body : $"{body}\n\n---\n\n{documentation}";
    }

    /// <summary>The italicised lines under the signature: what is wrong with using this symbol, and where it came from.</summary>
    private static string Notes(Symbol symbol, SourceFile viewedFrom, CompilationUnit unit)
    {
        var notes = new List<string>();
        if (DeclarationDisplay.DeprecationOf(symbol) is { } deprecation)
            notes.Add(deprecation);

        if (SymbolOrigin.Describe(symbol, viewedFrom, unit) is { } origin)
            notes.Add($"*{origin}*");

        return string.Join("\n\n", notes);
    }

    private static string? Documentation(Symbol symbol)
    {
        var block = DocumentationBlock.Parse(symbol.Declaration.Documentation);
        return block.IsEmpty ? null : block.ToMarkdown();
    }

    private static string Code(string text) => $"```loom\n{text}\n```";
}
