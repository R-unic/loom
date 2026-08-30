using Loom.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace Loom.Testing.Lexing;

[Collection("Assembly")]
public class IncrementalTextTest
{
    [Fact]
    public void ApplyChange_WithNoRange_ReplacesWholeDocument() =>
        Assert.Equal("goodbye", IncrementalText.ApplyChange("hello", null, "goodbye"));

    [Fact]
    public void ApplyChange_ReplacesTextWithinRangeOnFirstLine()
    {
        var result = IncrementalText.ApplyChange(
            "let x = 1;",
            new LspRange(new Position(0, 8), new Position(0, 9)),
            "2"
        );

        Assert.Equal("let x = 2;", result);
    }

    [Fact]
    public void ApplyChange_ReplacesTextSpanningMultipleLines()
    {
        var result = IncrementalText.ApplyChange(
            "let x = 1;\nlet y = 2;\nlet z = 3;",
            new LspRange(new Position(0, 4), new Position(2, 5)),
            "a = 9"
        );

        Assert.Equal("let a = 9 = 3;", result);
    }

    [Fact]
    public void ApplyChange_InsertsAtEndOfDocument()
    {
        var result = IncrementalText.ApplyChange(
            "let x = 1;",
            new LspRange(new Position(0, 10), new Position(0, 10)),
            "\nlet y = 2;"
        );

        Assert.Equal("let x = 1;\nlet y = 2;", result);
    }

    [Fact]
    public void ApplyChanges_AppliesEachChangeToThePriorResult()
    {
        var changes = new[]
        {
            new TextDocumentContentChangeEvent { Range = new LspRange(new Position(0, 8), new Position(0, 9)), Text = "2" },
            new TextDocumentContentChangeEvent { Range = new LspRange(new Position(0, 9), new Position(0, 10)), Text = "!" }
        };

        Assert.Equal("let x = 2!", IncrementalText.ApplyChanges("let x = 1;", changes));
    }
}
