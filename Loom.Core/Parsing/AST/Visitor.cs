// ReSharper disable VirtualMemberNeverOverridden.Global

namespace Loom.Core.Parsing.AST;

public abstract class Visitor<T>(Func<Node?, T> defaultValue)
{
    private T DefaultValue(Node? node) => defaultValue(node);

    protected abstract T Visit(Node node);

    protected TResult Visit<TResult>(Node node)
        where TResult : T =>
        (TResult)Visit(node)!;

    public virtual T VisitTree(Tree tree) => VisitList(tree.Statements);
    public virtual T VisitFor(For @for) => CombineResults([VisitList(@for.Names), Visit(@for.CollectionExpression), Visit(@for.Body)]);
    public virtual T VisitAfter(After after) => CombineResults([Visit(after.Duration), Visit(after.Body)]);
    public virtual T VisitEvery(Every every) => CombineResults([Visit(every.Duration), VisitWithDefault(every.Condition), Visit(every.Body)]);
    public virtual T VisitBreak(Break @break) => DefaultValue(@break);
    public virtual T VisitContinue(Continue @continue) => DefaultValue(@continue);
    public virtual T VisitWhile(While @while) => CombineResults([Visit(@while.Condition), Visit(@while.Body)]);
    public virtual T VisitIf(If @if) => CombineResults([Visit(@if.Condition), Visit(@if.ThenBranch), VisitWithDefault(@if.ElseBranch)]);
    public virtual T VisitElseBranch(ElseBranch elseBranch) => Visit(elseBranch.Branch);

    public virtual T VisitMatchExpression(MatchExpression matchExpression) => CombineResults([Visit(matchExpression.Expression), VisitList(matchExpression.Arms)]);

    public virtual T VisitMatchArm(MatchArm matchArm) => CombineResults([Visit(matchArm.Pattern), VisitWithDefault(matchArm.Guard), Visit(matchArm.Body)]);

    public virtual T VisitWildcardPattern(WildcardPattern wildcardPattern) => DefaultValue(wildcardPattern);
    public virtual T VisitIdentifierPattern(IdentifierPattern identifierPattern) => DefaultValue(identifierPattern);
    public virtual T VisitLiteralPattern(LiteralPattern literalPattern) => DefaultValue(literalPattern);
    public virtual T VisitOrPattern(OrPattern orPattern) => VisitList(orPattern.Patterns);
    public virtual T VisitAndPattern(AndPattern andPattern) => CombineResults([Visit(andPattern.Pattern), Visit(andPattern.Guard)]);
    public virtual T VisitNotPattern(NotPattern notPattern) => Visit(notPattern.Pattern);

    public virtual T VisitRangePattern(RangePattern rangePattern) => CombineResults([Visit(rangePattern.Minimum), Visit(rangePattern.Maximum)]);

    public virtual T VisitLetPattern(LetPattern letPattern) => DefaultValue(letPattern);

    public virtual T VisitTypedPattern(TypedPattern typedPattern) => CombineResults([Visit(typedPattern.Type), VisitWithDefault(typedPattern.ObjectPattern)]);

    public virtual T VisitTypePattern(TypePattern typePattern) => CombineResults([Visit(typePattern.Type), VisitWithDefault(typePattern.ObjectPattern)]);

    public virtual T VisitObjectPattern(ObjectPattern objectPattern) => VisitList(objectPattern.Fields);
    public virtual T VisitObjectPatternField(ObjectPatternField objectPatternField) => Visit(objectPatternField.Pattern);

    public virtual T VisitArrayPattern(ArrayPattern arrayPattern) => CombineResults([VisitList(arrayPattern.Elements), VisitWithDefault(arrayPattern.Rest)]);

    public virtual T VisitRestPattern(RestPattern restPattern) => Visit(restPattern.Pattern);
    public virtual T VisitTuplePattern(TuplePattern tuplePattern) => VisitList(tuplePattern.Patterns);
    public virtual T VisitNullPattern(NullPattern nullPattern) => DefaultValue(nullPattern);

