using Loom.Core.Text;

namespace Loom.Core.Lexing;

public readonly struct LexerRule(SyntaxKind syntax, string pattern)
{
    public static LexerRule RegEx(SyntaxKind syntax, string pattern) => new(syntax, pattern);

    public SyntaxKind Syntax { get; } = syntax;
    public string Pattern { get; } = pattern;
}