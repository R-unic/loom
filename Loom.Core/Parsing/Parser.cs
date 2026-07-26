using System.Diagnostics.CodeAnalysis;
using Loom.Core.Diagnostics;
using Loom.Core.Lexing;
using Loom.Core.Parsing.AST;
using Loom.Core.Text;

namespace Loom.Core.Parsing;

public sealed partial class Parser(LexerResult lexerResult)
{
    private readonly DiagnosticBag _diagnostics = new();
    private int _position;

    public ParserResult Parse()
    {
        var statements = new List<Statement>();
        while (!IsEof())
            statements.Add(ParseStatement());

        var tree = new Tree(lexerResult, statements);
        return new ParserResult(tree, _diagnostics);
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

    private Statement ParseImport(Token importKeyword)
    {
        if (Match(out var star, SyntaxKind.Star))
            return ParseNamespaceImport(importKeyword, star);

        Match(out var typeKeyword, SyntaxKind.TypeKeyword);

        var leftBrace = Expect(SyntaxKind.LBrace, "'{' after 'import'");
        var specifiers = !IsEof() && Current() is { Kind: SyntaxKind.Identifier }
            ? ParseDelimited(ParseImportSpecifier).OfType<ImportSpecifier>().ToList()
            : [];
        
        var rightBrace = Expect(SyntaxKind.RBrace);
        if (specifiers.Count == 0)
            _diagnostics.Error(importKeyword, InternalCodes.EmptyImportClause, "Import declaration must name at least one member.");

        var fromKeyword = ExpectContextualKeyword("from");
        var pathToken = Expect(SyntaxKind.StringLiteral, "module path");

        return new ImportDeclaration(
            importKeyword,
            typeKeyword,
            leftBrace,
            specifiers,
            rightBrace,
            fromKeyword,
            new Literal(pathToken, LiteralUtility.ResolveValue(pathToken))
        );
    }
    
    private Statement ParseNamespaceImport(Token importKeyword, Token star)
    {
        var asKeyword = Expect(SyntaxKind.AsKeyword, "'as' after 'import *'");
        var name = ExpectIdentifier("namespace name");
        var fromKeyword = ExpectContextualKeyword("from");
        var pathToken = Expect(SyntaxKind.StringLiteral, "module path");

        return new NamespaceImport(
            importKeyword,
            star,
            asKeyword,
            name,
            fromKeyword,
            new Literal(pathToken, LiteralUtility.ResolveValue(pathToken))
        );
    }

    private ImportSpecifier? ParseImportSpecifier() =>
        !Match(out var name, SyntaxKind.Identifier)
            ? null
            : Match(out var asKeyword, SyntaxKind.AsKeyword)
                ? new ImportSpecifier(name, asKeyword, ExpectIdentifier("import alias"))
                : new ImportSpecifier(name, null, null);

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

    private bool ValidateFunctionSignature(string kind, LocationSpan span, [NotNullWhen(true)] ColonTypeClause? returnType, Parameters? parameters) =>
        ValidateSignatureReturnType(kind, span, returnType) && ValidateSignatureParameters(kind, parameters);

    private bool ValidateSignatureParameters(string kind, Parameters? parameters)
    {
        var parameterWithDefault = parameters?.ParameterList.Find(p => p.EqualsValueClause != null);
        if (parameterWithDefault != null)
        {
            _diagnostics.Error(
                parameterWithDefault,
                InternalCodes.UseOfDeclareFnParameterDefaults,
                $"Parameters may not have default values in {kind}."
            );

            return false;
        }

        var parameterWithoutType = parameters?.ParameterList.Find(p => p.ColonTypeClause == null);
        if (parameterWithoutType == null)
            return true;

        _diagnostics.Error(
            parameterWithoutType,
            InternalCodes.MissingDeclareFnParameterType,
            $"Parameters must have types in {kind}."
        );

        return false;
    }

    private bool ValidateSignatureReturnType(string kind, LocationSpan span, ColonTypeClause? returnType)
    {
        if (returnType != null)
            return true;

        _diagnostics.Error(
            span,
            InternalCodes.MissingDeclareFnReturnType,
            $"{(kind.Length > 0 ? char.ToUpperInvariant(kind[0]) + kind[1..] : kind)} must have a return type."
        );

        return false;
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

    private bool MatchClosingArrow([MaybeNullWhen(false)] out Token closingArrow)
    {
        closingArrow = null;
        if (IsEof())
            return false;

        return Current().Kind switch
        {
            SyntaxKind.RArrow => (closingArrow = Advance()) != null,
            SyntaxKind.RArrowRArrow => SplitAndAdvance(1, SyntaxKind.RArrow, out closingArrow),
            SyntaxKind.RArrowRArrowRArrow => SplitAndAdvance(1, SyntaxKind.RArrowRArrow, out closingArrow),
            _ => false
        };
    }

    // evil token splitting function
    private bool SplitAndAdvance(int splitIndex, SyntaxKind remainderKind, out Token closingArrow)
    {
        var token = Current();
        closingArrow = new Token(SyntaxKind.RArrow, token.File, new TextSpan(token.Span.Position, splitIndex), token.Text[..splitIndex]);

        var remainder = new Token(
            remainderKind,
            token.File,
            new TextSpan(token.Span.Position + splitIndex, token.Span.Length - splitIndex),
            token.Text[splitIndex..]
        );

        lexerResult.Tokens[_position] = closingArrow;
        lexerResult.Tokens.Insert(_position + 1, remainder);
        Advance();
        return true;
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
        {
            _diagnostics.Error(
                current,
                InternalCodes.UnexpectedEof,
                message != null ? message(null) : $"Expected '{expected}', got EOF."
            );
        }
        else
        {
            _diagnostics.Error(
                current,
                InternalCodes.UnexpectedToken,
                message != null ? message(current) : $"Expected '{expected}', got {SafeTokenText(current)}."
            );
        }
        
        return MissingToken(kind);
    }

    private Token Advance()
    {
        var current = Current();
        _position++;
        return current;
    }

    /// <summary>
    /// Skip tokens until a statement boundary so later syntax can still be parsed.
    /// Consumes ';' ; leaves '}' and statement keywords for the caller.
    /// </summary>
    private void Synchronize()
    {
        while (!IsEof())
        {
            switch (Current().Kind)
            {
                case SyntaxKind.Semicolon:
                    Advance();
                    return;
                case SyntaxKind.RBrace:
                    return;
                default:
                    if (AtStatementKeyword())
                        return;

                    Advance();
                    break;
            }
        }
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

        for (var i = startOffset + 1; ; i++)
        {
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