    public virtual T VisitImplementBody(ImplementBody implementBody) => VisitList(implementBody.Implementations);
    public virtual T VisitImplement(Implement implement) => CombineResults([Visit(implement.TraitName), Visit(implement.InterfaceName), Visit(implement.Body)]);
    public virtual T VisitTraitBody(TraitBody traitBody) => VisitList(traitBody.Members);

    public virtual T VisitTraitDeclaration(TraitDeclaration traitDeclaration) =>
        CombineResults([VisitWithDefault(traitDeclaration.TypeParameters), Visit(traitDeclaration.Body)]);

    public virtual T VisitIndexerDeclaration(IndexerDeclaration indexerDeclaration) =>
        CombineResults([Visit(indexerDeclaration.IndexType), Visit(indexerDeclaration.ColonTypeClause)]);

    public virtual T VisitPropertyDeclaration(PropertyDeclaration propertyDeclaration) =>
        CombineResults([VisitWithDefault(propertyDeclaration.Attributes), Visit(propertyDeclaration.ColonTypeClause)]);

    public virtual T VisitInterfaceBody(InterfaceBody interfaceBody) => VisitList(interfaceBody.Members);

    public virtual T VisitInterfaceDeclaration(InterfaceDeclaration interfaceDeclaration) =>
        CombineResults(
            [
                VisitWithDefault(interfaceDeclaration.TypeParameters),
                VisitWithDefault(interfaceDeclaration.ColonTypeListClause),
                VisitWithDefault(interfaceDeclaration.Attributes),
                VisitWithDefault(interfaceDeclaration.Body)
            ]
        );

    public virtual T VisitFunctionDeclaration(FunctionDeclaration functionDeclaration) =>
        CombineResults(
            [
                VisitWithDefault(functionDeclaration.TypeParameters),
                VisitWithDefault(functionDeclaration.Parameters),
                VisitWithDefault(functionDeclaration.ReturnType),
                VisitWithDefault(functionDeclaration.Attributes),
                Visit(functionDeclaration.Body)
            ]
        );

    public virtual T VisitFunctionExpression(FunctionExpression functionExpression) =>
        CombineResults(
            [
                VisitWithDefault(functionExpression.TypeParameters),
                VisitWithDefault(functionExpression.Parameters),
                VisitWithDefault(functionExpression.ReturnType),
                Visit(functionExpression.Body)
            ]
        );

    public virtual T VisitDeclare(Declare declare) => Visit(declare.Signature);
    public virtual T VisitExportDeclaration(ExportDeclaration export) => Visit(export.Declaration);

    public virtual T VisitImportDeclaration(ImportDeclaration import) => CombineResults([VisitList(import.Specifiers), Visit(import.ModuleSpecifier)]);
    public virtual T VisitImportSpecifier(ImportSpecifier specifier) => DefaultValue(specifier);

    public virtual T VisitNamespaceImport(NamespaceImport import) => DefaultValue(import);

    public virtual T VisitExportList(ExportList export) => CombineResults([VisitList(export.Specifiers), VisitWithDefault(export.ModuleSpecifier)]);

    public virtual T VisitExportSpecifier(ExportSpecifier specifier) => DefaultValue(specifier);

    public virtual T VisitExportAll(ExportAll export) => VisitWithDefault(export.ModuleSpecifier);

    public virtual T VisitDeclareVariableSignature(DeclareVariableSignature declareVariableSignature) => VisitWithDefault(declareVariableSignature.ColonTypeClause);

    public virtual T VisitDeclareFunctionSignature(DeclareFunctionSignature declareFunctionSignature) =>
        CombineResults(
            [
                VisitWithDefault(declareFunctionSignature.TypeParameters),
                VisitWithDefault(declareFunctionSignature.Parameters),
                VisitWithDefault(declareFunctionSignature.Attributes),
                Visit(declareFunctionSignature.ReturnType)
            ]
        );

