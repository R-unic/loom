using System.Diagnostics.CodeAnalysis;
using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Text;

namespace Loom.Core.Parsing;

public sealed partial class Parser
{
    private TypeExpression ParseType() =>
        Current().Kind is SyntaxKind.Identifier or SyntaxKind.At && PeekKind(1) == SyntaxKind.IsKeyword
            ? ParseTypePredicateType()
            : ParseUnionType();

    private TypeExpression ParseTypePredicateType()
    {
        Expression subject;
        if (Match(out var atToken, SyntaxKind.At))
            subject = new SelfExpression(atToken);
        else
            subject = new Identifier(Expect(SyntaxKind.Identifier));

        var isKeyword = Expect(SyntaxKind.IsKeyword);
        var type = ParseType();
        return new TypePredicateType(subject, isKeyword, type);
    }

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

        if (Match(out var asyncKeyword, SyntaxKind.AsyncKeyword))
            return ParseFunctionType(Expect(SyntaxKind.FnKeyword), asyncKeyword);

        if (Match(out var leftParen, SyntaxKind.LParen))
            return ParseParenthesizedType(leftParen);

        if (Match(out var token, SyntaxFacts.IsLiteral))
            return new LiteralType(token, LiteralUtility.ResolveValue(token));

        var name = ExpectIdentifier("type");
        if (SyntaxFacts.IsPrimitiveType(name.Text))
            // Only 'string' ever takes one ('string<u8>', its length-prefix width) - trying this for
            // every primitive would make 'a as number < b' commit to parsing '<' as a generic argument
            // list instead of a comparison the moment it sees a type name, with no way back out.
            return new PrimitiveType(name, name.Text == "string" ? ParseTypeArguments() : null);

        var typeArguments = ParseTypeArguments();
        var typeName = new TypeName(name, typeArguments);

        return PeekKind(0) == SyntaxKind.Dot ? SkipQualifiedTypeName(typeName) : typeName;
    }
    
    private TypeExpression SkipQualifiedTypeName(TypeName typeName)
    {
        while (Match(out _, SyntaxKind.Dot))
        {
            ExpectIdentifier("type");
            ParseTypeArguments();
        }

        _diagnostics.NotImplemented(typeName, "Qualified type names are not supported yet.", "import the type by name with 'import type { ... }'");
        return new PrimitiveType(new Token(SyntaxKind.Identifier, typeName.LocationSpan, "unknown"));
    }

    private TypeExpression ParseFunctionType(Token fnKeyword, Token? asyncKeyword = null)
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

        return new FunctionType(fnKeyword, typeParameters, parameters, returnType, asyncKeyword);
    }

    private TypeExpression ParseParenthesizedType(Token leftParen)
    {
        var type = ParseType();
        if (Current().Kind == SyntaxKind.Comma)
        {
            var types = new List<TypeExpression> { type };
            var commas = new List<Token>();
            while (Match(out var comma, SyntaxKind.Comma))
            {
                commas.Add(comma);
                types.Add(ParseType());
            }

            var tupleRightParen = Expect(
                SyntaxKind.RParen,
                got => $"Expected ')' here to close '{leftParen.Text}' at character {leftParen.GetLocation().Start.Character}, got {SafeTokenText(got)}."
            );

            return new TupleType(leftParen, tupleRightParen, commas, types);
        }

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

    // evil token splitting function - RArrowRArrow ('>>') and RArrowRArrowRArrow ('>>>') lex as single
    // tokens, but closing nested generic argument lists (e.g. 'Foo<Bar<T>>') needs them read one '>' at a
    // time, so this splits the current token in the underlying token list and re-points the cursor at it.
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
}