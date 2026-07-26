using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Text;

namespace Loom.Core.Parsing;

public sealed partial class Parser
{
    private Expression ParseExpression() => ParseBinaryLevel(0);

    private Expression ParseBinaryLevel(int level)
    {
        if (level >= BinaryPrecedenceLevel.Levels.Length)
            return ParseRange();

        var (rightAssociative, matches) = BinaryPrecedenceLevel.Levels[level];
        var left = ParseBinaryLevel(level + 1);
        while (Match(out var op, matches))
        {
            switch (op.Kind)
            {
                case SyntaxKind.AsKeyword:
                {
                    var type = ParseType();
                    left = new As(op, left, type);
                    continue;
                }
                case SyntaxKind.Question:
                {
                    var thenBranch = ParseBinaryLevel(level);
                    var colon = Expect(SyntaxKind.Colon);
                    var elseBranch = ParseBinaryLevel(level);
                    left = new TernaryOperator(op, colon, left, thenBranch, elseBranch);
                    continue;
                }
            }

            var right = ParseBinaryLevel(rightAssociative ? level : level + 1);
            var isAssignment = SyntaxFacts.IsAssignmentOperator(op.Kind);
            if (isAssignment && left is not AssignmentTarget)
            {
                _diagnostics.Error(left, InternalCodes.InvalidAssignmentTarget, "Invalid assignment target.", $"did you mean '{left} == {right}'?");
                return left;
            }

            left = isAssignment && left is AssignmentTarget target
                ? new AssignmentOperator(op, target, right)
                : new BinaryOperator(op, left, right);
        }

        return left;
    }

    private Expression ParseRange()
    {
        var expression = ParseUnary();
        if (!Match(out var dotDot, SyntaxKind.DotDot))
            return expression;

        var maximum = ParseUnary();
        return new RangeLiteral(dotDot, expression, maximum);
    }

    private InterfaceInvocation ParseInterfaceInvocation(Token keyword)
    {
        var name = new Identifier(ExpectIdentifier());
        var typeArguments = ParseTypeArguments(true);
        var leftBrace = Expect(SyntaxKind.LBrace);
        var initializers = new List<InterfaceInvocationInitializer>();
        if (!Match(out var rightBrace, SyntaxKind.RBrace))
        {
            initializers.AddRange(ParseDelimited(ParseInterfaceInvocationInitializer));
            rightBrace = Expect(SyntaxKind.RBrace);
        }

        var body = new InterfaceInvocationBody(leftBrace, rightBrace, initializers);
        return new InterfaceInvocation(keyword, name, typeArguments, body);
    }

    private Expression ParseUnary() =>
        Match(out var op, SyntaxFacts.IsUnaryOperator)
            ? new UnaryOperator(op, ParseUnary())
            : ParsePostfix();

    private Expression ParsePostfix()
    {
        var expression = ParsePrimary();
        while (!IsEof())
            if (AtInvocationStart())
                expression = ParseInvocation(expression);
            else if (Match(out var leftBracket, SyntaxKind.LBracket))
                expression = ParseElementAccess(leftBracket, expression);
            else if (Match(out var dot, SyntaxKind.Dot))
                expression = ParseNamedAccess(dot, expression);
            else
                break;

        return expression;
    }

    private bool AtInvocationStart() => Current() is { Kind: SyntaxKind.LParen or SyntaxKind.ColonColonLArrow };

    private ElementAccess ParseElementAccess(Token leftBracket, Expression expression)
    {
        var indexExpression = ParseExpression();
        var rightBracket = Expect(SyntaxKind.RBracket);
        return new ElementAccess(leftBracket, rightBracket, expression, indexExpression);
    }

    private AssignmentTarget ParseNamedAccess(Token dot, Expression expression)
    {
        var name = ExpectIdentifier();
        var names = new List<DotName> { new(dot, name) };
        while (Match(out var nextDot, SyntaxKind.Dot))
            names.Add(new DotName(nextDot, ExpectIdentifier()));

        return expression is Identifier identifier
            ? new QualifiedName(identifier, names)
            : new PropertyAccess(expression, names);
    }

    private Invocation ParseInvocation(Expression callee)
    {
        var typeArguments = ParseTypeArguments(true);
        var leftParen = Expect(SyntaxKind.LParen);
        var arguments = ParseArguments(leftParen);
        return new Invocation(callee, typeArguments, arguments);
    }

