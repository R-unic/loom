using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Text;

namespace Loom.Core.Parsing;

public sealed partial class Parser
{
    private MatchExpression ParseMatchExpression(Token keyword)
    {
        var expression = ParseExpression();
        var leftBrace = Expect(SyntaxKind.LBrace);
        var arms = new List<MatchArm>();
        while (!IsEof() && Current() is not { Kind: SyntaxKind.RBrace })
        {
            var positionBefore = _position;
            arms.Add(ParseMatchArm());
            Match(SyntaxKind.Comma, SyntaxKind.Semicolon);
            if (_position == positionBefore)
                Advance();
        }

        var rightBrace = Expect(SyntaxKind.RBrace);
        return new MatchExpression(keyword, expression, leftBrace, rightBrace, arms);
    }

    private MatchArm ParseMatchArm()
    {
        var pattern = ParsePattern();
        Expression? guard = null;
        if (Match(out var when, SyntaxKind.WhenKeyword))
            guard = ParseExpression();

        var arrow = Expect(SyntaxKind.Arrow);
        var body = ParseExpression();
        return new MatchArm(pattern, when, guard, arrow, body);
    }

    private Pattern ParsePattern()
    {
        var pattern = ParsePrimaryPattern();
        if (!Match(out var firstPipe, SyntaxKind.Pipe))
            return pattern;

        var patterns = new List<Pattern> { pattern };
        var pipes = new List<Token> { firstPipe };
        while (true)
        {
            patterns.Add(ParsePrimaryPattern());
            if (IsEof() || !Match(out var pipe, SyntaxKind.Pipe))
                break;

            pipes.Add(pipe);
        }

        return new OrPattern(pipes, patterns);
    }

    private Pattern ParsePrimaryPattern()
    {
        if (Match(out var leftBracket, SyntaxKind.LBracket))
            return ParseArrayPattern(leftBracket);

        if (Match(out var leftBrace, SyntaxKind.LBrace))
            return ParseObjectPattern(leftBrace);

        if (Match(out var letKeyword, SyntaxKind.LetKeyword))
        {
            var letName = ExpectIdentifier("binding name");
            return new LetPattern(letKeyword, letName);
        }

        if (Match(out var identifier, SyntaxKind.Identifier))
        {
            if (identifier.Text != "_" && Current() is { Kind: SyntaxKind.WhenKeyword })
            {
                var whenPosition = _position;
                Match(out var when, SyntaxKind.WhenKeyword);
                var type = ParseType();
                if (AtTypedPatternFollower())
                {
                    ObjectPattern? objectPattern = null;
                    if (Match(out var typedLeftBrace, SyntaxKind.LBrace))
                        objectPattern = ParseObjectPattern(typedLeftBrace);

                    return new TypedPattern(identifier, when!, type, objectPattern);
                }

                _position = whenPosition;
            }

            var typeArguments = ParseTypeArguments();
            if (typeArguments != null || Current() is { Kind: SyntaxKind.LBrace })
            {
                TypeExpression type = SyntaxFacts.IsPrimitiveType(identifier.Text) && typeArguments == null
                    ? new PrimitiveType(identifier)
                    : new TypeName(identifier, typeArguments);

                ObjectPattern? objectPattern = null;
                if (Match(out var typeLeftBrace, SyntaxKind.LBrace))
                    objectPattern = ParseObjectPattern(typeLeftBrace);

                return new TypePattern(type, objectPattern);
            }

            return identifier.Text == "_"
                ? new WildcardPattern(identifier)
                : new IdentifierPattern(identifier);
        }

        if (Match(out var literal, SyntaxFacts.IsLiteral))
        {
            var minimum = new LiteralPattern(literal, LiteralUtility.ResolveValue(literal));
            if (!Match(out var dotDot, SyntaxKind.DotDot))
                return minimum;

            if (Match(out var maximumLiteral, SyntaxFacts.IsLiteral))
                return new RangePattern(minimum, dotDot, new LiteralPattern(maximumLiteral, LiteralUtility.ResolveValue(maximumLiteral)));

            var badToken = Current();
            if (IsEof())
            {
                _diagnostics.Error(badToken, InternalCodes.UnexpectedEof, "Unexpected end of file.");
            }
            else
            {
                _diagnostics.Error(badToken, InternalCodes.UnexpectedToken, $"Expected range end, got {SafeTokenText(badToken)}.");
                _position++;
            }

            return new RangePattern(minimum, dotDot, new NullPattern(badToken));
        }

        var current = Current();
        if (IsEof())
        {
            _diagnostics.Error(current, InternalCodes.UnexpectedEof, "Unexpected end of file.");
        }
        else
        {
            _diagnostics.Error(current, InternalCodes.UnexpectedToken, $"Expected pattern, got {SafeTokenText(current)}.");
            _position++;
        }

        return new NullPattern(current);
    }

    private bool AtTypedPatternFollower() =>
        Current().Kind is SyntaxKind.Arrow
                       or SyntaxKind.Comma
                       or SyntaxKind.Semicolon
                       or SyntaxKind.RBrace
                       or SyntaxKind.RBracket
                       or SyntaxKind.Pipe
                       or SyntaxKind.LBrace
                       or SyntaxKind.WhenKeyword
                       or SyntaxKind.Eof;

    private ArrayPattern ParseArrayPattern(Token leftBracket)
    {
        var elements = new List<Pattern>();
        RestPattern? rest = null;
        if (Match(out var rightBracket, SyntaxKind.RBracket))
            return new ArrayPattern(leftBracket, rightBracket, elements, rest);

        while (!IsEof() && Current() is not { Kind: SyntaxKind.RBracket })
        {
            if (Match(out var dotDot, SyntaxKind.DotDot))
            {
                if (rest != null)
                {
                    _diagnostics.Error(dotDot, InternalCodes.UnexpectedToken, "Rest pattern must appear at most once in an array pattern.");
                    break;
                }

                rest = new RestPattern(dotDot, ParsePrimaryPattern());
                Match(SyntaxKind.Comma, SyntaxKind.Semicolon);
                if (!IsEof() && Current() is not { Kind: SyntaxKind.RBracket })
                    _diagnostics.Error(Current(), InternalCodes.UnexpectedToken, "Rest pattern must be the last element in an array pattern.");

                break;
            }

            elements.Add(ParsePattern());
            if (!Match(out _, SyntaxKind.Comma, SyntaxKind.Semicolon))
                break;
        }

        rightBracket = Expect(SyntaxKind.RBracket);
        return new ArrayPattern(leftBracket, rightBracket, elements, rest);
    }

    private ObjectPattern ParseObjectPattern(Token leftBrace)
    {
        var fields = new List<ObjectPatternField>();
        if (Match(out var rightBrace, SyntaxKind.RBrace))
            return new ObjectPattern(leftBrace, rightBrace, fields);

        while (!IsEof() && Current() is not { Kind: SyntaxKind.RBrace })
        {
            var positionBefore = _position;
            fields.Add(ParseObjectPatternField());
            Match(SyntaxKind.Comma, SyntaxKind.Semicolon);

            // ExpectIdentifier does not consume a bad token; advance so we cannot spin forever.
            if (_position == positionBefore)
                Advance();
        }

        rightBrace = Expect(SyntaxKind.RBrace);
        return new ObjectPattern(leftBrace, rightBrace, fields);
    }

    private ObjectPatternField ParseObjectPatternField()
    {
        var name = ExpectIdentifier("property name");
        return !Match(out var colon, SyntaxKind.Colon)
            ? new ObjectPatternField(name, null, new IdentifierPattern(name))
            : new ObjectPatternField(name, colon, ParsePattern());
    }
}
