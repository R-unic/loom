using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Text;
using Attribute = Loom.Core.Parsing.AST.Attribute;

namespace Loom.Core.Parsing;

public sealed partial class Parser
{
    private TraitDeclaration ParseTraitDeclaration(Token keyword)
    {
        var name = ExpectIdentifier("trait name");
        var typeParameters = ParseTypeParameters();
        var body = ParseTraitBody();

        return new TraitDeclaration(keyword, name, typeParameters, body);
    }

    private TraitBody ParseTraitBody()
    {
        var leftBrace = Expect(SyntaxKind.LBrace);
        var members = ParseTraitMembers();
        var rightBrace = Expect(SyntaxKind.RBrace);
        return new TraitBody(leftBrace, rightBrace, members);
    }

    private List<DeclareFunctionSignature> ParseTraitMembers()
    {
        var members = new List<Statement>();
        while (!IsEof() && Current() is not { Kind: SyntaxKind.RBrace })
        {
            var attributes = Match(out var leftBracket, SyntaxKind.LBracket) ? ParseAttributes(leftBracket) : null;
            var asyncKeyword = MatchAsyncKeyword();
            if (!Match(out var fnKeyword, SyntaxKind.FnKeyword))
                break;

            members.Add(ParseDeclareFunctionSignature(fnKeyword, attributes, asyncKeyword));
            Match(SyntaxKind.Comma, SyntaxKind.Semicolon);
        }

        return members.OfType<DeclareFunctionSignature>().ToList();
    }

    private InterfaceDeclaration ParseInterfaceDeclaration(Token keyword) => ParseInterfaceDeclaration(keyword, null);

    private InterfaceDeclaration ParseInterfaceDeclaration(Token keyword, Attributes? attributes)
    {
        var isSealed = keyword.Kind == SyntaxKind.SealedKeyword;
        var interfaceKeyword = isSealed ? Expect(SyntaxKind.InterfaceKeyword) : keyword;
        var sealedKeyword = isSealed ? keyword : null;
        var name = ExpectIdentifier("interface name");
        var typeParameters = ParseTypeParameters();
        var colonTypeListClause = ParseColonTypeListClause();
        var body = ParseInterfaceBody();

        return new InterfaceDeclaration(
            sealedKeyword,
            interfaceKeyword,
            name,
            typeParameters,
            colonTypeListClause,
            body,
            attributes
        );
    }

    private InterfaceBody? ParseInterfaceBody()
    {
        if (!Match(out var leftBrace, SyntaxKind.LBrace))
            return null;

        var members = ParseInterfaceMembers();
        var rightBrace = Expect(SyntaxKind.RBrace);
        return new InterfaceBody(leftBrace, rightBrace, members);
    }

    // A member that fails to parse is skipped rather than aborting the whole body, so parsing still
    // reaches the closing '}' instead of leaving it unconsumed and cascading into an unrelated error.
    private List<Statement> ParseInterfaceMembers()
    {
        var members = new List<Statement>();
        while (!IsEof() && Current() is not { Kind: SyntaxKind.RBrace })
        {
            var previousPosition = _position;
            var token = Current();
            Statement? member;
            if (token.Kind != SyntaxKind.LBracket)
            {
                var mutKeyword = Match(out var kw, SyntaxKind.MutKeyword) ? kw : null;
                member = ParseInterfaceMember(mutKeyword);
            }
            else if (LooksLikeIndexer())
            {
                member = ParseInterfaceMember(null);
            }
            else
            {
                Advance();
                var attributes = ParseAttributes(token);
                member = Match(out var eventKeyword, SyntaxKind.EventKeyword)
                    ? ParseEventDeclaration(eventKeyword, attributes)
                    : ParsePropertyDeclaration(Match(out var mutKeyword, SyntaxKind.MutKeyword) ? mutKeyword : null, attributes);
            }

            if (member != null)
                members.Add(member);

            Match(SyntaxKind.Comma, SyntaxKind.Semicolon);
            EnsureProgress(previousPosition);
        }

        return members;
    }

    private Statement? ParseInterfaceMember(Token? mutKeyword)
    {
        if (Match(out var leftBracket, SyntaxKind.LBracket))
            return ParseIndexerDeclaration(mutKeyword, leftBracket);

        if (mutKeyword == null && Match(out var keyword, SyntaxKind.EventKeyword))
            return ParseEventDeclaration(keyword, null);

        return ParsePropertyDeclaration(mutKeyword, null);
    }