    public virtual T VisitTypeAlias(TypeAlias typeAlias) => CombineResults([VisitWithDefault(typeAlias.TypeParameters), Visit(typeAlias.EqualsTypeClause)]);

    public virtual T VisitVariableDeclaration(VariableDeclaration variableDeclaration) =>
        CombineResults([VisitWithDefault(variableDeclaration.ColonTypeClause), VisitWithDefault(variableDeclaration.EqualsValueClause)]);

    public virtual T VisitDestructuringDeclaration(DestructuringDeclaration destructuringDeclaration) =>
        CombineResults(
            [
                Visit(destructuringDeclaration.Target),
                VisitWithDefault(destructuringDeclaration.ColonTypeClause),
                VisitWithDefault(destructuringDeclaration.EqualsValueClause)
            ]
        );

    public virtual T VisitDestructuringElement(DestructuringElement destructuringElement) => DefaultValue(destructuringElement);

    public virtual T VisitArrayDestructuringTarget(ArrayDestructuringTarget arrayDestructuringTarget) => VisitList(arrayDestructuringTarget.Elements);

    public virtual T VisitObjectDestructuringTarget(ObjectDestructuringTarget objectDestructuringTarget) => VisitList(objectDestructuringTarget.Fields);

    public virtual T VisitObjectDestructuringField(ObjectDestructuringField objectDestructuringField) => DefaultValue(objectDestructuringField);

    public virtual T VisitTupleDestructuringTarget(TupleDestructuringTarget tupleDestructuringTarget) => VisitList(tupleDestructuringTarget.Elements);

    public virtual T VisitEnumDeclaration(EnumDeclaration enumDeclaration) => VisitList(enumDeclaration.Members);
    public virtual T VisitEnumMember(EnumMember enumMember) => VisitWithDefault(enumMember.EqualsValueClause);

    public virtual T VisitEventDeclaration(EventDeclaration eventDeclaration) =>
        CombineResults(
            [VisitWithDefault(eventDeclaration.TypeParameters), VisitWithDefault(eventDeclaration.Parameters), VisitWithDefault(eventDeclaration.Attributes)]
        );

    public virtual T VisitParameters(Parameters parameters) => VisitList(parameters.ParameterList);

    public virtual T VisitParameter(Parameter parameter) =>
        CombineResults([VisitWithDefault(parameter.ColonTypeClause), VisitWithDefault(parameter.EqualsValueClause)]);

    public virtual T VisitBlock(Block block) => VisitList(block.Statements);
    public virtual T VisitExpressionStatement(ExpressionStatement expressionStatement) => Visit(expressionStatement.Expression);
    public virtual T VisitReturn(Return @return) => VisitWithDefault(@return.Expression);
    public virtual T VisitExpressionBody(ExpressionBody expressionBody) => Visit(expressionBody.Expression);

    public virtual T VisitInterfaceInvocation(InterfaceInvocation interfaceInvocation) =>
        CombineResults([Visit(interfaceInvocation.Name), VisitWithDefault(interfaceInvocation.TypeArguments), Visit(interfaceInvocation.Body)]);

    public virtual T VisitInterfaceInvocationBody(InterfaceInvocationBody interfaceInvocationBody) => VisitList(interfaceInvocationBody.Initializers);

    public virtual T VisitInterfaceInvocationIndexInitializer(IndexInitializer indexInitializer) =>
        CombineResults([Visit(indexInitializer.IndexExpression), Visit(indexInitializer.Expression)]);

    public virtual T VisitInterfaceInvocationPropertyInitializer(PropertyInitializer propertyInitializer) => Visit(propertyInitializer.Expression);

    public virtual T VisitInterfaceInvocationShorthandPropertyInitializer(ShorthandPropertyInitializer shorthandPropertyInitializer) =>
        Visit(shorthandPropertyInitializer.Expression);

