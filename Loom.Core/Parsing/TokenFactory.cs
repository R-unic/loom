using Loom.Core.Text;

namespace Loom.Core.Parsing;

public static class TokenFactory
{
    private static readonly LocationSpan _defaultSpan = LocationSpan.Empty();

    public static Token Identifier(string text, LocationSpan? span = null) => new(SyntaxKind.Identifier, span ?? _defaultSpan, text.Trim());
    public static Token Keyword(SyntaxKind kind, LocationSpan? span = null) => new(kind, span ?? _defaultSpan, SyntaxFacts.GetKeywordText(kind));
    public static Token Operator(SyntaxKind kind, LocationSpan? span = null) => new(kind, span ?? _defaultSpan, SyntaxFacts.GetOperatorText(kind));
}