    private IndexerDeclaration? ParseIndexerDeclaration(Token? mutKeyword, Token leftBracket)
    {
        var indexType = ParseType();
        var rightBracket = Expect(SyntaxKind.RBracket);
        var colonTypeClause = ExpectInterfaceMemberColonTypeClause($"Expected indexer type, got {SafeTokenText(Current())}.");
        return colonTypeClause == null ? null : new IndexerDeclaration(mutKeyword, leftBracket, rightBracket, indexType, colonTypeClause);
    }

    private PropertyDeclaration? ParsePropertyDeclaration(Token? mutKeyword, Attributes? attributes)
    {
        var name = ExpectIdentifier("property name");
        var propertyType = ExpectInterfaceMemberColonTypeClause($"Expected indexer type, got {SafeTokenText(Current())}.");
        return propertyType == null ? null : new PropertyDeclaration(mutKeyword, name, propertyType, attributes);
    }

    private Attributes ParseAttributes(Token leftBracket)
    {
        var attributesList = ParseDelimited(ParseAttribute);
        var rightBracket = Expect(SyntaxKind.RBracket);
        return new Attributes(leftBracket, rightBracket, attributesList);
    }

    private Attribute ParseAttribute()
    {
        var baseExpression = ParsePostfix();
        return baseExpression is Invocation invocation
            ? new Attribute(invocation.Expression, invocation.TypeArguments, invocation.Arguments)
            : new Attribute(baseExpression, null, null);
    }

    private Statement ParseDeclare(Token declareKeyword) => ParseDeclare(declareKeyword, null);

    private Statement ParseDeclare(Token declareKeyword, Attributes? attributes)
    {
        var statement = ParseDeclareSignature(declareKeyword, attributes);
        if (statement is not DeclareSignature signature)
            return statement;

        return new Declare(declareKeyword, signature);
    }

    private Statement ParseDeclareSignature(Token declareKeyword, Attributes? attributes)
    {
        var asyncKeyword = MatchAsyncKeyword();
        if (Match(out var fnKeyword, SyntaxKind.FnKeyword))
            return ParseDeclareFunctionSignature(fnKeyword, attributes, asyncKeyword);

        if (asyncKeyword != null)
            return ParseDeclareFunctionSignature(Expect(SyntaxKind.FnKeyword), attributes, asyncKeyword);

        if (Match(out var eventKeyword, SyntaxKind.EventKeyword))
            return ParseEventDeclaration(eventKeyword, attributes);

        if (attributes != null)
        {
            _diagnostics.Error(
                attributes,
                InternalCodes.AttributesNotSupportedOnDeclaration,
                "Attributes are only supported on declared function and event signatures."
            );

            return new NullStatement(declareKeyword);
        }

        if (Match(out var variableKeyword, SyntaxKind.LetKeyword, SyntaxKind.MutKeyword))
            return ParseDeclareVariableSignature(variableKeyword);

        if (Match(out var interfaceKeyword, SyntaxKind.InterfaceKeyword, SyntaxKind.SealedKeyword))
            return ParseInterfaceDeclaration(interfaceKeyword);

        _diagnostics.Error(
            Current(),
            InternalCodes.ExpectedDeclarationSignature,
            $"Expected declaration signature, got {SafeTokenText(Current())}."
        );

        return new NullStatement(declareKeyword);
    }

    private DeclareVariableSignature ParseDeclareVariableSignature(Token variableKeyword)
    {
        var name = ExpectIdentifier();
        var colonTypeClause = ParseColonTypeClause();
        return new DeclareVariableSignature(variableKeyword, name, colonTypeClause!);
    }

    private Statement ParseDeclareFunctionSignature(Token fnKeyword, Attributes? attributes = null, Token? asyncKeyword = null)
    {
        var name = ExpectIdentifier("function name");
        var typeParameters = ParseTypeParameters();
        var parameters = ParseParameters();
        var returnType = ParseColonTypeClause();
        if (!ValidateFunctionSignature(
                "declared function signatures",
                parameters?.LocationSpan ?? typeParameters?.LocationSpan ?? name.GetLocation(),
                returnType,
                parameters
            ))
            return new NullStatement(fnKeyword);

        return new DeclareFunctionSignature(fnKeyword, name, typeParameters, parameters, returnType, attributes, asyncKeyword);
    }

    private Statement ParseFunctionDeclaration(Token keyword) => ParseFunctionDeclaration(keyword, null);

    /// <summary>
    ///     Entered with the <c>async</c> already consumed, since the statement dispatch table is keyed on a
    ///     declaration's first token and <c>async</c> is the first token of an asynchronous one.
    /// </summary>
    private Statement ParseAsyncFunctionDeclaration(Token asyncKeyword) => ParseAsyncFunctionDeclaration(asyncKeyword, null);

    private Statement ParseAsyncFunctionDeclaration(Token asyncKeyword, Attributes? attributes) =>
        ParseFunctionDeclaration(Expect(SyntaxKind.FnKeyword), attributes, asyncKeyword);

