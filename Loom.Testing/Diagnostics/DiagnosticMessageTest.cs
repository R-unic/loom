using Loom.Core.Diagnostics;
using Loom.Core.Text;
using Loom.Testing;

namespace Loom.Testing.Diagnostics;

[Collection("Assembly")]
public class DiagnosticMessageTest
{
    private static readonly SourceFile _testFile = new("test.loom", "let x: number = 5;\nlet y = x + 10;\nprint(y);");

    [Fact]
    public void ToString_ErrorDiagnostic_FormatsCorrectly()
    {
        var start = new Location(_testFile, 4);
        var end = new Location(_testFile, 9);
        var span = new LocationSpan(start, end);
        var diagnostic = new Diagnostic(
            span,
            DiagnosticSeverity.Error,
            InternalCodes.TypeMismatch,
            "Type 'string' is not assignable to type 'number'.",
            "did you mean to use a number?"
        );

        var result = diagnostic.ToString();

        Assert.Contains(
            $"{Colors.Red}{Colors.Bold}error{Colors.Reset}{Colors.Red} {Colors.Dim}(L306){Colors.Reset}{Colors.Red}:{Colors.Reset} {Colors.Gray}Type 'string' is not assignable to type 'number'.{Colors.Reset}",
            result
        );

        Assert.Contains($"{Colors.Dim}  ╭─{Colors.Reset} {Colors.Orange}test.loom @ 1:5 - 1:9{Colors.Reset}", result);
        Assert.Contains("let x: number = 5;", result);
        Assert.Contains($"{Colors.Magenta}┬────{Colors.Reset}", result);
        Assert.Contains(
            $"{Colors.Magenta}╰─{Colors.Reset}  {Colors.Red}{Colors.Bold}Hint:{Colors.Reset} {Colors.Gray}did you mean to use a number?{Colors.Reset}",
            result
        );
    }

    [Fact]
    public void ToString_WarningDiagnostic_FormatsCorrectly()
    {
        var start = new Location(_testFile, 27);
        var end = new Location(_testFile, 33);
        var span = new LocationSpan(start, end);
        var diagnostic = new Diagnostic(span, DiagnosticSeverity.Warn, InternalCodes.RedundantCode, "Unnecessary null check.", null);
        var result = diagnostic.ToString();

        Assert.Contains(
            $"{Colors.Yellow}{Colors.Bold}warning{Colors.Reset}{Colors.Yellow} {Colors.Dim}(L316){Colors.Reset}{Colors.Yellow}:{Colors.Reset} {Colors.Gray}Unnecessary null check.{Colors.Reset}",
            result
        );

        Assert.Contains($"{Colors.Dim}  ╭─{Colors.Reset} {Colors.Orange}test.loom @ 2:9 - 2:14{Colors.Reset}", result);
        Assert.Contains("let y = x + 10;", result);
        Assert.Contains($"{Colors.Yellow}──────{Colors.Reset}", result);
        Assert.DoesNotContain("Hint:", result);
    }

    [Fact]
    public void ToString_InfoDiagnostic_FormatsCorrectly()
    {
        var start = new Location(_testFile, 0);
        var end = new Location(_testFile, _testFile.SourceText.Length - 1);
        var span = new LocationSpan(start, end);
        var diagnostic = new Diagnostic(span, DiagnosticSeverity.Info, null, "Variable 'print' is a built-in function.", null);
        var result = diagnostic.ToString();

        Assert.Contains(
            $"{Colors.Blue}{Colors.Bold}info{Colors.Reset}{Colors.Blue}:{Colors.Reset} {Colors.Gray}Variable 'print' is a built-in function.{Colors.Reset}",
            result
        );

        Assert.DoesNotContain("(L", result);
        Assert.Contains("print(y);", result);
        Assert.DoesNotContain("Hint:", result);
    }

    [Fact]
    public void ToString_MultiLineError_FormatsCorrectly()
    {
        var multiLineFile = new SourceFile(
            "multi.loom",
            $"fn test() {{{Environment.NewLine}    let x: string = 42;{Environment.NewLine}    return x;{Environment.NewLine}}}"
        );

        var start = new Location(multiLineFile, 13);
        var end = new Location(multiLineFile, 15);
        var span = new LocationSpan(start, end);
        var diagnostic = new Diagnostic(
            span,
            DiagnosticSeverity.Error,
            InternalCodes.TypeMismatch,
            "Cannot assign number to string.",
            "use a string literal instead"
        );

        var result = diagnostic.ToString();

        Assert.Contains(
            $"{Colors.Red}{Colors.Bold}error{Colors.Reset}{Colors.Red} {Colors.Dim}(L306){Colors.Reset}{Colors.Red}:{Colors.Reset} {Colors.Gray}Cannot assign number to string.{Colors.Reset}",
            result
        );

        Assert.Contains("let x: string = 42;", result);
        Assert.Contains($"{Colors.Magenta}┬─{Colors.Reset}", result);
        Assert.Contains(
            $"{Colors.Magenta}╰─{Colors.Reset}  {Colors.Red}{Colors.Bold}Hint:{Colors.Reset} {Colors.Gray}use a string literal instead{Colors.Reset}",
            result
        );
    }

