using Loom.Core.Diagnostics;
using Loom.Core.Text;

namespace Loom.Testing;

internal static partial class Utility
{
    public static IReadOnlyList<Token> GetTokens(string source, bool withTrivia = false) => withTrivia ? Tokenize(source).TokensWithTrivia : Tokenize(source).Tokens;

    public static DiagnosticBag GetLexerDiagnostics(string source) => Tokenize(source).Diagnostics;

    public static Token IdentifierToken(string name, LocationSpan? span = null) => Token(SyntaxKind.Identifier, name, span);
    private static Token Token(SyntaxKind kind, string text, LocationSpan? span = null) => new(kind, span ?? Span, text);
}
