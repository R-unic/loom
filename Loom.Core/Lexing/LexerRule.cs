using Loom.Core.Text;

namespace Loom.Core.Lexing;

public readonly struct LexerRule(LexerRuleKind kind, SyntaxKind syntax, string pattern)
{
    public static LexerRule SingleCharacter(SyntaxKind syntax, char character) => new(LexerRuleKind.SingleCharacter, syntax, character.ToString());
    public static LexerRule MultiCharacter(SyntaxKind syntax, string pattern) => new(LexerRuleKind.MultiCharacter, syntax, pattern);
    public static LexerRule RegEx(SyntaxKind syntax, string pattern) => new(LexerRuleKind.RegEx, syntax, pattern);

    public LexerRuleKind Kind { get; } = kind;
    public SyntaxKind Syntax { get; } = syntax;
    public string Pattern { get; } = pattern;
}