    [Fact]
    public void ToString_SpanAcrossMultipleLines_FormatsCorrectly()
    {
        var multiLineFile = new SourceFile(
            "multi.loom",
            $"fn test() {{{Environment.NewLine}    let x = {Environment.NewLine}        \"hello\"{Environment.NewLine}        + \"world\";{Environment.NewLine}}}"
        );

        var start = new Location(multiLineFile, 25);
        var end = new Location(multiLineFile, 59);
        var span = new LocationSpan(start, end);
        var diagnostic = new Diagnostic(
            span,
            DiagnosticSeverity.Error,
            InternalCodes.InvalidBinaryOp,
            "Cannot concatenate strings with +.",
            "use .. for string concatenation"
        );

        var result = diagnostic.ToString();

        Assert.Contains(
            $"{Colors.Red}{Colors.Bold}error{Colors.Reset}{Colors.Red} {Colors.Dim}(L311){Colors.Reset}{Colors.Red}:{Colors.Reset} {Colors.Gray}Cannot concatenate strings with +.{Colors.Reset}",
            result
        );

        Assert.Contains("\"hello\"", result);
        Assert.Contains("+ \"world\"", result);
        Assert.Contains($"{Colors.Magenta}┬─────", result);
    }

    [Fact]
    public void ToString_NoCode_OmitsCodeFromHeader()
    {
        var start = new Location(_testFile, 0);
        var end = new Location(_testFile, 3);
        var span = new LocationSpan(start, end);
        var diagnostic = new Diagnostic(span, DiagnosticSeverity.Error, null, "Generic error message.", null);
        var result = diagnostic.ToString();

        Assert.Contains($"{Colors.Red}{Colors.Bold}error{Colors.Reset}{Colors.Red}:{Colors.Reset} {Colors.Gray}Generic error message.{Colors.Reset}", result);
        Assert.DoesNotContain("()", result);
    }

    [Fact]
    public void ToString_WithHint_IncludesHintSection()
    {
        var start = new Location(_testFile, 10);
        var end = new Location(_testFile, 15);
        var span = new LocationSpan(start, end);
        var diagnostic = new Diagnostic(span, DiagnosticSeverity.Error, InternalCodes.CannotFindName, "Cannot find name 'undefined'.", "did you mean 'undefined'?");
        var result = diagnostic.ToString();

        Assert.Contains(
            $"{Colors.Magenta}╰─{Colors.Reset}  {Colors.Red}{Colors.Bold}Hint:{Colors.Reset} {Colors.Gray}did you mean 'undefined'?{Colors.Reset}",
            result
        );
    }

    [Fact]
    public void ToString_WithoutHint_OmitsHintSection()
    {
        var start = new Location(_testFile, 0);
        var end = new Location(_testFile, 3);
        var span = new LocationSpan(start, end);
        var diagnostic = new Diagnostic(span, DiagnosticSeverity.Warn, InternalCodes.RedundantCode, "Redundant code.", null);
        var result = diagnostic.ToString();

        Assert.DoesNotContain("Hint:", result);
        Assert.DoesNotContain("╰─", result);
    }

    [Fact]
    public void ToString_SingleCharacterSpan_FormatsCorrectly()
    {
        var start = new Location(_testFile, 6);
        var end = new Location(_testFile, 7);
        var span = new LocationSpan(start, end);
        var diagnostic = new Diagnostic(span, DiagnosticSeverity.Error, InternalCodes.UnexpectedToken, "Unexpected token ';'.", "remove the semicolon");
        var result = diagnostic.ToString();

        Assert.Contains(
            $"{Colors.Red}{Colors.Bold}error{Colors.Reset}{Colors.Red} {Colors.Dim}(L201){Colors.Reset}{Colors.Red}:{Colors.Reset} {Colors.Gray}Unexpected token ';'.{Colors.Reset}",
            result
        );

        Assert.Contains($"{Colors.Magenta}┬{Colors.Reset}", result);
        Assert.Contains($"{Colors.Magenta}╰─{Colors.Reset}  {Colors.Red}{Colors.Bold}Hint:{Colors.Reset} {Colors.Gray}remove the semicolon{Colors.Reset}", result);
    }