    public virtual T VisitRangeLiteral(RangeLiteral rangeLiteral) => CombineResults([Visit(rangeLiteral.Minimum), Visit(rangeLiteral.Maximum)]);
    public virtual T VisitArrayLiteral(ArrayLiteral arrayLiteral) => VisitList(arrayLiteral.Expressions);
    public virtual T VisitLiteral(Literal literal) => DefaultValue(literal);
    public virtual T VisitInterpolatedStringLiteral(InterpolatedStringLiteral interpolatedStringLiteral) => VisitList(interpolatedStringLiteral.Expressions);
    public virtual T VisitIdentifier(Identifier identifier) => DefaultValue(identifier);
    public virtual T VisitSelfExpression(SelfExpression selfExpression) => DefaultValue(selfExpression);
    public virtual T VisitParenthesized(Parenthesized parenthesized) => Visit(parenthesized.Expression);
    public virtual T VisitTupleExpression(TupleExpression tupleExpression) => VisitList(tupleExpression.Expressions);
    public virtual T VisitNameOf(NameOf nameOf) => CombineResults([VisitWithDefault(nameOf.TypeArguments), VisitWithDefault(nameOf.Name)]);
    public virtual T VisitArguments(Arguments arguments) => VisitList(arguments.ArgumentList);

    public virtual T VisitInvocation(Invocation invocation) =>
        CombineResults([Visit(invocation.Expression), VisitWithDefault(invocation.TypeArguments), Visit(invocation.Arguments)]);

    public virtual T VisitQualifiedName(QualifiedName qualifiedName) => Visit(qualifiedName.Identifier);
    public virtual T VisitPropertyAccess(PropertyAccess propertyAccess) => Visit(propertyAccess.Expression);
    public virtual T VisitElementAccess(ElementAccess elementAccess) => CombineResults([Visit(elementAccess.Expression), Visit(elementAccess.IndexExpression)]);

    public virtual T VisitAs(As @as) => CombineResults([Visit(@as.Expression), Visit(@as.Type)]);
    public virtual T VisitNullForgiving(NullForgiving nullForgiving) => Visit(nullForgiving.Expression);
    public virtual T VisitErrorPropagation(ErrorPropagation errorPropagation) => Visit(errorPropagation.Expression);
    public virtual T VisitAwait(Await await) => Visit(await.Expression);
    public virtual T VisitIs(Is @is) => CombineResults([Visit(@is.Expression), Visit(@is.Pattern)]);

    public virtual T VisitAssignmentOperator(AssignmentOperator assignmentOperator) =>
        CombineResults([Visit(assignmentOperator.Left), Visit(assignmentOperator.Right)]);

    public virtual T VisitTernaryOperator(TernaryOperator ternaryOperator) =>
        CombineResults([Visit(ternaryOperator.Condition), Visit(ternaryOperator.ThenBranch), Visit(ternaryOperator.ElseBranch)]);

    public virtual T VisitBinaryOperator(BinaryOperator binaryOperator) => CombineResults([Visit(binaryOperator.Left), Visit(binaryOperator.Right)]);
    public virtual T VisitUnaryOperator(UnaryOperator unaryOperator) => Visit(unaryOperator.Operand);
    public virtual T VisitLiteralType(LiteralType literalType) => DefaultValue(literalType);
    public virtual T VisitPrimitiveType(PrimitiveType primitiveType) => DefaultValue(primitiveType);
    public virtual T VisitTypeName(TypeName typeName) => VisitWithDefault(typeName.TypeArguments);
    public virtual T VisitParenthesizedType(ParenthesizedType parenthesized) => Visit(parenthesized.Type);
    public virtual T VisitTupleType(TupleType tupleType) => VisitList(tupleType.Types);
    public virtual T VisitIndexedType(IndexedType indexedType) => CombineResults([Visit(indexedType.TargetType), Visit(indexedType.IndexType)]);
    public virtual T VisitKeyOf(KeyOf keyOf) => Visit(keyOf.Type);
    public virtual T VisitTypeOf(TypeOf typeOf) => Visit(typeOf.Expression);
    public virtual T VisitTypePredicateType(TypePredicateType typePredicateType) => CombineResults([Visit(typePredicateType.Subject), Visit(typePredicateType.Type)]);

