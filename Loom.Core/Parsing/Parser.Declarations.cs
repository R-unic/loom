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

            members.Add(ParseTraitMember(fnKeyword, attributes, asyncKeyword));
            Match(SyntaxKind.Comma, SyntaxKind.Semicolon);
        }

        return [.. members.OfType<DeclareFunctionSignature>()];
    }

    /// <summary>
    ///     A trait member with a body is a default implementation (a <see cref="FunctionDeclaration" />,
    ///     which is itself a <see cref="DeclareFunctionSignature" />) that an <c>implement</c> block may
    ///     omit; one without is the abstract signature every implementer must still provide.
    /// </summary>
    private Statement ParseTraitMember(Token fnKeyword, Attributes? attributes, Token? asyncKeyword)
    {
        var name = ExpectIdentifier("function name");
        var typeParameters = ParseTypeParameters();
        var parameters = ParseParameters();
        var returnType = ParseColonTypeClause();
        if (!ValidateFunctionSignature(
                "trait members",
                parameters?.LocationSpan ?? typeParameters?.LocationSpan ?? name.GetLocation(),
                returnType,
                parameters
            ))
            return new NullStatement(fnKeyword);

        if (Current().Kind is not (SyntaxKind.LBrace or SyntaxKind.Arrow))
            return new DeclareFunctionSignature(fnKeyword, name, typeParameters, parameters, returnType, attributes, asyncKeyword);

        var body = ParseFunctionBody();
        return new FunctionDeclaration(fnKeyword, name, typeParameters, parameters, returnType, body, attributes, asyncKeyword);
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
                var staticKeyword = Match(out var stk, SyntaxKind.StaticKeyword) ? stk : null;
                var mutKeyword = Match(out var kw, SyntaxKind.MutKeyword) ? kw : null;
                member = ParseInterfaceMember(staticKeyword, mutKeyword);
            }
            else if (LooksLikeIndexer())
            {
                member = ParseInterfaceMember(null, null);
            }
            else
            {
                Advance();
                var attributes = ParseAttributes(token);
                var staticKeyword = Match(out var stk, SyntaxKind.StaticKeyword) ? stk : null;
                member = staticKeyword == null && Match(out var eventKeyword, SyntaxKind.EventKeyword)
                    ? ParseEventDeclaration(eventKeyword, attributes)
                    : ParsePropertyDeclaration(staticKeyword, Match(out var mutKeyword, SyntaxKind.MutKeyword) ? mutKeyword : null, attributes);
            }

            if (member != null)
                members.Add(member);

            Match(SyntaxKind.Comma, SyntaxKind.Semicolon);
            EnsureProgress(previousPosition);
        }

        return members;
    }

    private Statement? ParseInterfaceMember(Token? staticKeyword, Token? mutKeyword)
    {
        if (Match(out var leftBracket, SyntaxKind.LBracket))
            return ParseIndexerDeclaration(mutKeyword, leftBracket);

        if (staticKeyword == null && mutKeyword == null && Match(out var keyword, SyntaxKind.EventKeyword))
            return ParseEventDeclaration(keyword, null);

        return ParsePropertyDeclaration(staticKeyword, mutKeyword, null);
    }

    private Statement? ParseIndexerDeclaration(Token? mutKeyword, Token leftBracket)
    {
        // '[K from ...]' binds a name for the keys it maps over, where '[K]' names the key type itself.
        if (Current() is { Kind: SyntaxKind.Identifier } && Peek(1) is { Kind: SyntaxKind.Identifier, Text: "from" })
            return ParseMappedTypeDeclaration(mutKeyword, leftBracket);

        var indexType = ParseType();
        var rightBracket = Expect(SyntaxKind.RBracket);
        var colonTypeClause = ExpectInterfaceMemberColonTypeClause($"Expected indexer type, got {SafeTokenText(Current())}.");
        return colonTypeClause == null ? null : new IndexerDeclaration(mutKeyword, leftBracket, rightBracket, indexType, colonTypeClause);
    }

    private MappedTypeDeclaration? ParseMappedTypeDeclaration(Token? mutKeyword, Token leftBracket)
    {
        var name = ExpectIdentifier("mapped type binder name");
        var fromKeyword = ExpectContextualKeyword("from");
        var sourceType = ParseType();
        var rightBracket = Expect(SyntaxKind.RBracket);
        var colonTypeClause = ExpectInterfaceMemberColonTypeClause($"Expected mapped member type, got {SafeTokenText(Current())}.");
        return colonTypeClause == null
            ? null
            : new MappedTypeDeclaration(mutKeyword, leftBracket, rightBracket, name, fromKeyword, sourceType, colonTypeClause);
    }

    private PropertyDeclaration? ParsePropertyDeclaration(Token? staticKeyword, Token? mutKeyword, Attributes? attributes)
    {
        var name = ExpectIdentifier("property name");
        var propertyType = ExpectInterfaceMemberColonTypeClause($"Expected indexer type, got {SafeTokenText(Current())}.");
        return propertyType == null ? null : new PropertyDeclaration(staticKeyword, mutKeyword, name, propertyType, attributes);
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

        if (Match(out var staticKeyword, SyntaxKind.StaticKeyword))
            return ParseDeclareStaticBlock(staticKeyword);

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

    private DeclareStaticBlock ParseDeclareStaticBlock(Token staticKeyword)
    {
        var name = ExpectIdentifier("type alias name");
        var leftBrace = Expect(SyntaxKind.LBrace);
        var members = ParseDeclareStaticBlockMembers();
        var rightBrace = Expect(SyntaxKind.RBrace);

        return new DeclareStaticBlock(staticKeyword, name, leftBrace, rightBrace, members);
    }

    private List<PropertyDeclaration> ParseDeclareStaticBlockMembers()
    {
        var members = new List<PropertyDeclaration>();
        while (!IsEof() && Current().Kind is not SyntaxKind.RBrace)
        {
            var previousPosition = _position;
            if (ParsePropertyDeclaration(null, null, null) is { } member)
                members.Add(member);

            Match(SyntaxKind.Comma, SyntaxKind.Semicolon);
            EnsureProgress(previousPosition);
        }

        return members;
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

    /// <summary>How many parameters of the current list were written without a name. See <see cref="ParseUnnamedPatternParameter" />.</summary>
    private int _unnamedParameterIndex;

    private Parameters? ParseParameters()
    {
        if (!Match(out var leftParen, SyntaxKind.LParen))
            return null;

        if (Match(out var rightParen, SyntaxKind.RParen))
            return new Parameters(leftParen, rightParen, []);

        // Restored rather than merely reset, so a function type written inside another's parameter list
        // numbers its own unnamed parameters from zero - each list is a scope of its own.
        var enclosingIndex = _unnamedParameterIndex;
        _unnamedParameterIndex = 0;
        var parameters = Bracketed(() => ParseDelimited(ParseParameter));
        _unnamedParameterIndex = enclosingIndex;

        rightParen = Expect(SyntaxKind.RParen);
        ValidateRestParameterPlacement(parameters);

        return new Parameters(leftParen, rightParen, parameters);
    }

    private Parameter ParseParameter()
    {
        var dotDot = Match(out var dots, SyntaxKind.DotDot) ? dots : null;
        if (_typePatternDepth > 0 && !AtNamedParameter())
            return ParseUnnamedPatternParameter(dotDot);

        var name = ExpectIdentifier("parameter name");
        var colonTypeClause = ParseColonTypeClause();
        var equalsValueClause = ParseEqualsValueClause();
        return new Parameter(dotDot, name, colonTypeClause, equalsValueClause);
    }

    private bool AtNamedParameter() => Current() is { Kind: SyntaxKind.Identifier } && PeekKind(1) == SyntaxKind.Colon;

    /// <summary>
    ///     A parameter of a function type written inside a type pattern - <c>fn(..let P): any</c> - where
    ///     only the type is being matched and there is no value for a name to stand for.
    /// </summary>
    /// <remarks>
    ///     The name is synthesized rather than omitted so the rest of the compiler keeps seeing an ordinary
    ///     parameter, and is spelled with a character no source can contain so two of them in one signature
    ///     are never the duplicate-name error.
    /// </remarks>
    private Parameter ParseUnnamedPatternParameter(Token? dotDot)
    {
        var start = Current();
        var span = new TextSpan(start.Span.Position, 0);
        var name = new Token(SyntaxKind.Identifier, start.File, span, $"$p{_unnamedParameterIndex++}");
        return new Parameter(dotDot, name, new ColonTypeClause(new Token(SyntaxKind.Colon, start.File, span, ":"), ParseType()), null);
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