    [Fact]
    public void ToString_FirstLineOfFile_FormatsCorrectly()
    {
        var start = new Location(_testFile, 0);
        var end = new Location(_testFile, _testFile.SourceText.Length - 1);
        var span = new LocationSpan(start, end);
        var diagnostic = new Diagnostic(span, DiagnosticSeverity.Error, InternalCodes.CannotFindName, "Cannot find name 'letx'.", "did you mean 'let x'?");
        var result = diagnostic.ToString();

        Assert.Contains("let x: number = 5;", result);
    }

    [Fact]
    public void ToString_LastLineOfFile_FormatsCorrectly()
    {
        var source = $"line1{Environment.NewLine}line2{Environment.NewLine}line3";
        var lastLineFile = new SourceFile("last.loom", source);
        var start = new Location(lastLineFile, source.Length - 1 - 5);
        var end = new Location(lastLineFile, source.Length - 1);
        var span = new LocationSpan(start, end);
        var diagnostic = new Diagnostic(span, DiagnosticSeverity.Warn, InternalCodes.RedundantCode, "Warning at end of file.", null);
        var result = diagnostic.ToString();

        Assert.Contains("line3", result);
        Assert.Contains($"{Colors.Yellow}────{Colors.Reset}", result);
    }

    [Fact]
    public void ToString_LineNumbers_ArePaddedCorrectly()
    {
        var lines = string.Join(Environment.NewLine, Enumerable.Range(1, 100).Select(i => $"line {i}"));
        var longFile = new SourceFile("long.loom", lines);
        var start = new Location(longFile, 0);
        var end = new Location(longFile, lines.Length - 1);
        var span = new LocationSpan(start, end);
        var diagnostic = new Diagnostic(span, DiagnosticSeverity.Info, null, "Info message.", null);
        var result = diagnostic.ToString();

        Assert.Contains("100 │", result);
        Assert.Contains("99 │", result);
    }

    [Fact]
    public void ToString_WithDifferentSeverityColors_FormatsCorrectly()
    {
        var start = new Location(_testFile, 0);
        var end = new Location(_testFile, 1);
        var span = new LocationSpan(start, end);

        var errorDiag = new Diagnostic(span, DiagnosticSeverity.Error, null, "Error", null);
        var warnDiag = new Diagnostic(span, DiagnosticSeverity.Warn, null, "Warning", null);
        var infoDiag = new Diagnostic(span, DiagnosticSeverity.Info, null, "Info", null);

        var errorResult = errorDiag.ToString();
        var warnResult = warnDiag.ToString();
        var infoResult = infoDiag.ToString();

        Assert.Contains($"{Colors.Red}{Colors.Bold}error{Colors.Reset}", errorResult);
        Assert.Contains($"{Colors.Yellow}{Colors.Bold}warning{Colors.Reset}", warnResult);
        Assert.Contains($"{Colors.Blue}{Colors.Bold}info{Colors.Reset}", infoResult);
    }

    [Fact]
    public void ToString_EmptyFile_HandlesGracefully()
    {
        var emptyFile = new SourceFile("empty.loom", "");
        var start = new Location(emptyFile, 0);
        var end = new Location(emptyFile, 0);
        var span = new LocationSpan(start, end);
        var diagnostic = new Diagnostic(span, DiagnosticSeverity.Error, InternalCodes.UnexpectedEof, "Unexpected end of file.", null);
        var result = diagnostic.ToString();

        Assert.Contains(
            $"{Colors.Red}{Colors.Bold}error{Colors.Reset}{Colors.Red} {Colors.Dim}(L202){Colors.Reset}{Colors.Red}:{Colors.Reset} {Colors.Gray}Unexpected end of file.{Colors.Reset}",
            result
        );

        Assert.Contains("empty.loom", result);
    }

    [Fact]
    public void ToString_WhitespaceLine_FormatsCorrectly()
    {
        var whitespaceFile = new SourceFile("whitespace.loom", $"    {Environment.NewLine}let x = 5;");
        var start = new Location(whitespaceFile, 0);
        var end = new Location(whitespaceFile, 3);
        var span = new LocationSpan(start, end);
        var diagnostic = new Diagnostic(span, DiagnosticSeverity.Error, InternalCodes.CannotFindName, "Cannot find name 'let'.", null);
        var result = diagnostic.ToString();

        Assert.Contains("let x = 5;", result);
        Assert.Contains($"{Colors.Magenta}───{Colors.Reset}", result);
    }