    private Statement ParseFunctionDeclaration(Token keyword, Attributes? attributes, Token? asyncKeyword = null)
    {
        var name = ExpectIdentifier("function name");
        var typeParameters = ParseTypeParameters();
        var parameters = ParseParameters();
        var returnType = ParseColonTypeClause();
        var body = ParseFunctionBody();

        if (body is not NullStatement nullStatement)
            return new FunctionDeclaration(
                keyword,
                name,
                typeParameters,
                parameters,
                returnType,
                body,
                attributes,
                asyncKeyword
            );

        _diagnostics.Error(
            nullStatement.Token ?? Current(),
            InternalCodes.MissingFunctionBody,
            $"Expected function body, got {SafeTokenText(nullStatement.Token)}."
        );

        return new NullStatement(nullStatement.Token);
    }

    private Expression ParseFunctionExpression(Token keyword, Token? asyncKeyword = null)
    {
        var typeParameters = ParseTypeParameters();
        var parameters = ParseParameters();
        var returnType = ParseColonTypeClause();
        var body = ParseFunctionBody();

        if (body is not NullStatement nullStatement)
            return new FunctionExpression(keyword, typeParameters, parameters, returnType, body, asyncKeyword);

        _diagnostics.Error(
            nullStatement.Token ?? Current(),
            InternalCodes.MissingFunctionBody,
            $"Expected function body, got {SafeTokenText(nullStatement.Token)}."
        );

        return new NullExpression(nullStatement.Token ?? Current());
    }

    private Statement ParseFunctionBody()
    {
        if (Match(out var leftBrace, SyntaxKind.LBrace))
            return ParseBlock(leftBrace);

        if (Match(out var arrow, SyntaxKind.Arrow))
            return new ExpressionBody(arrow, ParseExpression());

        return new NullStatement(Current());
    }

    private TypeAlias ParseTypeAlias(Token keyword)
    {
        var name = ExpectIdentifier();
        var typeParameters = ParseTypeParameters();
        var equals = Expect(SyntaxKind.Equals);
        var type = ParseType();
        var equalsTypeClause = new EqualsTypeClause(equals, type);
        return new TypeAlias(keyword, name, typeParameters, equalsTypeClause);
    }

    private Statement ParseVariableDeclaration(Token keyword)
    {
        if (Current().Kind is SyntaxKind.LBracket or SyntaxKind.LBrace or SyntaxKind.LParen)
            return ParseDestructuringDeclaration(keyword);

        var name = ExpectIdentifier();
        var colonTypeClause = ParseColonTypeClause();
        var equalsValueClause = ParseEqualsValueClause();
        return new VariableDeclaration(keyword, name, colonTypeClause, equalsValueClause);
    }

    private DestructuringDeclaration ParseDestructuringDeclaration(Token keyword)
    {
        var target = ParseDestructuringTarget();
        var colonTypeClause = ParseColonTypeClause();
        var equalsValueClause = ParseEqualsValueClause();
        return new DestructuringDeclaration(keyword, target, colonTypeClause, equalsValueClause);
    }

    private DestructuringTarget ParseDestructuringTarget() =>
        Current().Kind switch
        {
            SyntaxKind.LBrace => ParseObjectDestructuringTarget(),
            SyntaxKind.LParen => ParseTupleDestructuringTarget(),
            _ => ParseArrayDestructuringTarget()
        };

    private TupleDestructuringTarget ParseTupleDestructuringTarget()
    {
        var leftParen = Expect(SyntaxKind.LParen);
        var elements = ParseDelimited(ParseDestructuringElement);
        var rightParen = Expect(SyntaxKind.RParen);
        return new TupleDestructuringTarget(leftParen, rightParen, elements);
    }

    private ArrayDestructuringTarget ParseArrayDestructuringTarget()
    {
        var leftBracket = Expect(SyntaxKind.LBracket);
        var elements = ParseDelimited(ParseDestructuringElement);
        var rightBracket = Expect(SyntaxKind.RBracket);
        return new ArrayDestructuringTarget(leftBracket, rightBracket, elements);
    }

    private DestructuringElement ParseDestructuringElement()
    {
        if (Match(out var dotDot, SyntaxKind.DotDot))
            _diagnostics.Error(dotDot, InternalCodes.InvalidDestructureTarget, "Destructuring targets do not support rest elements.");

        return new DestructuringElement(ExpectIdentifier());
    }

    private ObjectDestructuringTarget ParseObjectDestructuringTarget()
    {
        var leftBrace = Expect(SyntaxKind.LBrace);
        var fields = ParseDelimited(ParseObjectDestructuringField);
        var rightBrace = Expect(SyntaxKind.RBrace);
        return new ObjectDestructuringTarget(leftBrace, rightBrace, fields);
    }

