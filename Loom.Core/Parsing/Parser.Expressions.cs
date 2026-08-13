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
                case SyntaxKind.IsKeyword:
                {
                    var pattern = ParseIsPattern();
                    left = new Is(left, op, pattern);
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

    private Expression ParseUnary()
    {
        if (Match(out var awaitKeyword, SyntaxKind.AwaitKeyword))
            return BuildAwait(awaitKeyword, ParseUnary());

        return Match(out var op, SyntaxFacts.IsUnaryOperator)
            ? new UnaryOperator(op, ParseUnary())
            : ParsePostfix();
    }

    // 'await' takes the whole postfix chain, as it does in JS and C#, so '(await x).y' still needs its
    // parentheses. A trailing '?' is the one exception: 'await x?' is rebuilt as '(await x)?', because a
    // Future is never a Result and so the other reading could only ever be an error - and a Roblox API
    // member that yields usually raises too, which would leave the parenthesised form as the common one.
    private static Expression BuildAwait(Token keyword, Expression operand) =>
        operand is ErrorPropagation propagation
            ? new ErrorPropagation(BuildAwait(keyword, propagation.Expression), propagation.Question)
            : new Await(keyword, operand);

    private Expression ParsePostfix()
    {
        var expression = ParsePrimary();
        while (!IsEof())
            if (AtInvocationStart())
                expression = ParseInvocation(expression);
            else if (Match(out var leftBracket, SyntaxKind.LBracket))
                expression = ParseElementAccess(null, leftBracket, expression);
            else if (AtOptionalElementAccessStart())
                expression = ParseElementAccess(Advance(), Expect(SyntaxKind.LBracket), expression);
            else if (Match(out var dot, SyntaxKind.Dot, SyntaxKind.QuestionDot))
                expression = ParseNamedAccess(dot, expression);
            else if (Current().Kind == SyntaxKind.Bang && IsOnSameLine(expression.LastToken()!, Current()))
                expression = new NullForgiving(expression, Advance());
            else if (AtErrorPropagationStart())
                expression = new ErrorPropagation(expression, Advance());
            else
                break;

        return expression;
    }

    private bool AtInvocationStart() => Current() is { Kind: SyntaxKind.LParen or SyntaxKind.ColonColonLArrow };

    // '?' and '[' stay separate tokens (unlike '?.') because 'T?[]' already means "array of optional T"
    // in type position, so a dedicated '?[' token would collide with that. Disambiguating 'a?[b]'
    // (optional indexing) from a ternary whose then-branch is an array literal, e.g. 'a ? [b] : c', means
    // looking past the closing ']' for a ':' - if one follows, this is the ternary's '?' and '[b]' is its
    // then-branch; otherwise it's an optional element access.
    private bool AtOptionalElementAccessStart() =>
        Current().Kind == SyntaxKind.Question
        && OffsetAfterBrackets(1) is { } closeOffset
        && PeekKind(closeOffset + 1) != SyntaxKind.Colon;

    // A bare postfix '?' means error-propagation (ErrorPropagation) unless it's actually the start of a
    // ternary sharing the same token. This is a pure lookahead - it never Advance()s, so an abandoned
    // attempt can't leak diagnostics (DiagnosticBag has no rollback) - that scans forward for a
    // same-statement ':' at bracket depth 0, requiring at least one token before it: a ternary can't have
    // a zero-length then-branch, so a ':' immediately after '?' can only belong to an *enclosing* ternary,
    // not one starting here (this is what makes 'cond ? unwrap()? : fallback' parse correctly - the inner
    // '?' must not mistake the outer ':' for its own).
    //
    // Statements don't require a trailing ';' in this grammar (ParseStatement only *optionally* consumes
    // one), so the scan can't just look for an explicit terminator - 'foo()?\nbar()' has nothing between
    // the two statements at all. Instead it tracks whether it's in "expecting an operand" or "expecting an
    // operator/terminator" position, mirroring how ParseBinaryLevel/ParsePostfix actually consume tokens:
    // two operand-shaped tokens with nothing connecting them (e.g. the 'bar' that starts a new statement,
    // sitting right where an operator was expected) can only mean the implied then-branch already ended,
    // which is what makes this safe even though it deliberately does not track real statement boundaries.
    //
    // A bare '{' never starts an expression anywhere in this grammar except as 'new Foo { ... }' or
    // 'match x { ... }', both always immediately preceded by 'new'/'match' - so an unguarded '{' ends the
    // scan (e.g. the if-body in 'if foo()? { ... }', since if/while/for conditions aren't parenthesized),
    // while a guarded one is tracked as an openable region like '(' or '['. A '::<...>' generic argument
    // list gets its own depth counter so a comma inside it (foo::<T, U>()) isn't mistaken for one ending
    // an argument list the '?' sits in.
    //
    // Known limitation: a 'match'/'new' used directly as another match's subject with no separating
    // brackets confuses this single pending-brace flag. Contrived enough to leave unhandled - wrap the
    // inner one in parens if it ever comes up.
    private bool AtErrorPropagationStart()
    {
        if (Current().Kind != SyntaxKind.Question || PeekKind(1) == SyntaxKind.LBracket)
            return false;

        var parenDepth = 0;
        var bracketDepth = 0;
        var braceDepth = 0;
        var angleDepth = 0;
        var expectingBrace = false;
        var expectingOperand = true;

        for (var i = 1;; i++)
        {
            var atZero = parenDepth == 0 && bracketDepth == 0 && braceDepth == 0 && angleDepth == 0;
            var kind = PeekKind(i);

            switch (kind)
            {
                case SyntaxKind.Eof:
                    return true;

                case SyntaxKind.LParen:
                    parenDepth++;
                    continue;
                case SyntaxKind.RParen:
                    if (--parenDepth < 0) return true;
                    expectingOperand = false;
                    continue;

                case SyntaxKind.LBracket:
                    bracketDepth++;
                    continue;
                case SyntaxKind.RBracket:
                    if (--bracketDepth < 0) return true;
                    expectingOperand = false;
                    continue;

                case SyntaxKind.LBrace when atZero:
                    if (!expectingBrace) return true;
                    expectingBrace = false;
                    braceDepth++;
                    continue;
                case SyntaxKind.LBrace:
                    braceDepth++;
                    continue;
                case SyntaxKind.RBrace:
                    if (--braceDepth < 0) return true;
                    expectingOperand = false;
                    continue;

                case SyntaxKind.ColonColonLArrow when atZero:
                    angleDepth++;
                    continue;
                case SyntaxKind.LArrow when angleDepth > 0:
                    angleDepth++;
                    continue;
                case SyntaxKind.RArrow when angleDepth > 0:
                    angleDepth--;
                    expectingOperand = false;
                    continue;
                case SyntaxKind.RArrowRArrow when angleDepth > 0:
                    angleDepth = Math.Max(0, angleDepth - 2);
                    expectingOperand = false;
                    continue;
                case SyntaxKind.RArrowRArrowRArrow when angleDepth > 0:
                    angleDepth = Math.Max(0, angleDepth - 3);
                    expectingOperand = false;
                    continue;
            }

            if (!atZero || expectingBrace)
                continue;

            if (expectingOperand)
            {
                switch (kind)
                {
                    case SyntaxKind.Colon:
                        return true;
                    case SyntaxKind.NewKeyword or SyntaxKind.MatchKeyword:
                        expectingBrace = true;
                        continue;
                    case SyntaxKind.Minus or SyntaxKind.Tilde or SyntaxKind.Bang or SyntaxKind.MutKeyword:
                        continue;
                    case SyntaxKind.Identifier or SyntaxKind.NameOfKeyword or SyntaxKind.At or SyntaxKind.InterpolatedStringStart:
                        expectingOperand = false;
                        continue;
                    default:
                        if (!SyntaxFacts.IsLiteral(kind))
                            return true;

                        expectingOperand = false;
                        continue;
                }
            }

            switch (kind)
            {
                case SyntaxKind.Colon:
                    return false;
                case SyntaxKind.Dot or SyntaxKind.QuestionDot:
                    // A member name (an identifier) must follow, unlike the postfix operators below.
                    expectingOperand = true;
                    continue;
                case SyntaxKind.Bang or SyntaxKind.Question:
                    continue;
                default:
                    if (!IsErrorPropagationBinaryOperator(kind))
                        return true;

                    expectingOperand = true;
                    continue;
            }
        }
    }

    // 'Question' itself is excluded even though BinaryPrecedenceLevel registers it for the ternary: it's
    // handled as a postfix continuation above instead (another '?' right after a completed operand keeps
    // "expecting operand" false, it doesn't reset to "needs a fresh operand" the way a real binary
    // operator would) - this also means a second statement that itself starts with 'x ? y : z' gets its
    // own '?' misread as a continuation rather than recognized as a new ternary, but that only delays the
    // eventual operand-after-operand collision (at 'y') by one token, so the verdict for the original '?'
    // still comes out right.
    private static bool IsErrorPropagationBinaryOperator(SyntaxKind kind) =>
        kind != SyntaxKind.Question && (BinaryPrecedenceLevel.Levels.Any(level => level.Matches(kind)) || kind == SyntaxKind.DotDot);

    private static bool IsOnSameLine(Token previous, Token next) => previous.GetLocation().End.Line == next.GetLocation().Start.Line;

    private ElementAccess ParseElementAccess(Token? questionMark, Token leftBracket, Expression expression)
    {
        var indexExpression = ParseExpression();
        var rightBracket = Expect(SyntaxKind.RBracket);
        return new ElementAccess(questionMark, leftBracket, rightBracket, expression, indexExpression);
    }

    private AssignmentTarget ParseNamedAccess(Token dot, Expression expression)
    {
        var name = ExpectIdentifier();
        var names = new List<DotName> { new(dot, name) };
        while (Match(out var nextDot, SyntaxKind.Dot, SyntaxKind.QuestionDot))
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
        if (Match(out var fnKeyword, SyntaxKind.FnKeyword))
            return ParseFunctionExpression(fnKeyword);

        if (Match(out var asyncKeyword, SyntaxKind.AsyncKeyword))
            return ParseFunctionExpression(Expect(SyntaxKind.FnKeyword), asyncKeyword);

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

        if (Match(out var atToken, SyntaxKind.At))
            return new SelfExpression(atToken);

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

        var argumentList = ParseDelimited(ParseSpreadable);
        var rightParen = Expect(SyntaxKind.RParen);
        return new Arguments(leftParen, rightParen, argumentList);
    }

    private Expression ParseParenthesized(Token leftParen)
    {
        var expression = ParseExpression();
        if (Current().Kind == SyntaxKind.Comma)
        {
            var expressions = new List<Expression> { expression };
            var commas = new List<Token>();
            while (Match(out var comma, SyntaxKind.Comma))
            {
                commas.Add(comma);
                expressions.Add(ParseExpression());
            }

            var tupleRightParen = Expect(
                SyntaxKind.RParen,
                got => $"Expected ')' here to close '{leftParen.Text}' at character {leftParen.GetLocation().Start.Character}, got {SafeTokenText(got)}."
            );

            return new TupleExpression(leftParen, tupleRightParen, commas, expressions);
        }

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

        var expressions = ParseDelimited(ParseSpreadable);
        var rightBracket = Expect(SyntaxKind.RBracket);
        return new ArrayLiteral(mutKeyword, leftBracket, rightBracket, expressions);
    }

    private Expression ParseSpreadable() => Match(out var dotDot, SyntaxKind.DotDot) ? new SpreadElement(dotDot, ParseExpression()) : ParseExpression();

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