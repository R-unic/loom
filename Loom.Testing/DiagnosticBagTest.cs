using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Text;

namespace Loom.Testing;

[Collection("Assembly")]
public class DiagnosticBagTest
{
    private readonly LocationSpan _span = LocationSpan.Empty(SourceFile.Empty);

    private Token NewToken(SyntaxKind kind = SyntaxKind.Identifier, string text = "x") => new(kind, _span, text);

    private Identifier NewIdentifier(string name = "x") => new(new Token(SyntaxKind.Identifier, _span, name));

    [Fact]
    public void Debug_Node_RecordsDiagnostic()
    {
        var bag = new DiagnosticBag();
        var node = NewIdentifier();
        bag.Debug(node, "my-code", "hello");
        Assert.Single(bag.Set);

        var diag = bag.Set.Single();
        Assert.Equal(DiagnosticSeverity.Debug, diag.Severity);
        Assert.Equal("my-code", diag.Code);
        Assert.Equal("hello", diag.Message);
        Assert.Equal(node.LocationSpan, diag.Span);
    }

    [Fact]
    public void Info_Node_RecordsDiagnostic()
    {
        var bag = new DiagnosticBag();
        var node = NewIdentifier();
        bag.Info(node, "my-code", "hello");
        Assert.Single(bag.Set);

        var diag = bag.Set.Single();
        Assert.Equal(DiagnosticSeverity.Info, diag.Severity);
        Assert.Equal("my-code", diag.Code);
        Assert.Equal("hello", diag.Message);
        Assert.Equal(node.LocationSpan, diag.Span);
    }

    [Fact]
    public void Warn_Token_RecordsDiagnostic()
    {
        var bag = new DiagnosticBag();
        var token = NewToken();
        bag.Warn(token, "w1", "watch out", "use bar");
        Assert.Single(bag.Set);

        var diag = bag.Set.Single();
        Assert.Equal(DiagnosticSeverity.Warn, diag.Severity);
        Assert.Equal("w1", diag.Code);
        Assert.Equal("watch out", diag.Message);
        Assert.Equal("use bar", diag.Hint);
        Assert.Equal(token.GetLocation(), diag.Span);
    }

    [Fact]
    public void Error_Node_RecordsDiagnostic()
    {
        var bag = new DiagnosticBag();
        var node = NewIdentifier();
        bag.Error(node, "e1", "oh no", "maybe x?");
        Assert.Single(bag.Set);

        var diag = bag.Set.Single();
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Equal("e1", diag.Code);
        Assert.Equal("oh no", diag.Message);
        Assert.Equal("maybe x?", diag.Hint);
    }

    [Fact]
    public void Info_LocationSpan_RecordsDiagnostic()
    {
        var bag = new DiagnosticBag();
        bag.Info(_span, "info-span", "info message");
        Assert.Single(bag.Set);

        var diag = bag.Set.Single();
        Assert.Equal(DiagnosticSeverity.Info, diag.Severity);
        Assert.Equal("info-span", diag.Code);
        Assert.Equal("info message", diag.Message);
        Assert.Null(diag.Hint);
    }

    [Fact]
    public void Report_MultipleDiagnostics_AreStored()
    {
        var bag = new DiagnosticBag();
        bag.Info(_span, "i1", "first");
        bag.Warn(_span, "w1", "second");
        bag.Error(_span, "e1", "third");
        Assert.Equal(3, bag.Set.Count);
    }

    [Fact]
    public void DuplicateDiagnostic_IgnoredByHashSet()
    {
        var bag = new DiagnosticBag();
        var diag = new Diagnostic(_span, DiagnosticSeverity.Error, "dup", "msg", null);
        bag.Set.Add(diag);
        bag.Set.Add(diag);
        Assert.Single(bag.Set);
    }

    [Fact]
    public void NotImplemented_Node_RecordsErrorWithNotImplementedCode()
    {
        var bag = new DiagnosticBag();
        var node = NewIdentifier();
        bag.NotImplemented(node, "custom feature");
        Assert.Single(bag.Set);

        var diag = bag.Set.Single();
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Equal(InternalCodes.NotImplemented, diag.Code);
        Assert.Equal("custom feature", diag.Message);
    }

    [Fact]
    public void NotImplemented_Node_DefaultMessage()
    {
        var bag = new DiagnosticBag();
        bag.NotImplemented(NewIdentifier());

        Assert.Single(bag.Set);
        var diag = bag.Set.Single();
        Assert.Equal("This feature is not yet implemented.", diag.Message);
    }

    // --- CompilerError ---

    [Fact]
    public void CompilerError_Node_UsesCompilerErrorCodeAndHint()
    {
        var bag = new DiagnosticBag();
        bag.CompilerError(NewIdentifier(), "internal error");

        Assert.Single(bag.Set);
        var diag = bag.Set.Single();
        Assert.Equal(InternalCodes.CompilerError, diag.Code);
        Assert.Equal("internal error", diag.Message);
        Assert.Equal("this is a compiler bug! please report an issue.", diag.Hint);
    }