    /// <remarks>
    ///     Columns are 0-based inside the compiler for the LSP's sake, but a header a reader lines up
    ///     against their editor has to be 1-based on both ends, and cover exactly what the underline marks:
    ///     position 4 through 9 is "x: nu", columns 5 through 9.
    /// </remarks>
    [Fact]
    public void ToString_LocationSpan_ToString_FormatsCorrectly()
    {
        var start = new Location(_testFile, 4);
        var end = new Location(_testFile, 9);
        var span = new LocationSpan(start, end);
        var result = span.ToString();

        Assert.Equal("test.loom @ 1:5 - 1:9", result);
    }

    [Fact]
    public void ToString_EmptyLocationSpan_ToString_ReportsStartOnBothEnds()
    {
        var start = new Location(_testFile, 4);
        var span = new LocationSpan(start, start);

        Assert.Equal("test.loom @ 1:5 - 1:5", span.ToString());
    }

    [Fact]
    public void ToString_Location_ToString_FormatsCorrectly()
    {
        var location = new Location(_testFile, 4);
        var result = location.ToString();

        Assert.Equal("test.loom:1:5", result);
    }

    /// <remarks>
    ///     Both context lines carry their number. Without one on the trailing line a reader cannot tell
    ///     which of two identical-looking lines the frame is pointing at - the copy of a body in another
    ///     function reads exactly like the original.
    /// </remarks>
    [Fact]
    public void ToString_ContextLines_AreNumbered()
    {
        var start = new Location(_testFile, 19);
        var end = new Location(_testFile, 24);
        var diagnostic = new Diagnostic(new LocationSpan(start, end), DiagnosticSeverity.Error, InternalCodes.CannotFindName, "Cannot find name 'y'.", null);
        var result = diagnostic.ToString();

        Assert.Contains($"{Colors.Dim}1 │ let x: number = 5;{Colors.Reset}", result);
        Assert.Contains($"{Colors.Bold}2 │{Colors.Reset} let y = x + 10;", result);
        Assert.Contains($"{Colors.Dim}3 │ print(y);{Colors.Reset}", result);
    }

    /// <remarks>
    ///     The gutter is sized for every line the frame prints, and the trailing context line is one past
    ///     the span - on line 9 of a longer file the frame reaches line 10 and the whole frame has to be
    ///     two digits wide, or that one line steps a character out of the gutter.
    /// </remarks>
    [Fact]
    public void ToString_Gutter_FitsTrailingContextLine()
    {
        var lines = string.Join(Environment.NewLine, Enumerable.Range(1, 12).Select(i => $"line {i}"));
        var file = new SourceFile("rollover.loom", lines);
        var start = new Location(file, lines.IndexOf("line 9", StringComparison.Ordinal));
        var diagnostic = new Diagnostic(new LocationSpan(start, 6), DiagnosticSeverity.Error, null, "Error on line nine.", null);
        var result = diagnostic.ToString();

        Assert.Contains($"{Colors.Dim} 8 │ line 8{Colors.Reset}", result);
        Assert.Contains($"{Colors.Bold} 9 │{Colors.Reset} line 9", result);
        Assert.Contains($"{Colors.Dim}10 │ line 10{Colors.Reset}", result);
    }

    [Fact]
    public void Location_AdditionOperator()
    {
        var start = new Location(_testFile, 4);
        var end = start + 5;

        Assert.Equal(9, end.Position);
        Assert.Equal(9, end.Character);
        Assert.Equal(1, end.Line);
        Assert.Equal(_testFile, end.File);
    }

    [Fact]
    public void ToString_ColorCodes_AreProperlyClosed()
    {
        var start = new Location(_testFile, 0);
        var end = new Location(_testFile, 1);
        var span = new LocationSpan(start, end);
        var diagnostic = new Diagnostic(span, DiagnosticSeverity.Error, InternalCodes.TypeMismatch, "Test message", "Test hint");
        var result = diagnostic.ToString();

        var resetCount = result.Count(c => c == $"{Colors.Reset}"[0]);
        var colorStartCount = result.Count(c => c == '\u001b');

        Assert.Equal(colorStartCount, resetCount);
    }

    [Fact]
    public void ToString_NewLines_UseEnvironmentNewLine()
    {
        var start = new Location(_testFile, 0);
        var end = new Location(_testFile, 1);
        var span = new LocationSpan(start, end);
        var diagnostic = new Diagnostic(span, DiagnosticSeverity.Error, null, "Test message", null);
        var result = diagnostic.ToString();

        Assert.Contains(Environment.NewLine, result);
        Assert.DoesNotContain("\n", result.Replace(Environment.NewLine, ""));
        Assert.DoesNotContain("\r", result.Replace(Environment.NewLine, ""));
    }
}