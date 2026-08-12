using System.Diagnostics.CodeAnalysis;
using Loom.Core.Diagnostics;
using Loom.Core.Lexing;
using Loom.Core.Parsing.AST;
using Loom.Core.Text;

namespace Loom.Core.Parsing;

public sealed partial class Parser(LexerResult lexerResult)
{
    private readonly DiagnosticBag _diagnostics = new(options: lexerResult.Diagnostics.Options);
    private int _position;

    public ParserResult Parse()
    {
        var statements = new List<Statement>();
        while (!IsEof())
        {
            var previousPosition = _position;
            statements.Add(ParseStatement());
            EnsureProgress(previousPosition);
        }

        var tree = new Tree(lexerResult, statements);
        return new ParserResult(tree, _diagnostics);
    }

    private void EnsureProgress(int previousPosition)
    {
        if (_position != previousPosition || IsEof())
            return;

        Advance();
    }

    private List<T> ParseDelimited<T>(Func<T> parse, SyntaxKind delimiter = SyntaxKind.Comma)
    {
        var first = parse();
        if (first == null)
            return [];

        var nodes = new List<T> { first };
        while (Match(delimiter))
        {
            var node = parse();
            if (node == null) continue;
            nodes.Add(node);
        }

        return nodes;
    }

    private bool AtContextualKeyword(string text) => !IsEof() && Current() is { Kind: SyntaxKind.Identifier } token && token.Text == text;

    private Token ExpectContextualKeyword(string text)
    {
        if (AtContextualKeyword(text))
            return Advance();

        _diagnostics.Error(
            Current(),
            IsEof() ? InternalCodes.UnexpectedEof : InternalCodes.UnexpectedToken,
            $"Expected '{text}', got {SafeTokenText(IsEof() ? null : Current())}."
        );

        return MissingToken(SyntaxKind.Identifier);
    }

    private bool AssertDeclarationInsideOfBlock(Statement statement)
    {
        if (statement is not NamedDeclaration namedDeclaration)
            return true;

        _diagnostics.Error(
            namedDeclaration,
            InternalCodes.DeclarationOutsideOfBlock,
            "Declarations can only be declared inside of a block.",
            "surround with '{' and '}'"
        );

        return false;
    }

    private bool Match([MaybeNullWhen(false)] out Token token, SyntaxKind kindA, SyntaxKind kindB) => Match(out token, kind => kind == kindA || kind == kindB);
    private bool Match(SyntaxKind kind) => Match(out _, kind);
    private void Match(SyntaxKind kindA, SyntaxKind kindB) => Match(out _, kindA, kindB);

    private bool Match([MaybeNullWhen(false)] out Token token, SyntaxKind kind) => Match(out token, matched => matched == kind);

    private bool Match([MaybeNullWhen(false)] out Token token, Predicate<SyntaxKind> predicate)
    {
        token = null;
        if (IsEof() || !predicate(Current().Kind))
            return false;

        token = Advance();
        return true;
    }

    /// <summary>
    ///     Consumes the <c>async</c> in front of an <c>fn</c>, if one is written. A function can begin in six
    ///     places - statement, expression, trait member, implement member, declared signature, and type
    ///     position - and every one of them accepts the modifier, so they share this rather than each
    ///     spelling out the match.
    /// </summary>
    private Token? MatchAsyncKeyword() => Match(out var asyncKeyword, SyntaxKind.AsyncKeyword) ? asyncKeyword : null;

    private Token ExpectIdentifier() => ExpectIdentifier("identifier");
    private Token ExpectIdentifier(string expected) => Expect(SyntaxKind.Identifier, expected);
    private Token Expect(SyntaxKind kind, string expected) => Expect(kind, token => $"Expected {expected}, got {SafeTokenText(token)}.");

    private Token Expect(SyntaxKind kind, Func<Token?, string>? message = null)
    {
        if (Current().Kind == kind)
            return Advance();

        var current = Current();
        var expected = SyntaxFacts.GetText(kind) ?? kind.ToString();

        if (IsEof())
            _diagnostics.Error(
                current,
                InternalCodes.UnexpectedEof,
                message != null ? message(null) : $"Expected '{expected}', got EOF."
            );
        else
            _diagnostics.Error(
                current,
                InternalCodes.UnexpectedToken,
                message != null ? message(current) : $"Expected '{expected}', got {SafeTokenText(current)}."
            );

        return MissingToken(kind);
    }

    private Token Advance()
    {
        var current = Current();
        _position++;
        return current;
    }

    private Token MissingToken(SyntaxKind kind)
    {
        var current = Current();
        var text = SyntaxFacts.GetText(kind) ?? string.Empty;

        return new Token(
            kind,
            current.File,
            new TextSpan(current.Span.Position, 0),
            text
        );
    }

    private int? OffsetAfterBrackets(int startOffset = 0)
    {
        if (PeekKind(startOffset) != SyntaxKind.LBracket)
            return null;

        var depth = 1;

        for (var i = startOffset + 1;; i++)
            switch (PeekKind(i))
            {
                case SyntaxKind.LBracket:
                    depth++;
                    break;
                case SyntaxKind.RBracket:
                    if (--depth == 0)
                        return i;

                    break;

                case SyntaxKind.Eof:
                    return null;
            }
    }

    private Token Current() => lexerResult.Tokens[_position];

    private SyntaxKind PeekKind(int offset)
    {
        var index = _position + offset;
        return index >= 0 && index < lexerResult.Tokens.Count ? lexerResult.Tokens[index].Kind : SyntaxKind.Eof;
    }

    private bool IsEof() => Current().Kind == SyntaxKind.Eof;
    private static string SafeTokenText(Token? token) => token is { Kind: not SyntaxKind.Eof } ? $"'{token.Text}'" : "EOF";
}