    [Fact]
    public void CompilerError_File_CreatesEmptySpanForFile()
    {
        var bag = new DiagnosticBag();
        bag.CompilerError(SourceFile.Empty, "file error");

        Assert.Single(bag.Set);
        var diag = bag.Set.Single();
        Assert.Equal(SourceFile.Empty, diag.Span.File);
        Assert.Equal(0, diag.Span.Start.Character);
    }

    [Fact]
    public void Find_ReturnsMatchingDiagnostic()
    {
        var bag = new DiagnosticBag();
        bag.Error(_span, "e1", "first");
        bag.Warn(_span, "w1", "second");

        var found = bag.Find(d => d.Code == "w1");
        Assert.NotNull(found);
        Assert.Equal("w1", found.Code);
    }

    [Fact]
    public void WithoutInfo_RemovesInfoAndBelowDiagnostics()
    {
        var bag = new DiagnosticBag();
        bag.Info(_span, "d", "debug");
        bag.Info(_span, "i", "info");
        bag.Warn(_span, "w", "warn");

        var filtered = bag.WithoutInfo();
        Assert.Single(filtered.Set);
        Assert.Equal(DiagnosticSeverity.Warn, filtered.Set.First().Severity);
    }

    [Fact]
    public void Errors_ReturnsOnlyErrors()
    {
        var bag = new DiagnosticBag();
        bag.Warn(_span, "w", "warn");
        bag.Error(_span, "e", "error");
        bag.Info(_span, "i", "info");

        var errors = bag.Errors();
        Assert.Single(errors.Set);
        Assert.Equal("e", errors.Set.First().Code);
    }

    [Fact]
    public void ContainsErrors_ReturnsTrueWhenErrorsExist()
    {
        var bag = new DiagnosticBag();
        Assert.False(bag.ContainsErrors());
        bag.Error(_span, "e", "err");
        Assert.True(bag.ContainsErrors());
    }

    [Fact]
    public void Concat_CombinesMultipleBags()
    {
        var bag1 = new DiagnosticBag();
        bag1.Error(_span, "e1", "err1");
        var bag2 = new DiagnosticBag();
        bag2.Warn(_span, "w1", "warn1");
        var combined = DiagnosticBag.Concat([bag1, bag2]);
        Assert.Equal(2, combined.Set.Count);
    }

    [Fact]
    public void Concat_DeduplicatesByHashSet()
    {
        var diag = new Diagnostic(_span, DiagnosticSeverity.Error, "dup", "msg", null);
        var bag1 = new DiagnosticBag([diag]);
        var bag2 = new DiagnosticBag([diag]);
        var combined = DiagnosticBag.Concat([bag1, bag2]);
        Assert.Single(combined.Set);
    }

    [Fact]
    public void Report_WithFailFastDisabled_StillRecordsErrorWithoutExiting()
    {
        var originalFailFast = DiagnosticBag.FailFast;
        try
        {
            DiagnosticBag.FailFast = false;
            var bag = new DiagnosticBag();
            bag.Error(_span, "e", "fail");
            Assert.Single(bag.Set);
        }
        finally
        {
            DiagnosticBag.FailFast = originalFailFast;
        }
    }

    [Fact]
    public void Debug_Node_TwoArgOverload_RecordsDiagnosticWithNullCode()
    {
        var bag = new DiagnosticBag();
        var node = NewIdentifier();
        bag.Debug(node, "hello");
        Assert.Single(bag.Set);

        var diag = bag.Set.Single();
        Assert.Equal(DiagnosticSeverity.Debug, diag.Severity);
        Assert.Null(diag.Code);
        Assert.Equal("hello", diag.Message);
        Assert.Equal(node.LocationSpan, diag.Span);
    }

    [Fact]
    public void Info_Node_TwoArgOverload_RecordsDiagnosticWithNullCode()
    {
        var bag = new DiagnosticBag();
        var node = NewIdentifier();
        bag.Info(node, "hello");
        Assert.Single(bag.Set);

        var diag = bag.Set.Single();
        Assert.Equal(DiagnosticSeverity.Info, diag.Severity);
        Assert.Null(diag.Code);
        Assert.Equal("hello", diag.Message);
        Assert.Equal(node.LocationSpan, diag.Span);
    }

    [Fact]
    public void NotImplemented_Token_RecordsErrorWithNotImplementedCode()
    {
        var bag = new DiagnosticBag();
        var token = NewToken();
        bag.NotImplemented(token, "custom feature");
        Assert.Single(bag.Set);

        var diag = bag.Set.Single();
        Assert.Equal(DiagnosticSeverity.Error, diag.Severity);
        Assert.Equal(InternalCodes.NotImplemented, diag.Code);
        Assert.Equal("custom feature", diag.Message);
        Assert.Equal(token.GetLocation(), diag.Span);
    }

    [Fact]
    public void ToString_JoinsAllDiagnosticsWithNewline()
    {
        var bag = new DiagnosticBag();
        bag.Error(_span, "e1", "first");
        bag.Error(_span, "e2", "second");

        var result = bag.ToString();
        Assert.Contains("first", result);
        Assert.Contains("second", result);
    }

    [Fact]
    public void ToString_EmptyBag_ReturnsEmptyString()
    {
        var bag = new DiagnosticBag();
        Assert.Equal("", bag.ToString());
    }
}