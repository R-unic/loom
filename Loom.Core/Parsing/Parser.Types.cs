using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Text;

namespace Loom.Core.Parsing;

public sealed partial class Parser
{
    private TypeExpression ParseType() => ParseUnionType();

    private TypeExpression ParseUnionType() => ParseChainedType(ParseIntersectionType, SyntaxKind.Pipe, (separators, types) => new UnionType(separators, types));

    private TypeExpression ParseIntersectionType() =>
        ParseChainedType(ParsePostfixType, SyntaxKind.Ampersand, (separators, types) => new IntersectionType(separators, types));

    private TypeExpression ParsePostfixType()
    {
        var type = ParseUnaryType();
        while (true)
            if (Match(out var leftBracket, SyntaxKind.LBracket))
            {
                if (Match(out var immediateRightBracket, SyntaxKind.RBracket))
                {
                    type = new ArrayType(type, leftBracket, null, immediateRightBracket);
                    continue;
                }

                if (Match(out var mutKeyword, SyntaxKind.MutKeyword))
                {
                    var arrayRightBracket = Expect(SyntaxKind.RBracket);
                    type = new ArrayType(type, leftBracket, mutKeyword, arrayRightBracket);
                    continue;
                }

                var indexType = ParseType();
                var rightBracket = Expect(SyntaxKind.RBracket);
                type = new IndexedType(leftBracket, rightBracket, type, indexType);
            }
            else if (Match(out var question, SyntaxKind.Question))
            {
                type = new OptionalType(question, type);
            }
            else
            {
                break;
            }

        return type;
    }

    private TypeExpression ParseUnaryType()
    {
        if (Match(out var keyOfKeyword, SyntaxKind.KeyOfKeyword))
        {
            var leftParen = Expect(SyntaxKind.LParen);
            var innerType = ParsePostfixType();
            var rightParen = Expect(SyntaxKind.RParen);
            return new KeyOf(keyOfKeyword, leftParen, rightParen, innerType);
        }

        if (Match(out var typeOfKeyword, SyntaxKind.TypeOfKeyword))
        {
            var leftParen = Expect(SyntaxKind.LParen);
            var expression = ParseExpression();
            var rightParen = Expect(SyntaxKind.RParen);
            return new TypeOf(typeOfKeyword, leftParen, rightParen, expression);
        }

        return ParsePrimaryType();
    }

    private TypeExpression ParsePrimaryType()
    {
        if (Match(out var fnKeyword, SyntaxKind.FnKeyword))
            return ParseFunctionType(fnKeyword);

        if (Match(out var leftParen, SyntaxKind.LParen))
            return ParseParenthesizedType(leftParen);

        if (Match(out var token, SyntaxFacts.IsLiteral))
            return new LiteralType(token, LiteralUtility.ResolveValue(token));

        var name = ExpectIdentifier("type");
        if (SyntaxFacts.IsPrimitiveType(name.Text))
            return new PrimitiveType(name);

        var typeArguments = ParseTypeArguments();
        return new TypeName(name, typeArguments);
    }

    private TypeExpression ParseFunctionType(Token fnKeyword)
    {
        var typeParameters = ParseTypeParameters();
        var parameters = ParseParameters();
        var returnType = ParseColonTypeClause();
        if (!ValidateFunctionSignature(
                "function types",
                parameters?.LocationSpan ?? typeParameters?.LocationSpan ?? fnKeyword.GetLocation(),
                returnType,
                parameters
            ))
            return new NullTypeExpression(fnKeyword);

        return new FunctionType(fnKeyword, typeParameters, parameters, returnType);
    }

    private ParenthesizedType ParseParenthesizedType(Token leftParen)
    {
        var type = ParseType();
        var rightParen = Expect(
            SyntaxKind.RParen,
            got => $"Expected ')' here to close '{leftParen.Text}' at character {leftParen.GetLocation().Start.Character}, got {SafeTokenText(got)}."
        );

        return new ParenthesizedType(leftParen, rightParen, type);
    }

    private TypeExpression ParseChainedType(
        Func<TypeExpression> parseInner,
        SyntaxKind separator,
        Func<List<Token>, List<TypeExpression>, TypeExpression> create)
    {
        var types = new List<TypeExpression> { parseInner() };
        var separators = new List<Token>();
        while (Match(out var token, separator))
        {
            separators.Add(token);
            types.Add(parseInner());
        }

        return separators.Count > 0 ? create(separators, types) : types[0];
    }

    private TypeParameters? ParseTypeParameters()
    {
        if (!Match(out var leftArrow, SyntaxKind.LArrow))
            return null;

        var parameters = ParseDelimited(ParseTypeParameter);
        var rightArrow = Expect(SyntaxKind.RArrow);
        return new TypeParameters(leftArrow, rightArrow, parameters);
    }

    private TypeParameter ParseTypeParameter()
    {
        var name = ExpectIdentifier("type parameter name");
        var constraint = ParseColonTypeClause();
        var equalsTypeClause = ParseEqualsTypeClause();
        return new TypeParameter(name, constraint, equalsTypeClause);
    }

    private TypeArguments? ParseTypeArguments(bool forInvocation = false)
    {
        if (!Match(out var leftArrow, forInvocation ? SyntaxKind.ColonColonLArrow : SyntaxKind.LArrow))
            return null;

        var arguments = ParseDelimited(ParseType);
        if (MatchClosingArrow(out var rightArrow))
            return new TypeArguments(leftArrow, rightArrow, arguments);

        Expect(SyntaxKind.RArrow);
        return null;
    }

    private TypeArguments<T>? ParseTypeArguments<T>(bool forInvocation = false, string? error = null)
        where T : TypeExpression
    {
        if (!Match(out var leftArrow, forInvocation ? SyntaxKind.ColonColonLArrow : SyntaxKind.LArrow))
            return null;

        var arguments = ParseDelimited(ParseType);
        if (error != null && arguments.Find(a => a is not T) is { } invalidArgument)
        {
            _diagnostics.Error(invalidArgument, InternalCodes.InvalidTypeArguments, error);
            return null;
        }

        if (MatchClosingArrow(out var rightArrow))
            return new TypeArguments<T>(leftArrow, rightArrow, arguments.OfType<T>().ToList());

        Expect(SyntaxKind.RArrow);
        return null;
    }
}