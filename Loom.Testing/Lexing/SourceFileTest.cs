using Loom.Core.Text;
using Loom.Testing;

namespace Loom.Testing.Lexing;

[Collection("Assembly")]
public class SourceFileTest
{
    [Theory]
    [InlineData("", 0, 0, 0)]
    [InlineData("hello", 0, 0, 0)]
    [InlineData("hello", 2, 0, 2)]
    [InlineData("hello", 5, 0, 5)]
    [InlineData("hello\nworld", 0, 0, 0)]
    [InlineData("hello\nworld", 0, 1, 6)]
    [InlineData("hello\nworld", 3, 1, 9)]
    [InlineData("hello\nworld", 5, 1, 11)]
    [InlineData("hello\nworld\n!", 0, 2, 12)]
    public void GetSourcePosition_ReturnsCorrectPosition(
        string source,
        int character,
        int line,
        int expected)
    {
        var file = new SourceFile("test.loom", source);

        Assert.Equal(expected, file.GetSourcePosition(character, line));
    }
    
    [Fact]
    public void GetSourcePosition_ReturnsLineStart()
    {
        var file = new SourceFile("test.loom", "hello\nworld\n!");

        Assert.Equal(0, file.GetSourcePosition(0, 0));
        Assert.Equal(6, file.GetSourcePosition(0, 1));
        Assert.Equal(12, file.GetSourcePosition(0, 2));
    }

    [Fact]
    public void GetSourcePosition_ReturnsEndOfLine()
    {
        var file = new SourceFile("test.loom", "hello\nworld");

        Assert.Equal(5, file.GetSourcePosition(5, 0));
        Assert.Equal(11, file.GetSourcePosition(5, 1));
    }

    [Fact]
    public void GetSourcePosition_WorksWithEmptyLines()
    {
        var file = new SourceFile("test.loom", "hello\n\nworld");

        Assert.Equal(0, file.GetSourcePosition(0, 0));
        Assert.Equal(6, file.GetSourcePosition(0, 1));
        Assert.Equal(7, file.GetSourcePosition(0, 2));
    }
    
    [Theory]
    [InlineData("", 0, 1)]
    [InlineData("hello", 0, 1)]
    [InlineData("hello", 2, 1)]
    [InlineData("hello", 5, 1)]
    [InlineData("hello\nworld", 0, 1)]
    [InlineData("hello\nworld", 5, 1)]
    [InlineData("hello\nworld", 6, 2)]
    [InlineData("hello\nworld", 9, 2)]
    [InlineData("hello\nworld", 11, 2)]
    [InlineData("hello\nworld\n!", 12, 3)]
    [InlineData("hello\n\nworld", 6, 2)]
    [InlineData("hello\n\nworld", 7, 3)]
    public void GetLineFromPosition_ReturnsCorrectLine(
        string source,
        int position,
        int expectedLine)
    {
        var file = new SourceFile("test.loom", source);

        Assert.Equal(expectedLine, file.GetLineFromPosition(position));
    }

    [Theory]
    [InlineData("", 0, 0)]
    [InlineData("hello", 0, 0)]
    [InlineData("hello", 2, 2)]
    [InlineData("hello", 5, 5)]
    [InlineData("hello\nworld", 0, 0)]
    [InlineData("hello\nworld", 5, 5)]
    [InlineData("hello\nworld", 6, 0)]
    [InlineData("hello\nworld", 9, 3)]
    [InlineData("hello\nworld", 11, 5)]
    public void GetCharacterFromPosition_ReturnsCorrectCharacter(
        string source,
        int position,
        int expectedCharacter)
    {
        var file = new SourceFile("test.loom", source);

        Assert.Equal(expectedCharacter, file.GetCharacterFromPosition(position));
    }

    [Theory]
    [InlineData("hello", 0)]
    [InlineData("hello", 2)]
    [InlineData("hello", 5)]
    [InlineData("hello\nworld", 0)]
    [InlineData("hello\nworld", 5)]
    [InlineData("hello\nworld", 6)]
    [InlineData("hello\nworld", 9)]
    [InlineData("hello\nworld", 11)]
    [InlineData("hello\n\nworld", 0)]
    [InlineData("hello\n\nworld", 6)]
    [InlineData("hello\n\nworld", 7)]
    [InlineData("hello\n\nworld", 12)]
    public void SourcePosition_RoundTrips(string source, int position)
    {
        var file = new SourceFile("test.loom", source);

        var line = file.GetLineFromPosition(position);
        var character = file.GetCharacterFromPosition(position);

        Assert.Equal(position, file.GetSourcePosition(character, line - 1));
    }
}