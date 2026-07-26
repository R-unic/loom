using System.Text.RegularExpressions;
using Loom.Core.Diagnostics;
using Loom.Core.Text;

namespace Loom.Core.Lexing;

using static LexerRule;
using static SyntaxKind;

public static class LexerRules
{
    private static readonly (LexerRule Rule, IReadOnlyList<char> LeadChars)[] _regexRuleSpecs =
    [
        (RegEx(BlockComment, @"#:[\s\S]*?:#"), ['#']), (RegEx(Comment, @"##[^\n]*"), ['#'])
    ];

    public static readonly IReadOnlyDictionary<char, (LexerRule Rule, Regex CompiledRegex)[]> RegexRulesByFirstCharacter = _regexRuleSpecs
        .SelectMany(spec => spec.LeadChars.Select(c => (Char: c, spec.Rule)))
        .GroupBy(x => x.Char)
        .ToDictionary(
            g => g.Key,
            g => g.Select(x => (x.Rule, new Regex($@"\G(?:{x.Rule.Pattern})", RegexOptions.Compiled))).ToArray()
        );

    public static readonly IReadOnlyList<LexerDiagnosticRule> Diagnostic =
    [
        DiagnosticRule(
            "\"",
            InternalCodes.UnterminatedString,
            _ => "Unterminated string literal: expected closing '\"'."
        ),
        DiagnosticRule(
            "'",
            InternalCodes.UnterminatedString,
            _ => "Unterminated string literal: expected closing \"'\"."
        ),
        DiagnosticRule(
            @"#:[\s\S]*",
            InternalCodes.UnterminatedComment,
            _ => "Unterminated block comment: expected closing ':#'."
        )
    ];

    private static LexerDiagnosticRule DiagnosticRule(string pattern, string code, Func<string, string> message) =>
        new(new Regex($@"\G(?:{pattern})", RegexOptions.Compiled), code, message);
}