    public virtual T VisitFunctionType(FunctionType functionType) =>
        CombineResults([VisitWithDefault(functionType.TypeParameters), VisitWithDefault(functionType.Parameters), Visit(functionType.ReturnType)]);

    public virtual T VisitArrayType(ArrayType arrayType) => Visit(arrayType.ElementType);
    public virtual T VisitOptionalType(OptionalType optionalType) => Visit(optionalType.NonNullableType);
    public virtual T VisitUnionType(UnionType unionType) => VisitList(unionType.Types);
    public virtual T VisitIntersectionType(IntersectionType intersectionType) => VisitList(intersectionType.Types);

    public virtual T VisitTypeParameter(TypeParameter typeParameter) =>
        CombineResults([VisitWithDefault(typeParameter.ColonTypeClause), VisitWithDefault(typeParameter.EqualsTypeClause)]);

    public virtual T VisitTypeParameters(TypeParameters typeParameters) => VisitList(typeParameters.ParameterList);

    public virtual T VisitTypeArguments<TType>(TypeArguments<TType> typeArguments)
        where TType : TypeExpression =>
        VisitList(typeArguments.ArgumentsList);

    public virtual T VisitAttribute(Attribute attribute) => CombineResults([Visit(attribute.Expression), VisitWithDefault(attribute.Arguments)]);
    public virtual T VisitAttributes(Attributes attributes) => VisitList(attributes.AttributeList);
    public virtual T VisitColonTypeListClause(ColonTypeListClause colonTypeListClause) => VisitList(colonTypeListClause.Types);
    public virtual T VisitColonTypeClause(ColonTypeClause colonTypeClause) => Visit(colonTypeClause.Type);
    public virtual T VisitEqualsTypeClause(EqualsTypeClause equalsTypeClause) => Visit(equalsTypeClause.Type);
    public virtual T VisitEqualsValueClause(EqualsValueClause equalsValueClause) => Visit(equalsValueClause.Value);

    public virtual T VisitNullExpression(NullExpression _) => DefaultValue(_);
    public virtual T VisitNullStatement(NullStatement _) => DefaultValue(_);
    public virtual T VisitNullTypeExpression(NullTypeExpression _) => DefaultValue(_);

    protected virtual T CombineResults(ReadOnlySpan<T?> results)
    {
        T result = default!;

        foreach (var item in results)
            if (item != null)
                result = item;

        return result;
    }

    protected TResult? MaybeVisit<TResult>(Node? node)
        where TResult : T =>
        node is null ? default : Visit<TResult>(node);

    protected T? MaybeVisit(Node? node) => node is null ? default : Visit(node);

    /// <remarks>
    ///     Cannot be written as <c>MaybeVisit(node) ?? DefaultValue(node)</c>: when <typeparamref name="T" />
    ///     is a value type the null coalescence never runs, so a missing node would yield
    ///     <c>default(T)</c> instead of the visitor's default value.
    /// </remarks>
    private T VisitWithDefault(Node? node) => node is null ? DefaultValue(node) : MaybeVisit(node) ?? DefaultValue(node);

    private T VisitList<TNode>(List<TNode> nodes)
        where TNode : Node
    {
        if (nodes.Count == 0)
            return DefaultValue(null);

        var results = new T?[nodes.Count];
        for (var i = 0; i < nodes.Count; i++)
            results[i] = Visit(nodes[i]);

        return CombineResults(results);
    }
}