    private Expression ParsePrimary()
    {
        if (Match(out var matchKeyword, SyntaxKind.MatchKeyword))
            return ParseMatchExpression(matchKeyword);

        if (Match(out var newKeyword, SyntaxKind.NewKeyword))
            return ParseInterfaceInvocation(newKeyword);

        if (Match(out var leftParen, SyntaxKind.LParen))
            return ParseParenthesized(leftParen);

        if (Match(out var mutKeyword, SyntaxKind.MutKeyword))
        {
            if (ParseArrayLiteral(mutKeyword) is { } mutableArrayLiteral)
                return mutableArrayLiteral;

            _diagnostics.Error(mutKeyword, InternalCodes.UnexpectedToken, "Expected array literal after 'mut'.");
        }

        if (ParseArrayLiteral() is { } arrayLiteral)
            return arrayLiteral;

        if (Match(out var interpolatedStart, SyntaxKind.InterpolatedStringStart))
            return ParseInterpolatedStringLiteral(interpolatedStart);

        if (Match(out var nameOfKeyword, SyntaxKind.NameOfKeyword))
            return ParseNameOf(nameOfKeyword);

        if (Match(out var nameToken, SyntaxKind.Identifier))
            return new Identifier(nameToken);

        if (Match(out var token, SyntaxFacts.IsLiteral))
            return new Literal(token, LiteralUtility.ResolveValue(token));

        var current = Current();
        if (IsEof())
        {
            _diagnostics.Error(current, InternalCodes.UnexpectedEof, "Unexpected end of file.");
        }
        else
        {
            _diagnostics.Error(current, InternalCodes.UnexpectedToken, $"Expected expression, got {SafeTokenText(Current())}.");
            _position++;
        }

        return new NullExpression(current);
    }

    private InterfaceInvocationInitializer ParseInterfaceInvocationInitializer()
    {
        if (Match(out var name, SyntaxKind.Identifier))
        {
            if (!Match(out var colon, SyntaxKind.Colon))
                return new ShorthandPropertyInitializer(new Identifier(name));

            var expression = ParseExpression();
            return new PropertyInitializer(name, colon, expression);
        }

        var leftBracket = Expect(SyntaxKind.LBracket, "property name or index initializer");
        var indexExpression = ParseExpression();
        var rightBracket = Expect(SyntaxKind.RBracket);
        var indexColon = Expect(SyntaxKind.Colon);
        var indexValueExpression = ParseExpression();
        return new IndexInitializer(leftBracket, rightBracket, indexColon, indexExpression, indexValueExpression);
    }

    private Arguments ParseArguments(Token leftParen)
    {
        if (Match(out var matchedRightParen, SyntaxKind.RParen))
            return new Arguments(leftParen, matchedRightParen, []);

        var argumentList = ParseDelimited(ParseExpression);
        var rightParen = Expect(SyntaxKind.RParen);
        return new Arguments(leftParen, rightParen, argumentList);
    }

    private Parenthesized ParseParenthesized(Token leftParen)
    {
        var expression = ParseExpression();
        var rightParen = Expect(
            SyntaxKind.RParen,
            got => $"Expected ')' here to close '{leftParen.Text}' at character {leftParen.GetLocation().Start.Character}, got {SafeTokenText(got)}."
        );

        return new Parenthesized(leftParen, rightParen, expression);
    }

    private Expression ParseNameOf(Token keyword)
    {
        var typeArguments = ParseTypeArguments<TypeName>(true, "May only get name of type when the type is a type name.");
        var leftParen = Expect(SyntaxKind.LParen);
        var expression = typeArguments == null ? ParseExpression() : null;
        var rightParen = Expect(SyntaxKind.RParen);
        if (expression is Name name)
            return new NameOf(keyword, null, leftParen, rightParen, name);

        if (typeArguments != null)
        {
            if (typeArguments.ArgumentsList.Count == 1)
                return new NameOf(keyword, typeArguments, leftParen, rightParen, null);

            _diagnostics.Error(typeArguments, InternalCodes.GenericArity, "Exactly one type parameter is allowed for 'nameof::<T>()'.");
            return new NullExpression(keyword);
        }

        _diagnostics.Error(
            typeArguments?.LocationSpan ?? expression!.LocationSpan,
            InternalCodes.InvalidNameOf,
            $"'{typeArguments?.ArgumentsList.FirstOrDefault()?.ToString() ?? expression!.ToString()}' is not a valid name."
        );

        return new NullExpression(keyword);
    }

    private ArrayLiteral? ParseArrayLiteral(Token? mutKeyword = null)
    {
        if (!Match(out var leftBracket, SyntaxKind.LBracket))
            return null;

        if (Match(out var immediateRightBracket, SyntaxKind.RBracket))
            return new ArrayLiteral(mutKeyword, leftBracket, immediateRightBracket, []);

        var expressions = ParseDelimited(ParseExpression);
        var rightBracket = Expect(SyntaxKind.RBracket);
        return new ArrayLiteral(mutKeyword, leftBracket, rightBracket, expressions);
    }

    private InterpolatedStringLiteral ParseInterpolatedStringLiteral(Token startToken)
    {
        var parts = new List<InterpolationPart>();
        while (true)
        {
            if (Match(out var textToken, SyntaxKind.InterpolatedStringText))
                parts.Add(new InterpolationTextPart(textToken));

            if (Match(out var endToken, SyntaxKind.InterpolatedStringEnd))
                return new InterpolatedStringLiteral(startToken, parts, endToken);

            var leftBrace = Expect(SyntaxKind.LBrace);
            var expression = ParseExpression();
            var rightBrace = Expect(SyntaxKind.RBrace);
            parts.Add(new InterpolationHolePart(leftBrace, expression, rightBrace));
        }
    }

}