    private ObjectDestructuringField ParseObjectDestructuringField()
    {
        if (Match(out var dotDot, SyntaxKind.DotDot))
            _diagnostics.Error(dotDot, InternalCodes.InvalidDestructureTarget, "Destructuring targets do not support rest elements.");

        var name = ExpectIdentifier();
        var colon = Match(out var colonToken, SyntaxKind.Colon) ? colonToken : null;
        var alias = colon != null ? ExpectIdentifier() : null;
        return new ObjectDestructuringField(name, colon, alias);
    }

    private EnumDeclaration ParseEnumDeclaration(Token keyword)
    {
        var name = ExpectIdentifier();
        var colonTypeClause = ParseColonTypeClause();
        var leftBrace = Expect(SyntaxKind.LBrace);
        var members = !IsEof() && Current() is { Kind: SyntaxKind.Identifier } ? ParseDelimited(ParseEnumMember).OfType<EnumMember>().ToList() : [];
        var rightBrace = Expect(SyntaxKind.RBrace);
        return new EnumDeclaration(
            keyword,
            name,
            leftBrace,
            rightBrace,
            colonTypeClause,
            members
        );
    }

    private EnumMember? ParseEnumMember() => Match(out var name, SyntaxKind.Identifier) ? new EnumMember(name, ParseEqualsValueClause()) : null;

    private Statement ParseEventDeclaration(Token keyword, Attributes? attributes)
    {
        var name = ExpectIdentifier();
        var typeParameters = ParseTypeParameters();
        var parameters = ParseParameters();
        if (!ValidateSignatureParameters("event declarations", parameters))
            return new NullStatement(keyword);

        return new EventDeclaration(keyword, name, typeParameters, parameters, attributes);
    }

    private Parameters? ParseParameters()
    {
        if (!Match(out var leftParen, SyntaxKind.LParen))
            return null;

        if (Match(out var rightParen, SyntaxKind.RParen))
            return new Parameters(leftParen, rightParen, []);

        var parameters = ParseDelimited(ParseParameter);
        rightParen = Expect(SyntaxKind.RParen);
        ValidateRestParameterPlacement(parameters);

        return new Parameters(leftParen, rightParen, parameters);
    }

    private Parameter ParseParameter()
    {
        var dotDot = Match(out var dots, SyntaxKind.DotDot) ? dots : null;
        var name = ExpectIdentifier("parameter name");
        var colonTypeClause = ParseColonTypeClause();
        var equalsValueClause = ParseEqualsValueClause();
        return new Parameter(dotDot, name, colonTypeClause, equalsValueClause);
    }

    private EqualsValueClause? ParseEqualsValueClause() => Match(out var equals, SyntaxKind.Equals) ? new EqualsValueClause(equals, ParseExpression()) : null;
    private ColonTypeClause? ParseColonTypeClause() => Match(out var colon, SyntaxKind.Colon) ? new ColonTypeClause(colon, ParseType()) : null;

    private ColonTypeListClause? ParseColonTypeListClause() =>
        Match(out var colon, SyntaxKind.Colon) ? new ColonTypeListClause(colon, ParseDelimited(ParseType)) : null;

    private EqualsTypeClause? ParseEqualsTypeClause() => Match(out var equals, SyntaxKind.Equals) ? new EqualsTypeClause(equals, ParseType()) : null;

    private bool LooksLikeIndexer() => OffsetAfterBrackets() is { } end && PeekKind(end + 1) == SyntaxKind.Colon;

    private ColonTypeClause? ExpectInterfaceMemberColonTypeClause(string message)
    {
        var colonTypeClause = ParseColonTypeClause();
        if (colonTypeClause != null)
            return colonTypeClause;

        _diagnostics.Error(Current(), InternalCodes.ExpectedInterfaceMemberType, message);
        return null;
    }

    private void ValidateRestParameterPlacement(List<Parameter> parameters)
    {
        for (var i = 0; i < parameters.Count; i++)
        {
            var parameter = parameters[i];
            if (parameter.DotDot == null) continue;

            if (i != parameters.Count - 1)
                _diagnostics.Error(parameter, InternalCodes.RestParameterNotLast, "A rest parameter must be the last parameter.");

            if (parameter.ColonTypeClause == null)
                _diagnostics.Error(parameter, InternalCodes.MissingRestParameterType, "A rest parameter must have an explicit array type.");

            if (parameter.EqualsValueClause != null)
                _diagnostics.Error(parameter, InternalCodes.RestParameterHasDefaultValue, "A rest parameter may not have a default value.");
        }
    }
}