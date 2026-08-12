using Loom.Core.Text;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.LanguageServer;

/// <summary>
///     The language's own words, offered alongside the names in scope. Neither keywords nor the built-in type
///     names are symbols - the lexer turns them into their own token kinds before the resolver ever sees a
///     name - so nothing enumerating a scope will ever produce them, and completion has to add them itself.
/// </summary>
public static class LanguageKeywords
{
    private static readonly HashSet<string> _typeOnlyKeywords = ["keyof"];
    private static readonly string[] _typeBuildingKeywords = ["async", "fn", "keyof", "typeof"];

    /// <summary>The words that may open or continue a statement or expression.</summary>
    public static readonly IReadOnlyList<VisibleSymbol> Values = SyntaxFacts.KeywordNames
        .Where(keyword => !_typeOnlyKeywords.Contains(keyword))
        .Order(StringComparer.Ordinal)
        .Select(keyword => new VisibleSymbol(keyword, CompletionItemKind.Keyword))
        .ToArray();

    /// <summary>
    ///     What may be written where a type is expected: the built-in type names, plus the few keywords that
    ///     build a type out of other types.
    /// </summary>
    public static readonly IReadOnlyList<VisibleSymbol> Types = SyntaxFacts.PrimitiveTypeNames
        .Concat(_typeBuildingKeywords)
        .Distinct()
        .Order(StringComparer.Ordinal)
        .Select(keyword => new VisibleSymbol(keyword, CompletionItemKind.Keyword) { IsTypeSymbol = true })
        .ToArray();
}
