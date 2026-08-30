using Loom.Core.Diagnostics;
using Loom.Core.Text;
using Loom.Testing;

namespace Loom.Testing.Lexing;

[Collection("Assembly")]
public class LexerTest
{
    public static readonly List<object[]> Operators = [.. SyntaxFacts.OperatorMap.Where(pair => SyntaxFacts.IsNotTrivia(pair.Value)).Select(t => new object[] { t.Key, t.Value })];
    public static readonly List<object[]> Keywords = [.. SyntaxFacts.KeywordMap.Select(t => new object[] { t.Key, t.Value })];

    [Theory]
    [InlineData("$")]
    [InlineData("\\")]
    [InlineData("`")]
    public void ThrowsFor_UnexpectedCharacters(string source)
    {
        var diagnostics = Utility.GetLexerDiagnostics(source);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.UnexpectedCharacter, $"Unexpected character '{source}'.");
    }

    [Theory]
    [InlineData("0x", "0x")]
    [InlineData("0X", "0X")]
    [InlineData("0x.", "0x")]
    [InlineData("0x ", "0x")]
    [InlineData("0xG", "0x")]
    public void ThrowsFor_MalformedHexLiteral(string source, string matched)
    {
        var diagnostics = Utility.GetLexerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.MalformedNumber,
            $"Malformed hexadecimal literal '{matched}': expected at least one hex digit after '0x'."
        );
    }

    [Theory]
    [InlineData("0b", "0b")]
    [InlineData("0B", "0B")]
    [InlineData("0b.", "0b")]
    [InlineData("0b2", "0b")]
    [InlineData("0b ", "0b")]
    public void ThrowsFor_MalformedBinaryLiteral(string source, string matched)
    {
        var diagnostics = Utility.GetLexerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.MalformedNumber,
            $"Malformed binary literal '{matched}': expected at least one binary digit after '0b'."
        );
    }

    [Theory]
    [InlineData("0o", "0o")]
    [InlineData("0O", "0O")]
    [InlineData("0o.", "0o")]
    [InlineData("0o8", "0o")]
    [InlineData("0o ", "0o")]
    public void ThrowsFor_MalformedOctalLiteral(string source, string matched)
    {
        var diagnostics = Utility.GetLexerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.MalformedNumber,
            $"Malformed octal literal '{matched}': expected at least one octal digit after '0o'."
        );
    }

    [Theory]
    [InlineData("1e", "1e")]
    [InlineData("1E", "1E")]
    [InlineData("1e+", "1e")]
    [InlineData("1e-", "1e-")]
    [InlineData("1e ", "1e")]
    [InlineData("3.14e", "3.14e")]
    [InlineData("1_0e", "1_0e")]
    [InlineData("1_0.2_3e", "1_0.2_3e")]
    public void ThrowsFor_MalformedScientificNotation(string source, string matched)
    {
        var diagnostics = Utility.GetLexerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.MalformedNumber,
            $"Malformed scientific notation '{matched}': expected one or more digits after the exponent."
        );
    }

    [Theory]
    [InlineData("1.", "1.")]
    [InlineData("42.", "42.")]
    [InlineData("1_0.", "1_0.")]
    [InlineData("1. ", "1.")]
    [InlineData("1.e5", "1.")]
    public void ThrowsFor_MalformedFloatLiteral(string source, string matched)
    {
        var diagnostics = Utility.GetLexerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.MalformedNumber,
            $"Malformed float literal '{matched}': expected one or more digits after the decimal point."
        );
    }

    [Theory]
    [InlineData("'abc\"", true)]
    [InlineData("\"abc'")]
    [InlineData("\"abc\n\n")]
    [InlineData("\"abc")]
    [InlineData("'abc", true)]
    [InlineData("\"")]
    [InlineData("'", true)]
    public void ThrowsFor_UnterminatedString(string source, bool singleQuote = false)
    {
        var diagnostics = Utility.GetLexerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.UnterminatedString,
            $"Unterminated string literal: expected closing {(singleQuote ? "\"'\"" : "'\"'")}."
        );
    }

    [Fact]
    public void ThrowsFor_UnterminatedBlockComment()
    {
        var diagnostics = Utility.GetLexerDiagnostics("#: hello!");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.UnterminatedComment, "Unterminated block comment: expected closing ':#'.");
    }

    [Theory]
    [InlineData("s")]
    [InlineData("ms")]
    [InlineData("hz")]
    [InlineData("HZ")]
    public void Tokenizes_UnitSuffixesAsIdentifiers_WhenNotPrefixedByNumber(string source)
    {
        var tokens = Utility.GetTokens(source);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(SyntaxKind.Identifier, tokens[0].Kind);
    }

    [Theory]
    [InlineData("## this is a comment")]
    [InlineData("##")]
    [InlineData("## 123 !@#$%")]
    public void Tokenizes_LineComments(string source)
    {
        var tokens = Utility.GetTokens(source, true);
        Assert.Equal(2, tokens.Count);

        var token = tokens[0];
        Assert.Equal(SyntaxKind.Comment, token.Kind);
    }

    [Theory]
    [InlineData("#: this is a multiline comment :#")]
    [InlineData("#::#")]
    [InlineData("#: line one\nline two :#")]
    [InlineData("#: ## nested-looking content :#")]
    public void Tokenizes_BlockComments(string source)
    {
        var tokens = Utility.GetTokens(source, true);
        Assert.Equal(2, tokens.Count);

        var token = tokens[0];
        Assert.Equal(SyntaxKind.BlockComment, token.Kind);
    }

    [Fact]
    public void LineComment_DoesNotConsume_Newline()
    {
        var tokens = Utility.GetTokens("## comment\ntrue", true);
        Assert.Equal(4, tokens.Count);
        Assert.Equal(SyntaxKind.Comment, tokens[0].Kind);
        Assert.Equal(SyntaxKind.Whitespace, tokens[1].Kind);
        Assert.Equal(SyntaxKind.TrueLiteral, tokens[2].Kind);
        Assert.Equal(SyntaxKind.Eof, tokens[3].Kind);

        Assert.Equal(1, tokens[0].GetLocation().Start.Line);
        Assert.Equal(1, tokens[0].GetLocation().End.Line);
        Assert.Equal(2, tokens[2].GetLocation().Start.Line);
    }

    [Fact]
    public void BlockComment_ContainingLoneColonOrHash_ConsumesFullContent()
    {
        var tokens = Utility.GetTokens("#: ## nested-looking content :# true", true);
        Assert.Equal(4, tokens.Count);
        Assert.Equal(SyntaxKind.BlockComment, tokens[0].Kind);
        Assert.Equal("#: ## nested-looking content :#", tokens[0].Text);
        Assert.Equal(SyntaxKind.Whitespace, tokens[1].Kind);
        Assert.Equal(SyntaxKind.TrueLiteral, tokens[2].Kind);
        Assert.Equal(SyntaxKind.Eof, tokens[3].Kind);
    }

    [Fact]
    public void BlockComment_ContainingLoneColonOrHash_NoDiagnostics() =>
        Utility.AssertNoErrors(Utility.GetLexerDiagnostics("#: a:b c# :# true"));

    [Fact]
    public void BlockComment_Tracks_Lines()
    {
        var tokens = Utility.GetTokens("#: line one\nline two\nline three :#", true);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(SyntaxKind.BlockComment, tokens[0].Kind);
        Assert.Equal(SyntaxKind.Eof, tokens[1].Kind);

        var comment = tokens[0];
        Assert.Equal(1, comment.GetLocation().Start.Line);
        Assert.Equal(3, comment.GetLocation().End.Line);
    }

    [Fact]
    public void LineComment_AfterCode()
    {
        var tokens = Utility.GetTokens("true ## comment", true);
        Assert.Equal(4, tokens.Count);
        Assert.Equal(SyntaxKind.TrueLiteral, tokens[0].Kind);
        Assert.Equal(SyntaxKind.Whitespace, tokens[1].Kind);
        Assert.Equal(SyntaxKind.Comment, tokens[2].Kind);
        Assert.Equal(SyntaxKind.Eof, tokens[3].Kind);
    }

    [Fact]
    public void BlockComment_BetweenCode()
    {
        var tokens = Utility.GetTokens("true #: ignored :# false", true);
        Assert.Equal(6, tokens.Count);
        Assert.Equal(SyntaxKind.TrueLiteral, tokens[0].Kind);
        Assert.Equal(SyntaxKind.Whitespace, tokens[1].Kind);
        Assert.Equal(SyntaxKind.BlockComment, tokens[2].Kind);
        Assert.Equal(SyntaxKind.Whitespace, tokens[3].Kind);
        Assert.Equal(SyntaxKind.FalseLiteral, tokens[4].Kind);
        Assert.Equal(SyntaxKind.Eof, tokens[5].Kind);
    }

    [Fact]
    public void BlockComment_DoesNotConsume_TrailingCode_AcrossLines()
    {
        var tokens = Utility.GetTokens("#: comment\nstill comment :# true", true);
        Assert.Equal(4, tokens.Count);
        Assert.Equal(SyntaxKind.BlockComment, tokens[0].Kind);
        Assert.Equal(SyntaxKind.Whitespace, tokens[1].Kind);
        Assert.Equal(SyntaxKind.TrueLiteral, tokens[2].Kind);
        Assert.Equal(SyntaxKind.Eof, tokens[3].Kind);

        Assert.Equal(2, tokens[1].GetLocation().Start.Line);
    }

    [Fact]
    public void Tokenizes_WithTriviaFalse_ExcludesWhitespaceAndComments()
    {
        var tokens = Utility.GetTokens("true  ## comment\nfalse");
        Assert.Equal(3, tokens.Count);
        Assert.Equal(SyntaxKind.TrueLiteral, tokens[0].Kind);
        Assert.Equal(SyntaxKind.FalseLiteral, tokens[1].Kind);
        Assert.Equal(SyntaxKind.Eof, tokens[2].Kind);
    }

    [Fact]
    public void Tokenizes_WithTriviaTrue_IncludesWhitespaceAndComments()
    {
        var tokens = Utility.GetTokens("true  ## comment\nfalse", true);
        Assert.Equal(6, tokens.Count);
        Assert.Equal(SyntaxKind.TrueLiteral, tokens[0].Kind);
        Assert.Equal(SyntaxKind.Whitespace, tokens[1].Kind);
        Assert.Equal(SyntaxKind.Comment, tokens[2].Kind);
        Assert.Equal(SyntaxKind.Whitespace, tokens[3].Kind);
        Assert.Equal(SyntaxKind.FalseLiteral, tokens[4].Kind);
        Assert.Equal(SyntaxKind.Eof, tokens[5].Kind);
    }

    [Fact]
    public void Tokenizes_Range()
    {
        var tokens = Utility.GetTokens("1..5");
        Assert.Equal(4, tokens.Count);
        Assert.Equal(SyntaxKind.NumberLiteral, tokens[0].Kind);
        Assert.Equal(SyntaxKind.DotDot, tokens[1].Kind);
        Assert.Equal(SyntaxKind.NumberLiteral, tokens[2].Kind);
        Assert.Equal(SyntaxKind.Eof, tokens[3].Kind);
    }

    [Fact]
    public void Tokenizes_MultipleOperators()
    {
        var lexemes = Operators.ConvertAll(a => a[0]).Cast<string>();
        var expectedSyntaxes = Operators.ConvertAll(a => a[1]).Cast<SyntaxKind>().ToList();
        var source = string.Join(' ', lexemes);
        var tokens = Utility.GetTokens(source);
        Assert.Equal(1 + expectedSyntaxes.Count, tokens.Count);

        for (var i = 0; i < tokens.Count; ++i)
        {
            var actual = tokens[i];
            var expected = expectedSyntaxes.Count > i ? expectedSyntaxes[i] : SyntaxKind.Eof;
            Assert.Equal(expected, actual.Kind);
        }
    }

    [Fact]
    public void Tokenizes_Eof()
    {
        var tokens = Utility.GetTokens("");
        Assert.Single(tokens);

        var token = tokens[0];
        Assert.Equal(SyntaxKind.Eof, token.Kind);
    }

    [Theory]
    [MemberData(nameof(Operators))]
    public void Tokenizes_Operators(string source, SyntaxKind expected)
    {
        var tokens = Utility.GetTokens(source);
        Assert.Equal(2, tokens.Count);

        var token = tokens[0];
        Assert.Equal(expected, token.Kind);
    }

    [Theory]
    [InlineData("69")]
    [InlineData("69_420")]
    [InlineData("123456")]
    [InlineData("1234567890")]
    [InlineData("1e5")]
    [InlineData("420_69.69_420")]
    [InlineData("420.69")]
    [InlineData(".420")]
    [InlineData("0.234")]
    [InlineData("1.24335e-3")]
    [InlineData("1_0.5_0_e-1_0")]
    [InlineData("1.24335e5")]
    [InlineData("1_2.24_3_35e1_1")]
    [InlineData("5s")]
    [InlineData("5ms")]
    [InlineData("5hz")]
    [InlineData("2_0.2_3Hz")]
    [InlineData("0.5s")]
    [InlineData("10m")]
    [InlineData("1M")]
    [InlineData("3H")]
    [InlineData("4h")]
    [InlineData("1_000")]
    [InlineData("1_000.5")]
    [InlineData("1.5e1_0")]
    [InlineData("1_0_0s")]
    [InlineData("1.5_0ms")]
    public void Tokenizes_Numbers(string source)
    {
        var tokens = Utility.GetTokens(source);
        Assert.Equal(2, tokens.Count);

        var token = tokens[0];
        Assert.Equal(SyntaxKind.NumberLiteral, token.Kind);
    }

    [Theory]
    [InlineData("\"abcd\"")]
    [InlineData(""" "\"abcd\"" """)]
    [InlineData("'abc'")]
    [InlineData(@"'\'abc\''")]
    public void Tokenizes_Strings(string source)
    {
        var tokens = Utility.GetTokens(source);
        Assert.Equal(2, tokens.Count);

        var token = tokens[0];
        Assert.Equal(SyntaxKind.StringLiteral, token.Kind);
        Assert.Equal(source.Trim(), token.Text);
    }

    [Fact]
    public void Tokenizes_InterpolatedStrings()
    {
        var tokens = Utility.GetTokens("$\"Welcome, {name}!\"");
        Assert.Equal(
            [
                SyntaxKind.InterpolatedStringStart,
                SyntaxKind.InterpolatedStringText,
                SyntaxKind.LBrace,
                SyntaxKind.Identifier,
                SyntaxKind.RBrace,
                SyntaxKind.InterpolatedStringText,
                SyntaxKind.InterpolatedStringEnd,
                SyntaxKind.Eof
            ],
            tokens.Select(t => t.Kind)
        );

        Assert.Equal("$\"", tokens[0].Text);
        Assert.Equal("Welcome, ", tokens[1].Text);
        Assert.Equal("name", tokens[3].Text);
        Assert.Equal("!", tokens[5].Text);
        Assert.Equal("\"", tokens[6].Text);
    }

    [Fact]
    public void Tokenizes_InterpolatedStrings_WithSingleQuote()
    {
        var tokens = Utility.GetTokens("$'hi {name}'");
        Assert.Equal(
            [
                SyntaxKind.InterpolatedStringStart,
                SyntaxKind.InterpolatedStringText,
                SyntaxKind.LBrace,
                SyntaxKind.Identifier,
                SyntaxKind.RBrace,
                SyntaxKind.InterpolatedStringEnd,
                SyntaxKind.Eof
            ],
            tokens.Select(t => t.Kind)
        );
    }

    [Fact]
    public void Tokenizes_InterpolatedStrings_WithNestedBraces()
    {
        var tokens = Utility.GetTokens("""$"{match 1 { 1 -> "a", _ -> "b" }}" """);
        Assert.Equal(
            [
                SyntaxKind.InterpolatedStringStart,
                SyntaxKind.LBrace,
                SyntaxKind.MatchKeyword,
                SyntaxKind.NumberLiteral,
                SyntaxKind.LBrace,
                SyntaxKind.NumberLiteral,
                SyntaxKind.Arrow,
                SyntaxKind.StringLiteral,
                SyntaxKind.Comma,
                SyntaxKind.Identifier,
                SyntaxKind.Arrow,
                SyntaxKind.StringLiteral,
                SyntaxKind.RBrace,
                SyntaxKind.RBrace,
                SyntaxKind.InterpolatedStringEnd,
                SyntaxKind.Eof
            ],
            tokens.Select(t => t.Kind)
        );
    }

    [Fact]
    public void Tokenizes_InterpolatedStrings_WithEscapedBrace()
    {
        var tokens = Utility.GetTokens(@"$""literal \{brace\} here""");
        Assert.Equal(
            [SyntaxKind.InterpolatedStringStart, SyntaxKind.InterpolatedStringText, SyntaxKind.InterpolatedStringEnd, SyntaxKind.Eof],
            tokens.Select(t => t.Kind)
        );

        Assert.Equal(@"literal \{brace\} here", tokens[1].Text);
    }

    [Theory]
    [InlineData("$\"unterminated", "'\"'")]
    [InlineData("$'unterminated", "\"'\"")]
    [InlineData("$\"unterminated {1 + 1}", "'\"'")]
    public void ThrowsFor_UnterminatedInterpolatedString(string source, string quotedQuote)
    {
        var diagnostics = Utility.GetLexerDiagnostics(source);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.UnterminatedString, $"Unterminated string literal: expected closing {quotedQuote}.");
    }

    [Theory]
    [InlineData("_abc_")]
    [InlineData("abc123")]
    [InlineData("abc_123")]
    [InlineData("AbC_123_")]
    public void Tokenizes_Identifiers(string source)
    {
        var tokens = Utility.GetTokens(source);
        Assert.Equal(2, tokens.Count);

        var token = tokens[0];
        Assert.Equal(SyntaxKind.Identifier, token.Kind);
    }

    [Theory]
    [MemberData(nameof(Keywords))]
    public void Tokenizes_Keywords(string source, SyntaxKind expected)
    {
        var tokens = Utility.GetTokens(source);
        Assert.Equal(2, tokens.Count);

        var token = tokens[0];
        Assert.Equal(expected, token.Kind);
    }

    [Fact]
    public void Tokenizes_TracksLineAndColumnNumbers()
    {
        var tokens = Utility.GetTokens("abc\n123\nxyz", true);
        Assert.Equal(6, tokens.Count);

        var first = tokens[0];
        Assert.Equal(1, first.GetLocation().Start.Line);
        Assert.Equal(0, first.GetLocation().Start.Character);
        Assert.Equal(3, first.GetLocation().End.Character);

        var firstWhitespace = tokens[1];
        Assert.Equal(1, firstWhitespace.GetLocation().Start.Line);
        Assert.Equal(3, firstWhitespace.GetLocation().Start.Character);
        Assert.Equal(2, firstWhitespace.GetLocation().End.Line);
        Assert.Equal(0, firstWhitespace.GetLocation().End.Character);

        var number = tokens[2];
        Assert.Equal(2, number.GetLocation().Start.Line);
        Assert.Equal(0, number.GetLocation().Start.Character);
        Assert.Equal(3, number.GetLocation().End.Character);
    }

    [Theory]
    [InlineData("hello_world", SyntaxKind.Identifier)]
    [InlineData("'abcdef'", SyntaxKind.StringLiteral)]
    [InlineData("123456", SyntaxKind.NumberLiteral)]
    [InlineData("+=", SyntaxKind.PlusEquals)]
    [InlineData("## hello", SyntaxKind.Comment)]
    public void Tokenizes_VeryLongFile(string lineText, SyntaxKind syntaxKind)
    {
        const int identifierCount = 60000;
        var source = string.Join('\n', Enumerable.Repeat(lineText, identifierCount));
        var tokens = Utility.GetTokens(source, true);
        Assert.Equal(identifierCount * 2, tokens.Count);

        for (var i = 0; i < identifierCount; i += 2)
        {
            var token = tokens[i];
            var whitespace = tokens[i + 1];
            Assert.Equal(lineText, token.Text);
            Assert.Equal(syntaxKind, token.Kind);
            Assert.Equal("\n", whitespace.Text);
            Assert.Equal(SyntaxKind.Whitespace, whitespace.Kind);
        }
    }

    [Fact]
    public void Tokenizes_ProperSpan_WithWhitespace()
    {
        var tokens = Utility.GetTokens("true false");
        Assert.Equal(3, tokens.Count);

        var first = tokens[0];
        var second = tokens[^2];
        var eof = tokens[^1];
        var firstStart = first.GetLocation().Start;
        var firstEnd = first.GetLocation().End;
        var secondStart = second.GetLocation().Start;
        var secondEnd = second.GetLocation().End;
        var eofStart = eof.GetLocation().Start;
        var eofEnd = eof.GetLocation().End;
        Assert.Equal(firstStart.Line, firstEnd.Line);
        Assert.Equal(firstStart.Character, firstStart.Position);
        Assert.Equal(firstEnd.Character, firstEnd.Position);
        Assert.Equal(0, firstStart.Position);
        Assert.Equal(4, firstEnd.Position);

        Assert.Equal(secondStart.Line, secondEnd.Line);
        Assert.Equal(secondStart.Character, secondStart.Position);
        Assert.Equal(secondEnd.Character, secondEnd.Position);
        Assert.Equal(5, secondStart.Position);
        Assert.Equal(10, secondEnd.Position);

        Assert.Equal(eofStart.Line, eofEnd.Line);
        Assert.Equal(eofStart.Character, eofStart.Position);
        Assert.Equal(eofEnd.Character, eofEnd.Position);
        Assert.Equal(10, eofStart.Position);
        Assert.Equal(10, eofEnd.Position);
    }

    [Fact]
    public void Tokenizes_ProperSpan()
    {
        var tokens = Utility.GetTokens("true");
        Assert.Equal(2, tokens.Count);

        var token = tokens[0];
        var start = token.GetLocation().Start;
        var end = token.GetLocation().End;
        Assert.Equal(start.Line, end.Line);
        Assert.Equal(start.Character, start.Position);
        Assert.Equal(end.Character, end.Position);
        Assert.Equal(0, start.Position);
        Assert.Equal(4, end.Position);
    }
}