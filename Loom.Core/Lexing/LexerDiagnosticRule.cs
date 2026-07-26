using System.Text.RegularExpressions;
using Loom.Core.Diagnostics;

namespace Loom.Core.Lexing;

/// <summary>
///     A recovery rule that matches malformed or incomplete input, emits a targeted
///     diagnostic, and consumes the offending text so the lexer can keep going.
/// </summary>
public sealed record LexerDiagnosticRule(
    Regex Pattern,
    string DiagnosticCode,
    Func<string, string> MessageFactory,
    DiagnosticSeverity Severity = DiagnosticSeverity.Error,
    string? Hint = null
);