using Loom.Core.Parsing.AST;
using Loom.Core.Text;
using Loom.Core.TypeChecking;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.Core.Diagnostics;

public sealed record Diagnostic(LocationSpan Span, DiagnosticSeverity Severity, string? Code, string Message, string? Hint)
{
    private readonly DiagnosticSeverityStyle _severityStyle = Severity switch
    {
        DiagnosticSeverity.Error => new DiagnosticSeverityStyle(Colors.Red, Colors.Magenta, "error"),
        DiagnosticSeverity.Warn => new DiagnosticSeverityStyle(Colors.Yellow, Colors.Yellow, "warning"),
        DiagnosticSeverity.Info => new DiagnosticSeverityStyle(Colors.Blue, Colors.Cyan, "info"),
        _ => new DiagnosticSeverityStyle(Colors.White, Colors.Gray, "unknown")
    };
    private int StartLine => Span.Start.Line;
    private int EndLine => Span.End.Line;
    private int StartCharacter => Span.Start.Character;
    private int EndCharacter => Span.End.Character;
    // the trailing context line is one past the span, so the gutter has to be wide enough for it or the
    // frame steps a character to the right on the line where the digit count rolls over
    private int LineDigits => Math.Min(EndLine + 1, SourceLines.Length).ToString().Length;
    private string[] SourceLines => field ??= Span.File.SourceText.Replace(Environment.NewLine, "\n").Split('\n');
    private string GutterIndent => new(' ', LineDigits);
    private string Gutter => $"{Colors.Dim}{GutterIndent} │{Colors.Reset}";

    // Explicit so the lazily-populated SourceLines/_severityStyle fields (record equality includes
    // every instance field by default) can't change this Diagnostic's hash after it's in a HashSet.
    public bool Equals(Diagnostic? other) =>
        other is not null
        && (ReferenceEquals(this, other)
            || Span.Equals(other.Span) && Severity == other.Severity && Code == other.Code && Message == other.Message && Hint == other.Hint);

    public override int GetHashCode() => HashCode.Combine(Span, Severity, Code, Message, Hint);

    internal static string? FormatBinaryHint(BinaryOperator op, Type left, Type right, BinaryOperatorRule? suggestion)
    {
        if (suggestion == null)
            return null;

        if (suggestion.OperatorKind != op.Operator.Kind)
            return $"did you mean '{op.Left} {SyntaxFacts.GetOperatorText(suggestion.OperatorKind)} {op.Right}'?";

        var leftWrong = !left.IsAssignableTo(suggestion.LeftType);
        var rightWrong = !right.IsAssignableTo(suggestion.RightType);
        return (leftWrong, rightWrong) switch
        {
            (true, true) => $"expected '{suggestion.LeftType} {op.Operator.Text} {suggestion.RightType}', not '{left} {op.Operator.Text} {right}'",
            (true, false) => $"expected left operand of type '{suggestion.LeftType}', not '{left}'",
            (false, true) => $"expected right operand of type '{suggestion.RightType}', not '{right}'",
            _ => null
        };
    }

    internal static string? FormatUnaryHint(UnaryOperator op, Type operand, UnaryOperatorRule? suggestion)
    {
        if (suggestion == null)
            return null;

        var suggestedOp = SyntaxFacts.GetOperatorText(suggestion.OperatorKind);
        return suggestion.OperatorKind != op.Operator.Kind
            ? $"did you mean '{suggestedOp}{op.Operand}'?"
            : $"expected operand of type '{suggestion.OperandType}', not '{operand}'";
    }

    public override string ToString() => FormatFrame();

    private string FormatFrame()
    {
        var lines = new List<string> { FormatHeader(), FormatLocation(), Gutter };
        AppendSource(lines);
        AppendHint(lines);
        lines.Add(Gutter);

        return string.Join(Environment.NewLine, lines);
    }

    private void AppendSource(List<string> lines)
    {
        AppendPreviousSourceLine(lines);
        AppendHighlightedLines(lines);
    }

    private void AppendHint(List<string> lines)
    {
        if (Hint == null)
        {
            AppendNextSourceLine(lines);
            return;
        }

        var pad = StartLine == EndLine ? new string(' ', StartCharacter) : "";
        lines.Add(
            $"{Gutter}{pad} {_severityStyle.UnderlineColor}╰─{Colors.Reset}  {_severityStyle.PrimaryColor}{Colors.Bold}Hint:{Colors.Reset} {Colors.Gray}{Hint}{Colors.Reset}"
        );

        AppendNextSourceLine(lines);
    }

    private void AppendHighlightedLines(List<string> lines)
    {
        var pad = new string(' ', StartCharacter);
        for (var line = StartLine; line <= EndLine; line++)
        {
            var source = SourceLines[line - 1];
            var number = line.ToString().PadLeft(LineDigits);
            lines.Add($"{Colors.Bold}{number} │{Colors.Reset} {source}");
            lines.Add(BuildUnderline(line, source, pad));
        }
    }

    private void AppendPreviousSourceLine(List<string> lines)
    {
        if (StartLine <= 1) return;
        lines.Add($"{Colors.Dim}{(StartLine - 1).ToString().PadLeft(LineDigits)} │ {SourceLines[StartLine - 2]}{Colors.Reset}");
        lines.Add(Gutter);
    }

    private void AppendNextSourceLine(List<string> lines)
    {
        if (EndLine >= SourceLines.Length) return;
        if (Hint != null)
            lines.Add(Gutter);

        lines.Add($"{Colors.Dim}{(EndLine + 1).ToString().PadLeft(LineDigits)} │ {SourceLines[EndLine]}{Colors.Reset}");
    }

    private string BuildUnderline(int line, string source, string firstLinePad)
    {
        var subtract = Hint != null
            ? Math.Min(EndCharacter - StartCharacter, 1)
            : 0;

        var prepend = Hint != null
            ? line != EndLine && StartLine != EndLine
                ? "─"
                : "┬"
            : "";

        var pad = line == StartLine ? firstLinePad : "";
        var length = line switch
        {
            _ when StartLine == EndLine => EndCharacter - StartCharacter - subtract,
            _ when line == StartLine => source.Length - StartCharacter - subtract,
            _ when line == EndLine => EndCharacter - subtract,
            _ => source.Length - subtract
        };

        return $"{Gutter} {Colors.Bold}{pad}{_severityStyle.UnderlineColor}{prepend}{new string('─', Math.Max(length, 0))}{Colors.Reset}";
    }

    private string FormatLocation() => $"{Colors.Dim}{GutterIndent} ╭─{Colors.Reset} {Colors.Orange}{Span}{Colors.Reset}";

    private string FormatHeader()
    {
        var code = string.IsNullOrEmpty(Code)
            ? ""
            : $" {Colors.Dim}({Code}){Colors.Reset}{_severityStyle.PrimaryColor}";

        return $"{_severityStyle.PrimaryColor}{Colors.Bold}{_severityStyle.Label}"
            + $"{Colors.Reset}{_severityStyle.PrimaryColor}{code}:{Colors.Reset} "
            + $"{Colors.Gray}{Message}{Colors.Reset}";
    }
}