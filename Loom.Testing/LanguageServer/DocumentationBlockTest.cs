using Loom.LanguageServer;

namespace Loom.Testing.LanguageServer;

public class DocumentationBlockTest
{
    [Fact]
    public void Parse_WithNoTags_IsAllSummary()
    {
        var block = DocumentationBlock.Parse("Adds two numbers.\nNothing else to say.");
        Assert.Equal("Adds two numbers.\nNothing else to say.", block.Summary);
        Assert.Empty(block.Parameters);
        Assert.Null(block.Returns);
    }

    [Fact]
    public void Parse_SplitsParameterAndReturnTags()
    {
        var block = DocumentationBlock.Parse("Adds two numbers.\n@param x the first addend\n@param y the second addend\n@returns their sum");

        Assert.Equal("Adds two numbers.", block.Summary);
        Assert.Equal(["x", "y"], block.Parameters.Select(parameter => parameter.Name));
        Assert.Equal("the first addend", block.ForParameter("x"));
        Assert.Equal("their sum", block.Returns);
    }

    [Fact]
    public void Parse_ContinuesATagOntoTheFollowingLines() =>
        Assert.Equal("the first addend,\nwhich may be negative", DocumentationBlock.Parse("@param x the first addend,\nwhich may be negative").ForParameter("x"));

    [Fact]
    public void Parse_AcceptsTheSingularReturnTag() => Assert.Equal("their sum", DocumentationBlock.Parse("@return their sum").Returns);

    [Fact]
    public void Parse_LeavesATagWithNoNameAsProse() => Assert.Equal("@param", DocumentationBlock.Parse("@param").Summary);

    [Fact]
    public void Parse_DoesNotTreatALongerWordAsATag() => Assert.Equal("@parameters are described below", DocumentationBlock.Parse("@parameters are described below").Summary);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  ")]
    public void Parse_WithNothingToRead_IsEmpty(string? raw) => Assert.True(DocumentationBlock.Parse(raw).IsEmpty);

    [Fact]
    public void ToMarkdown_RendersEachTagAsItsOwnEntry()
    {
        var markdown = DocumentationBlock.Parse("Adds two numbers.\n@param x the first addend\n@returns their sum").ToMarkdown();
        Assert.Equal("Adds two numbers.\n\n- `x` — the first addend\n\n**Returns** — their sum", markdown);
    }

    [Fact]
    public void ForParameter_ForAnUndocumentedParameter_IsNull() => Assert.Null(DocumentationBlock.Parse("@param x the first addend").ForParameter("y"));
}
