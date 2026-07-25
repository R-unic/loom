using System.Diagnostics.CodeAnalysis;
using Loom.Core.Diagnostics;
using Loom.Core.FlowAnalysis;
using Loom.Core.Generation;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Core.Text;
using Loom.Core.TypeChecking.Types;
using Loom.Core.Generation.Macros;
using ArrayType = Loom.Core.Parsing.AST.ArrayType;
using Attribute = Loom.Core.Parsing.AST.Attribute;
using FunctionType = Loom.Core.Parsing.AST.FunctionType;
using IndexedType = Loom.Core.Parsing.AST.IndexedType;
using IntersectionType = Loom.Core.Parsing.AST.IntersectionType;
using LiteralType = Loom.Core.Parsing.AST.LiteralType;
using OptionalType = Loom.Core.Parsing.AST.OptionalType;
using PrimitiveType = Loom.Core.Parsing.AST.PrimitiveType;
using TypeName = Loom.Core.Parsing.AST.TypeName;
using TypeParameter = Loom.Core.Parsing.AST.TypeParameter;
using UnionType = Loom.Core.Parsing.AST.UnionType;

namespace Loom.Core.TypeChecking;

using Type = Types.Type;

public sealed partial class TypeChecker
    : Visitor<Type>
{
    private readonly DiagnosticBag _diagnostics = new();
    private readonly Dictionary<Node, FlowState> _exitStates = [];
    private readonly Stack<List<FlowState>> _loopExitScopes = [];
    private readonly SemanticModel _semanticModel;
    private readonly FlowAnalyzer _flowAnalyzer;
    private readonly TypeInferrer _inferrer;
    private readonly TypeNarrower _narrower;
    private FlowState _flowState;
    private Symbol? _resolvingHoisted;

    private MacroContext EmptyMacroContext => field ??= new MacroContext(_semanticModel, new LuauState(), _diagnostics);

    public TypeChecker(SemanticModel semanticModel, FlowAnalyzer flowAnalyzer)
        : base(_ => Types.PrimitiveType.Never)
    {
        _semanticModel = semanticModel;
        _flowAnalyzer = flowAnalyzer;
        _inferrer = new TypeInferrer(Visit);
        _narrower = new TypeNarrower(semanticModel);
        _flowState = null!;
    }

    public TypeCheckerResult Check()
    {
        var tree = _semanticModel.Tree;
        var type = BindType(tree, VisitTree(tree));
        _semanticModel.TypeSolver.SolveConstraints();

        var diagnostics = DiagnosticBag.Concat([_semanticModel.TypeSolver.Diagnostics, _diagnostics]);
        return new TypeCheckerResult(type, diagnostics);
    }

    protected override Type Visit(Node node) => Visit(node, _flowState);

    private Type Visit(Node node, FlowState? state)
    {
        var lastState = _flowState;
        FlowState effectiveState;
        if (state != null)
        {
            effectiveState = state;
        }
        else
        {
            var baseState = _flowAnalyzer.GetState(node);
            effectiveState = new FlowState(baseState.DefinitelyInitialized, baseState.MaybeInitialized, baseState.IsUnreachable, lastState.NarrowedTypes);
        }

        _flowState = effectiveState;
        var type = node.Accept(this);
        _flowState = lastState;

        return type;
    }

    public override Type VisitTree(Tree tree)
    {
        _flowState = _flowAnalyzer.GetState(tree);
        var types = CheckStatements(tree, tree.Statements);
        return BindType(tree, types.LastOrDefault(Types.PrimitiveType.Void));
    }

    public override Type VisitExpressionStatement(ExpressionStatement expressionStatement) => BindType(expressionStatement, Visit(expressionStatement.Expression));
    public override Type VisitBlock(Block block) => BindType(block, CheckStatements(block, block.Statements).LastOrDefault(Types.PrimitiveType.Void));

    public override Type VisitEventDeclaration(EventDeclaration eventDeclaration)
    {
        MaybeVisit(eventDeclaration.Attributes);
        if (_semanticModel.GetDeclarationSymbol(eventDeclaration, SymbolKind.Event) is not { } symbol)
        {
            _diagnostics.Error(eventDeclaration, InternalCodes.CannotFindSymbol, $"Cannot find symbol for declaration of event '{eventDeclaration.Name.Text}'.");
            return BindType(eventDeclaration, Types.PrimitiveType.Never);
        }

        var parameterTypes = eventDeclaration.Parameters?.ParameterList.ConvertAll(VisitParameter) ?? [];
        var type = InstantiateEventType(eventDeclaration, symbol.IsAmbient, parameterTypes);
        return BindType(eventDeclaration, type);
    }

    public override Type VisitAttribute(Attribute attribute)
    {
        var expressionType = Visit(attribute.Expression);
        if (expressionType is not Types.FunctionType)
            _diagnostics.Error(attribute, InternalCodes.NonFunctionAttribute, "Only functions may be used as attributes.");

        return expressionType;
    }

    public override Type VisitBreak(Break @break)
    {
        _loopExitScopes.Peek().Add(_flowState);
        return BindType(@break, Types.PrimitiveType.Void);
    }

    public override Type VisitContinue(Continue @continue)
    {
        _loopExitScopes.Peek().Add(_flowState);
        return BindType(@continue, Types.PrimitiveType.Void);
    }

    public override Type VisitFor(For @for)
    {
        var collectionType = Visit(@for.CollectionExpression);
        if (collectionType is InstantiatedType i)
            collectionType = i.Expand();

        _semanticModel.TypeSolver.AddConstraint(collectionType, ObjectType.Empty, @for.CollectionExpression);
        var isRange = collectionType.Equals(Intrinsics.Range);
        var elementType = isRange ? Types.PrimitiveType.Number : GetObjectValueType(collectionType);
        var maxNames = isRange ? 1 : 2;
        if (@for.Names.Count > maxNames)
        {
            _diagnostics.NotImplemented(
                @for.Names[maxNames],
                isRange
                    ? "Iterating over a range only produces one value, so only one name is permitted."
                    : "Functional iterators are not supported yet, so more than two names is not permitted."
            );

            return BindType(@for, Types.PrimitiveType.Never);
        }

        switch (collectionType)
        {
            case var _ when collectionType.Equals(Intrinsics.Range):
                BindType(@for.Names[0], elementType);
                break;
            case Types.ArrayType:
                BindType(@for.Names[0], elementType);
                if (@for.Names.Count > 1)
                    BindType(@for.Names[1], Types.PrimitiveType.Number);

                break;
            case InterfaceType or ObjectType:
            {
                var objectType = collectionType is InterfaceType interfaceType ? interfaceType.ObjectType : (ObjectType)collectionType;
                BindType(@for.Names[0], objectType.KeyUnion());
                if (@for.Names.Count > 1)
                    BindType(@for.Names[1], elementType);

                break;
            }
        }

        _loopExitScopes.Push([]);
        var bodyType = CheckBody(@for.Body, _flowState);
        AssignLoopExitState(@for);

        return BindType(@for, bodyType);
    }

    public override Type VisitAfter(After after)
    {
        var durationType = Visit(after.Duration);
        _semanticModel.TypeSolver.AddConstraint(durationType, Types.PrimitiveType.Number, after.Duration);

        return BindType(after, Visit(after.Body));
    }

    public override Type VisitWhile(While @while)
    {
        var conditionType = Visit(@while.Condition);
        _semanticModel.TypeSolver.AddConstraint(conditionType, Types.PrimitiveType.Bool, @while.Condition);

        _loopExitScopes.Push([]);
        var (trueState, _) = _narrower.ComputeBranchStates(@while.Condition, _flowState);
        var bodyType = CheckBody(@while.Body, trueState);
        AssignLoopExitState(@while);

        return BindType(@while, bodyType);
    }

    public override Type VisitIf(If @if)
    {
        var conditionType = Visit(@if.Condition);
        _semanticModel.TypeSolver.AddConstraint(conditionType, Types.PrimitiveType.Bool, @if.Condition);

        var (trueState, falseState) = _narrower.ComputeBranchStates(@if.Condition, _flowState);
        var thenType = CheckBody(@if.ThenBranch, trueState);
        var thenExit = _exitStates.GetValueOrDefault(@if.ThenBranch, trueState);
        var elseType = @if.ElseBranch != null ? CheckBody(@if.ElseBranch, falseState) : Types.PrimitiveType.None;
        var elseExit = @if.ElseBranch != null ? _exitStates.GetValueOrDefault(@if.ElseBranch, falseState) : falseState;

        _exitStates[@if] = MergeExitStates(thenExit, elseExit);
        return BindType(@if, TypeSimplifier.Simplify(new Types.UnionType([thenType, elseType])));
    }

    public override Type VisitReturn(Return @return)
    {
        if (@return.Expression == null)
            return BindType(@return, Types.PrimitiveType.Void);

        var expected = GetEnclosingDeclaredReturnType(@return);
        var actual = expected != null
            ? Check(@return.Expression, expected)
            : Visit(@return.Expression);

        return BindType(@return, actual);
    }

    public override Type VisitFunctionDeclaration(FunctionDeclaration functionDeclaration)
    {
        var typeParameters = functionDeclaration.TypeParameters?.ParameterList.ConvertAll(VisitTypeParameter) ?? [];
        var parameterTypes = functionDeclaration.Parameters?.ParameterList.ConvertAll(Visit) ?? [];
        MaybeVisit(functionDeclaration.ReturnType);

        var returnType = GetReturnType(functionDeclaration);
        var functionType = BindType(
            functionDeclaration,
            new Types.FunctionType(typeParameters, parameterTypes, returnType, HasRestParameter(functionDeclaration.Parameters))
        );

        if (functionDeclaration.Body is ExpressionBody body)
        {
            if (functionDeclaration.ReturnType != null)
                Check(body.Expression, returnType);

            // Else... GetReturnType had already visited expression body
        }
        else
        {
            Visit(functionDeclaration.Body);
        }

        return functionType;
    }

    public override Type VisitTypeAlias(TypeAlias typeAlias)
    {
        if (typeAlias.TypeParameters == null)
        {
            var type = Visit(typeAlias.EqualsTypeClause);
            _semanticModel.TypeSolver.CheckCircular(ref type, typeAlias.Name);
            return BindType(typeAlias, TypeSimplifier.Simplify(type));
        }

        var parameters = typeAlias.TypeParameters.ParameterList.ConvertAll(VisitTypeParameter);
        var underlyingType = Visit(typeAlias.EqualsTypeClause);
        _semanticModel.TypeSolver.CheckCircular(ref underlyingType, typeAlias.Name);

        var genericType = VarianceInferrer.ApplyInferredVariance(new GenericType(typeAlias, parameters, TypeSimplifier.Simplify(underlyingType)));
        return BindType(typeAlias, genericType);
    }

    public override Type VisitVariableDeclaration(VariableDeclaration variableDeclaration)
    {
        Type? declaredType = null;
        if (variableDeclaration.ColonTypeClause != null)
            declaredType = Visit(variableDeclaration.ColonTypeClause);

        var initializerType = variableDeclaration.EqualsValueClause != null
            ? declaredType != null
                ? Check(variableDeclaration.EqualsValueClause.Value, declaredType, _flowState)
                : Visit(variableDeclaration.EqualsValueClause)
            : null;

        Type finalType;
        if (declaredType != null)
        {
            finalType = declaredType;
        }
        else if (initializerType != null)
        {
            finalType = initializerType;
        }
        else
        {
            finalType = Types.PrimitiveType.Unknown;
        }

        if (variableDeclaration.Keyword.Kind == SyntaxKind.MutKeyword)
            finalType = finalType.Widen();

        return BindType(variableDeclaration, TypeSimplifier.Simplify(finalType));
    }

    public override Type VisitDeclare(Declare declare)
    {
        var type = declare.Signature switch
        {
            InterfaceDeclaration interfaceDeclaration => Visit(interfaceDeclaration),
            DeclareVariableSignature variableSignature => Visit(variableSignature.ColonTypeClause!),
            DeclareFunctionSignature functionSignature => Visit(functionSignature),
            _ => Types.PrimitiveType.Never
        };

        BindType(declare.Signature, type);
        return BindType(declare, type);
    }

    public override Type VisitDeclareFunctionSignature(DeclareFunctionSignature declareFunctionSignature) =>
        new Types.FunctionType(
            declareFunctionSignature.TypeParameters?.ParameterList.ConvertAll(VisitTypeParameter) ?? [],
            declareFunctionSignature.Parameters?.ParameterList.ConvertAll(Visit) ?? [],
            Visit(declareFunctionSignature.ReturnType),
            HasRestParameter(declareFunctionSignature.Parameters)
        );

    private static bool HasRestParameter(Parameters? parameters) => parameters?.ParameterList is [.., { DotDot: not null }];

    public override Type VisitParameter(Parameter parameter)
    {
        var declaredType = MaybeVisit(parameter.ColonTypeClause);
        var initializerType = MaybeVisit(parameter.EqualsValueClause);
        if (declaredType != null && parameter.EqualsValueClause != null)
            _semanticModel.TypeSolver.AddConstraint(initializerType!, declaredType, parameter.EqualsValueClause.Value);

        if (parameter.DotDot != null && declaredType != null && !IsArrayType(declaredType))
            _diagnostics.Error(
                parameter,
                InternalCodes.InvalidRestParameterType,
                $"Rest parameter '{parameter.Name.Text}' must have an array type, but got '{declaredType}'."
            );

        return BindType(parameter, declaredType ?? initializerType!);
    }

    private static bool IsArrayType(Type type) => (type is Types.InstantiatedType instantiated ? instantiated.Expand() : type) is Types.ArrayType;

    public override Type VisitAs(As @as)
    {
        var expressionType = Visit(@as.Expression);
        var castedType = TypeSimplifier.Simplify(Visit(@as.Type));
        if (Type.IsNotUnknown(expressionType) && Type.IsNotNever(castedType) && Type.IsNotUnknown(castedType))
            _semanticModel.TypeSolver.AddConstraint(expressionType, castedType, @as);

        return BindType(@as, castedType);
    }

    public override Type VisitNameOf(NameOf nameOf) =>
        BindType(nameOf, new Types.LiteralType(nameOf.TypeArguments?.ArgumentsList.FirstOrDefault()?.ToString() ?? nameOf.Name?.ToString()));

    public override Type VisitInvocation(Invocation invocation)
    {
        var type = Visit(invocation.Expression);
        if (IsEventType(invocation, type, strictlyConsumer: true, out _))
        {
            _diagnostics.Error(invocation, InternalCodes.InvalidInvocation, "Consumer events may only be observed, not fired.");
            return BindType(invocation, Types.PrimitiveType.Never);
        }

        if (type is Types.FunctionType functionType)
            return functionType.TypeParameters.Count == 0
                ? CheckNonGenericInvocation(invocation, functionType)
                : CheckGenericInvocation(invocation, functionType);

        if (type is Types.IntersectionType { Types.Count: > 0 } intersection && intersection.Types.TrueForAll(t => t is Types.FunctionType))
            return CheckOverloadedInvocation(invocation, intersection.Types.ConvertAll(t => (Types.FunctionType)t));

        if (IsEventType(invocation, type, strictlyConsumer: false, out var eventType))
            return CheckEventInvocation(invocation, eventType);

        _diagnostics.Error(invocation, InternalCodes.InvalidInvocation, $"Cannot call value of type '{type}'.");
        return BindType(invocation, Types.PrimitiveType.Never);
    }

    // A callee typed as an intersection of function signatures is an overload set (MergeOverloadedProperties); first candidate whose required/optional parameter count fits and whose arguments are assignable wins.
    private Type CheckOverloadedInvocation(Invocation invocation, List<Types.FunctionType> candidates)
    {
        var argumentList = invocation.Arguments.ArgumentList;
        var argumentTypes = argumentList.ConvertAll(Visit);

        var match = candidates.Find(candidate =>
        {
            var requiredCount = candidate.ParameterTypes.Count(Type.IsNotOptional);
            return argumentTypes.Count >= requiredCount
                && argumentTypes.Count <= candidate.ParameterTypes.Count
                && !argumentTypes.Where((argumentType, i) => !argumentType.IsAssignableTo(candidate.ParameterTypes[i])).Any();
        });

        if (match == null)
        {
            _diagnostics.Error(
                invocation,
                InternalCodes.NoOverloadMatch,
                $"No overload matches this call. Candidates:\n{string.Join("\n", candidates.Select(c => "  " + c))}"
            );

            return BindType(invocation, Types.PrimitiveType.Never);
        }

        return match.TypeParameters.Count == 0
            ? CheckNonGenericInvocation(invocation, match)
            : CheckGenericInvocation(invocation, match);
    }

    private Type CheckNonGenericInvocation(Invocation invocation, Types.FunctionType functionType)
    {
        var declaration = _semanticModel.GetSymbol(invocation.Expression)?.Declaration as DeclareFunctionSignature;
        var argumentList = invocation.Arguments.ArgumentList;
        var argumentTypes = BuildArgumentTypes(argumentList, functionType.ParameterTypes, functionType.HasRestParameter);

        return BindNonGenericInvocation(invocation, argumentTypes, functionType, declaration);
    }

    private Type CheckGenericInvocation(Invocation invocation, Types.FunctionType functionType)
    {
        var declaration = _semanticModel.GetSymbol(invocation.Expression)?.Declaration as DeclareFunctionSignature;
        var expectedReturnType = GetContextualType(invocation);

        return invocation.TypeArguments != null
            ? CheckExplicitGenericInvocation(invocation, functionType, declaration, expectedReturnType)
            : CheckInferredGenericInvocation(invocation, functionType, declaration, expectedReturnType);
    }

    private Type CheckExplicitGenericInvocation(Invocation invocation, Types.FunctionType functionType, DeclareFunctionSignature? declaration, Type? expectedReturnType)
    {
        var substitution = ResolveTypeArguments(invocation, functionType, [], expectedReturnType);
        if (substitution == null)
            return BindType(invocation, Types.PrimitiveType.Never);

        var substitutedParameterTypes = SubstituteTypeParameters(invocation.Arguments, functionType.ParameterTypes, substitution);
        var substitutedReturnType = SubstituteTypeParameters(invocation, functionType.ReturnType, substitution);
        var argumentList = invocation.Arguments.ArgumentList;
        var argumentTypes = BuildArgumentTypes(argumentList, substitutedParameterTypes, functionType.HasRestParameter);

        CheckArity(invocation.Arguments, declaration?.Parameters, argumentTypes, substitutedParameterTypes, functionType.HasRestParameter);
        return BindType(invocation, substitutedReturnType);
    }

    private Type CheckInferredGenericInvocation(Invocation invocation, Types.FunctionType functionType, DeclareFunctionSignature? declaration, Type? expectedReturnType)
    {
        var argumentList = invocation.Arguments.ArgumentList;
        var argumentTypes = argumentList.ConvertAll(Visit);
        var substitution = ResolveTypeArguments(invocation, functionType, argumentTypes, expectedReturnType);
        if (substitution == null)
            return BindType(invocation, Types.PrimitiveType.Never);

        var substitutedParameterTypes = SubstituteTypeParameters(invocation.Arguments, functionType.ParameterTypes, substitution);
        var substitutedReturnType = SubstituteTypeParameters(invocation, functionType.ReturnType, substitution);
        CheckArguments(invocation.Arguments, declaration?.Parameters, argumentTypes, substitutedParameterTypes, argumentList, functionType.HasRestParameter);

        return BindType(invocation, substitutedReturnType);
    }

    private Types.PrimitiveType CheckEventInvocation(Invocation invocation, InstantiatedType eventType)
    {
        var argumentList = invocation.Arguments.ArgumentList;
        var argumentTypes = argumentList.ConvertAll(Visit);
        var declaration = _semanticModel.GetSymbol(invocation.Expression)?.Declaration as EventDeclaration
            ?? _semanticModel.GetPropertySymbol(invocation.Expression)?.Declaration as EventDeclaration;

        CheckArguments(invocation.Arguments, declaration?.Parameters, argumentTypes, eventType.Arguments, argumentList);
        return BindType(invocation, Types.PrimitiveType.Void);
    }

    private List<Type> BuildArgumentTypes(List<Expression> argumentList, List<Type> parameterTypes, bool hasRestParameter = false)
    {
        var fixedCount = hasRestParameter ? parameterTypes.Count - 1 : parameterTypes.Count;
        var restElementType = GetRestElementType(parameterTypes, hasRestParameter);
        var argumentTypes = new List<Type>(argumentList.Count);
        argumentTypes.AddRange(
            argumentList.Select((t, i) => i < fixedCount
                ? Check(t, parameterTypes[i])
                : restElementType != null
                    ? Check(t, restElementType)
                    : Visit(t)
            )
        );

        return argumentTypes;
    }

    private void CheckArguments(
        Arguments arguments,
        Parameters? parameters,
        List<Type> argumentTypes,
        List<Type> parameterTypes,
        List<Expression> args,
        bool hasRestParameter = false)
    {
        CheckArity(arguments, parameters, argumentTypes, parameterTypes, hasRestParameter);
        var fixedCount = hasRestParameter ? parameterTypes.Count - 1 : parameterTypes.Count;
        var restElementType = GetRestElementType(parameterTypes, hasRestParameter);
        for (var i = 0; i < args.Count; i++)
        {
            if (i < fixedCount)
                Check(args[i], parameterTypes[i]);
            else if (restElementType != null)
                Check(args[i], restElementType);
        }
    }

    private static Type? GetRestElementType(List<Type> parameterTypes, bool hasRestParameter) =>
        hasRestParameter && parameterTypes.Count > 0 && parameterTypes[^1] is Types.ArrayType restArray ? restArray.ElementType : null;

    public override Type VisitQualifiedName(QualifiedName qualifiedName) => GetTypeOfNamedAccess(qualifiedName, qualifiedName.Identifier, qualifiedName.Names);
    public override Type VisitPropertyAccess(PropertyAccess propertyAccess) => GetTypeOfNamedAccess(propertyAccess, propertyAccess.Expression, propertyAccess.Names);

    public override Type VisitElementAccess(ElementAccess elementAccess)
    {
        if (TryGetNarrowedType(elementAccess, out var narrowedType))
            return BindType(elementAccess, narrowedType);

        var type = Visit(elementAccess.Expression);
        var indexType = Visit(elementAccess.IndexExpression);
        switch (type)
        {
            case Types.TypeParameter { Constraint: ObjectType or InterfaceType or InstantiatedType } parameter:
                return BindType(elementAccess, new Types.IndexedType(parameter, indexType));
            case Types.ArrayType when indexType.IsAssignableTo(Intrinsics.Range):
                CheckInvalidAccessAssignment(elementAccess, type, indexType);
                return BindType(elementAccess, type);
        }

        var indexIsRangeOrNumber = indexType.IsAssignableTo(Intrinsics.Range) || indexType.IsAssignableTo(Types.PrimitiveType.Number);
        if (!indexIsRangeOrNumber || !type.IsAssignableTo(Types.PrimitiveType.String))
            return IndexType(elementAccess, type, indexType, $"Cannot index value of type '{type}'.");

        CheckInvalidAccessAssignment(elementAccess, type, indexType);
        return BindType(elementAccess, Types.PrimitiveType.String);
    }

    private Type IndexType(Node node, Type type, Type indexType, string errorMessage)
    {
        if (type is InstantiatedType instantiated)
            type = instantiated.Expand();

        switch (type)
        {
            case Types.UnionType union:
            {
                var results = new List<Type>();
                foreach (var member in union.Types)
                {
                    var memberType = GetTypeAtIndexSingle(node, member, indexType);
                    if (Type.IsNever(memberType))
                    {
                        _diagnostics.Error(
                            node,
                            InternalCodes.InvalidAccess,
                            $"Indexing '{indexType}' is not valid for union member '{member}'."
                        );

                        continue;
                    }

                    if (!Type.IsUnknown(memberType))
                        results.Add(memberType);
                }

                if (results.Count == 0)
                    return BindType(node, Types.PrimitiveType.Never);

                return BindType(
                    node,
                    TypeSimplifier.Simplify(new Types.UnionType(results))
                );
            }

            case NativelyIndexableType:
                return GetTypeAtIndex(node, type, indexType);
        }

        _diagnostics.Error(node, InternalCodes.InvalidAccess, errorMessage);
        return BindType(node, Types.PrimitiveType.Never);
    }

    public override Type VisitAssignmentOperator(AssignmentOperator assignmentOperator)
    {
        if (assignmentOperator.Operator.Kind != SyntaxKind.Equals)
            return VisitBinaryOperator(assignmentOperator);

        var targetType = Visit(assignmentOperator.Left);
        var valueType = Check(assignmentOperator.Right, targetType);
        return CheckImmutableAssignmentTarget(assignmentOperator, valueType);
    }

    private Type CheckImmutableAssignmentTarget(AssignmentOperator assignmentOperator, Type valueType)
    {
        if (assignmentOperator.Left is not (ElementAccess or PropertyAccess or QualifiedName))
            return BindType(assignmentOperator, valueType);

        var expression = assignmentOperator.Left switch
        {
            ElementAccess access => access.Expression,
            PropertyAccess propertyAccess => propertyAccess.Expression,
            QualifiedName name => name.Identifier,
            _ => null!
        };

        var expressionType = _semanticModel.GetType(expression);
        var indexType = assignmentOperator.Left switch
        {
            ElementAccess access => _semanticModel.GetType(access.IndexExpression),
            PropertyAccess propertyAccess => new Types.LiteralType(propertyAccess.Names.First().Name.Text),
            QualifiedName name => new Types.LiteralType(name.Names.First().Name.Text),
            _ => null!
        };

        if (expressionType is not NativelyIndexableType indexableType)
            return BindType(assignmentOperator, valueType);

        var names = (assignmentOperator.Left switch
        {
            PropertyAccess propertyAccess => propertyAccess.Names,
            QualifiedName name => name.Names,
            _ => []
        }).ToList();

        if (names.Count > 1)
        {
            foreach (var name in names.SkipLast(1))
            {
                var property = indexableType.GetProperty(name.Name.Text);
                if (property?.ValueType is not NativelyIndexableType nestedIndexable)
                    return BindType(assignmentOperator, valueType);

                indexableType = nestedIndexable;
            }

            indexType = new Types.LiteralType(names.Last().Name.Text);
        }

        var (bodyType, _) = indexableType.GetTypeAtIndex(indexType);
        if (bodyType is not { IsMutable: false })
            return BindType(assignmentOperator, valueType);

        var display = bodyType switch
        {
            ObjectProperty property => $"property '{property.Name}'.",
            ObjectIndexer indexer => $"index '{indexer.KeyType}'.",
            _ => ""
        };

        _diagnostics.Error(assignmentOperator, InternalCodes.AssignToImmutable, $"Cannot assign to immutable {display}");

        // Dropping AddConstraint here because the Check method already does it
        // _semanticModel.TypeSolver.AddConstraint(valueType, targetType, assignmentOperator.Right);
        return BindType(assignmentOperator, valueType);
    }

    public override Type VisitTernaryOperator(TernaryOperator ternaryOperator)
    {
        var conditionType = Visit(ternaryOperator.Condition);
        _semanticModel.TypeSolver.AddConstraint(conditionType, Types.PrimitiveType.Bool, ternaryOperator.Condition);

        var (trueState, falseState) = _narrower.ComputeBranchStates(ternaryOperator.Condition, _flowState);
        var thenBranchType = Visit(ternaryOperator.ThenBranch, trueState);
        var elseBranchType = Visit(ternaryOperator.ElseBranch, falseState);
        var union = new Types.UnionType([thenBranchType, elseBranchType]);
        return BindType(ternaryOperator, TypeSimplifier.Simplify(union));
    }

    public override Type VisitBinaryOperator(BinaryOperator binaryOperator)
    {
        var leftType = Visit(binaryOperator.Left);
        Type rightType;
        switch (binaryOperator.Operator.Kind)
        {
            case SyntaxKind.AmpersandAmpersand or SyntaxKind.AmpersandAmpersandEquals:
                var (trueState, _) = _narrower.ComputeBranchStates(binaryOperator.Left, _flowState);
                rightType = Visit(binaryOperator.Right, trueState);
                break;
            case SyntaxKind.PipePipe or SyntaxKind.PipePipeEquals:
                var (_, falseState) = _narrower.ComputeBranchStates(binaryOperator.Left, _flowState);
                rightType = Visit(binaryOperator.Right, falseState);
                break;
            default:
                rightType = Visit(binaryOperator.Right);
                break;
        }

        var rule = BinaryOperatorBinder.GetRule(binaryOperator, leftType, rightType);
        if (rule != null)
        {
            _semanticModel.TypeSolver.AddConstraint(leftType, rule.LeftType, binaryOperator.Left);
            _semanticModel.TypeSolver.AddConstraint(rightType, rule.RightType, binaryOperator.Right);
            return BindType(binaryOperator, rule.ReturnType);
        }

        switch (binaryOperator.Operator.Kind)
        {
            case SyntaxKind.QuestionQuestion or SyntaxKind.QuestionQuestionEquals:
            {
                if (!Type.IsOptional(leftType))
                {
                    _diagnostics.Warn(
                        binaryOperator,
                        InternalCodes.RedundantCode,
                        $"Null coalescing has no effect since '{leftType}' is not optional."
                    );
                }

                return BindType(binaryOperator, TypeSimplifier.Simplify(new Types.UnionType([leftType, rightType]).NonNullable()));
            }
            case SyntaxKind.PlusEquals or SyntaxKind.MinusEquals
                when TryGetEventParameterTypes(binaryOperator, leftType, out var eventParameters):
            {
                var assignableFunction = new Types.FunctionType([], eventParameters, Types.PrimitiveType.Void);
                _semanticModel.TypeSolver.AddConstraint(rightType, assignableFunction, binaryOperator.Right);
                return BindType(binaryOperator, GetIntrinsicType(binaryOperator, "ScriptConnection"));
            }
        }

        var suggestion = BinaryOperatorBinder.GetSuggestion(binaryOperator, leftType, rightType);
        var hint = Diagnostic.FormatBinaryHint(binaryOperator, leftType, rightType, suggestion);
        _diagnostics.Error(
            binaryOperator,
            InternalCodes.InvalidBinaryOp,
            $"No binary operation for '{leftType.Widen()}' {binaryOperator.Operator.Text} '{rightType.Widen()}'.",
            hint
        );

        return BindType(binaryOperator, Types.PrimitiveType.Never);
    }

    public override Type VisitUnaryOperator(UnaryOperator unaryOperator)
    {
        var operandType = Visit(unaryOperator.Operand);
        var rule = UnaryOperatorBinder.GetRule(unaryOperator, operandType);
        if (rule != null)
            return rule.ReturnType;

        var suggestion = UnaryOperatorBinder.GetSuggestion(unaryOperator, operandType);
        var hint = Diagnostic.FormatUnaryHint(unaryOperator, operandType, suggestion);
        _diagnostics.Error(unaryOperator, InternalCodes.InvalidUnaryOp, $"No unary operation for {unaryOperator.Operator.Text}{operandType.Widen()}.", hint);

        return BindType(unaryOperator, Types.PrimitiveType.Never);
    }

    public override Type VisitRangeLiteral(RangeLiteral rangeLiteral)
    {
        var minimumType = Visit(rangeLiteral.Minimum);
        var maximumType = Visit(rangeLiteral.Maximum);
        _semanticModel.TypeSolver.AddConstraint(minimumType, Types.PrimitiveType.Number, rangeLiteral.Minimum);
        _semanticModel.TypeSolver.AddConstraint(maximumType, Types.PrimitiveType.Number, rangeLiteral.Maximum);

        return BindType(rangeLiteral, Intrinsics.Range);
    }

    public override Type VisitArrayLiteral(ArrayLiteral arrayLiteral)
    {
        // TODO: array literal types for immutable arrays assigned to immutable names
        var expressionTypes = arrayLiteral.Expressions.ConvertAll(Visit).ConvertAll(t => t.Widen());
        var elementType = TypeSimplifier.Simplify(new Types.UnionType(expressionTypes));
        var isMutable = arrayLiteral.MutKeyword != null;
        var type = new Types.ArrayType(elementType, isMutable);
        return BindType(arrayLiteral, type);
    }

    public override Type VisitLiteral(Literal literal) => BindType(literal, new Types.LiteralType(literal.Value));

    public override Type VisitInterpolatedStringLiteral(InterpolatedStringLiteral interpolatedStringLiteral)
    {
        foreach (var expression in interpolatedStringLiteral.Expressions)
            Visit(expression);

        return BindType(interpolatedStringLiteral, Types.PrimitiveType.String);
    }

    public override Type VisitParenthesized(Parenthesized parenthesized) => BindType(parenthesized, Visit(parenthesized.Expression));

    private bool TryGetNarrowedType(Expression expression, [MaybeNullWhen(false)] out Type narrowedType) =>
        _narrower.TryGetNarrowedType(expression, _flowState, out narrowedType);

    public override Type VisitIdentifier(Identifier identifier)
    {
        if (TryGetNarrowedType(identifier, out var narrowedType))
            return BindType(identifier, narrowedType);

        var symbol = _semanticModel.GetSymbol(identifier);
        if (symbol != null)
        {
            var isMacroReference = CheckInvocationMacroReference(identifier);

            if (isMacroReference
                && InvocationMacroReference.IsValidReferenceContext(identifier, _semanticModel)
                && GetContextualType(identifier) is Types.FunctionType contextualType)
            {
                return BindType(identifier, contextualType);
            }

            if (symbol is InjectedPropertyVariableSymbol propertyVariableSymbol)
            {
                var interfaceType = (InterfaceType)_semanticModel.GetType(propertyVariableSymbol.From.Declaration);
                return GetTypeAtIndexNative(identifier, interfaceType, new Types.LiteralType(propertyVariableSymbol.Name));
            }

            var declaredType = ResolveHoistedType(symbol);
            return BindType(identifier, declaredType);
        }

        _diagnostics.Error(identifier, InternalCodes.CannotFindSymbol, $"Cannot find symbol for declaration of variable '{identifier.Name.Text}'.");
        return BindType(identifier, Types.PrimitiveType.Never);
    }

    public override Type VisitIntersectionType(IntersectionType intersectionType) =>
        BindType(intersectionType, new Types.IntersectionType(intersectionType.Types.ConvertAll(Visit)));

    public override Type VisitUnionType(UnionType unionType) => BindType(unionType, new Types.UnionType(unionType.Types.ConvertAll(Visit)));

    public override Type VisitFunctionType(FunctionType functionType) =>
        BindType(
            functionType,
            new Types.FunctionType(
                functionType.TypeParameters?.ParameterList.ConvertAll(VisitTypeParameter) ?? [],
                functionType.Parameters?.ParameterList.ConvertAll(Visit) ?? [],
                Visit(functionType.ReturnType),
                HasRestParameter(functionType.Parameters)
            )
        );

    public override Type VisitKeyOf(KeyOf keyOf)
    {
        var targetType = Visit(keyOf.Type);
        if (targetType is InstantiatedType instantiated)
            targetType = instantiated.Expand();

        if (targetType is Types.TypeParameter { Constraint: ObjectType or InterfaceType or InstantiatedType } parameter)
        {
            targetType = parameter.Constraint!;
            if (targetType is InstantiatedType constrainedInstantiation)
                targetType = constrainedInstantiation.Expand();
        }

        if (targetType is not (ObjectType or InterfaceType))
        {
            _diagnostics.Error(keyOf, InternalCodes.InvalidKeyOf, $"Cannot access keys of type '{targetType.Widen()}'.");
            return BindType(keyOf, Types.PrimitiveType.Never);
        }

        var objectType = targetType is InterfaceType interfaceType ? interfaceType.ObjectType : (ObjectType)targetType;
        var type = objectType.KeyUnion();
        return BindType(keyOf, type);
    }

    public override Type VisitTypeOf(TypeOf typeOf) => BindType(typeOf, Visit(typeOf.Expression));

    public override Type VisitIndexedType(IndexedType indexedType)
    {
        var targetType = Visit(indexedType.TargetType);
        var indexType = Visit(indexedType.IndexType);
        if (targetType is InstantiatedType instantiated)
            targetType = instantiated.Expand();

        if (targetType is not (ObjectType or InterfaceType))
        {
            _diagnostics.Error(indexedType, InternalCodes.InvalidAccess, $"Type '{indexType}' cannot be used to index type '{targetType}'.");
            return BindType(indexedType, Types.PrimitiveType.Never);
        }

        var type = indexedType.GetDescendants<TypeName>().Any(n => _semanticModel.GetType(n) is Types.TypeParameter)
            ? new Types.IndexedType(targetType, indexType)
            : GetTypeAtIndex(indexedType, targetType, indexType);

        return BindType(indexedType, type);
    }

    public override Type VisitArrayType(ArrayType arrayType) => BindType(arrayType, new Types.ArrayType(Visit(arrayType.ElementType), arrayType.MutKeyword != null));
    public override Type VisitOptionalType(OptionalType optionalType) => BindType(optionalType, new Types.OptionalType(Visit(optionalType.NonNullableType)));
    public override Type VisitPrimitiveType(PrimitiveType primitiveType) => BindType(primitiveType, new Types.PrimitiveType(primitiveType.Kind));
    public override Type VisitLiteralType(LiteralType literalType) => BindType(literalType, new Types.LiteralType(literalType.Value));

    public override Type VisitTypeName(TypeName typeName)
    {
        var symbol = _semanticModel.GetSymbol(typeName);
        if (symbol != null)
        {
            var declaredType = ResolveHoistedType(symbol);
            if (symbol is { Kind: SymbolKind.EnumType } && declaredType is ObjectType objectType)
                return BindType(typeName, typeName.Parent is IndexedType or KeyOf ? objectType : objectType.PropertyUnion());

            if (declaredType is GenericType genericType)
                return InstantiateGenericType(typeName, typeName.TypeArguments, genericType);

            if (typeName.TypeArguments == null)
                return BindType(typeName, declaredType);

            _diagnostics.Error(typeName, InternalCodes.NotGeneric, $"Type '{typeName.Name.Text}' is not generic and cannot receive type arguments.");
            return BindType(typeName, Types.PrimitiveType.Never);
        }

        _diagnostics.Error(typeName, InternalCodes.CannotFindSymbol, $"Cannot find symbol for declaration of type '{typeName.Name.Text}'.");
        return BindType(typeName, Types.PrimitiveType.Never);
    }

    public override Types.TypeParameter VisitTypeParameter(TypeParameter typeParameter)
    {
        var defaultType = MaybeVisit(typeParameter.EqualsTypeClause);
        var constraint = MaybeVisit(typeParameter.ColonTypeClause);
        if (defaultType != null)
        {
            _semanticModel.TypeSolver.CheckCircular(ref defaultType, typeParameter.Name);
            if (constraint != null)
                _semanticModel.TypeSolver.AddConstraint(defaultType, constraint, typeParameter.EqualsTypeClause!);
        }

        var parameter = new Types.TypeParameter(typeParameter.Name.Text, constraint, defaultType);
        return BindType(typeParameter, parameter);
    }

    private Type? GetContextualType(Expression expression) =>
        expression.Parent switch
        {
            EqualsValueClause equalsValueClause when equalsValueClause.Value == expression
                && equalsValueClause.Parent is VariableDeclaration { ColonTypeClause: not null } variableDeclaration =>
                _semanticModel.GetType(variableDeclaration.ColonTypeClause.Type),

            EqualsValueClause equalsValueClause when equalsValueClause.Value == expression
                && equalsValueClause.Parent is Parameter { ColonTypeClause: not null } parameter =>
                _semanticModel.GetType(parameter.ColonTypeClause.Type),

            Return @return when @return.Expression == expression =>
                GetEnclosingDeclaredReturnType(@return),

            AssignmentOperator { Operator.Kind: SyntaxKind.Equals } assignment
                when assignment.Right == expression =>
                _semanticModel.GetType(assignment.Left),

            Arguments arguments when arguments.ArgumentList.Contains(expression)
                && arguments.Parent is Invocation invocation =>
                GetInvocationArgumentType(invocation, expression),

            _ => null
        };

    private Type? GetInvocationArgumentType(Invocation invocation, Expression argument)
    {
        var index = invocation.Arguments.ArgumentList.IndexOf(argument);
        if (index < 0 || _semanticModel.GetType(invocation.Expression) is not Types.FunctionType functionType)
            return null;

        var fixedCount = functionType.HasRestParameter ? functionType.ParameterTypes.Count - 1 : functionType.ParameterTypes.Count;
        if (index < fixedCount)
            return functionType.ParameterTypes[index];

        return GetRestElementType(functionType.ParameterTypes, functionType.HasRestParameter);
    }

    private Type? GetEnclosingDeclaredReturnType(Return @return)
    {
        var enclosingFunction = @return.FirstAncestorOfType<FunctionDeclaration>();
        return enclosingFunction?.ReturnType != null
            ? ((Types.FunctionType)_semanticModel.GetType(enclosingFunction)).ReturnType
            : null;
    }

    private Type GetTypeOfNamedAccess(Expression accessExpression, Expression targetExpression, List<DotName> names)
    {
        var type = Visit(targetExpression);
        if (TryGetNarrowedType(accessExpression, out var narrowedType))
            return BindType(accessExpression, narrowedType);

        foreach (var indexType in names.Select(name => new Types.LiteralType(name.Name.Text)))
        {
            type = IndexType(accessExpression, type, indexType, $"Cannot access property '{indexType.Value}' on type '{type}'.");
            if (Type.IsNever(type))
                return type;
        }

        var isMacroReference = CheckInvocationMacroReference(accessExpression);
        if (isMacroReference
            && InvocationMacroReference.IsValidReferenceContext(accessExpression, _semanticModel)
            && GetContextualType(accessExpression) is Types.FunctionType contextualType)
        {
            return BindType(accessExpression, contextualType);
        }

        return BindType(accessExpression, type);
    }

    private Type GetTypeAtIndex(Node node, Type type, Type indexType)
    {
        if (type is Types.UnionType union)
        {
            var results = union.Types
                .Select(member => GetTypeAtIndexSingle(node, member, indexType))
                .Where(memberResult => !Type.IsNever(memberResult) && !Type.IsUnknown(memberResult))
                .ToList();

            return results.Count == 0
                ? ReportCannotUseToIndex(node, type, indexType)
                : BindType(node, TypeSimplifier.Simplify(new Types.UnionType(results)));
        }

        if (indexType is not Types.UnionType indexUnion || !indexUnion.Types.All(t => t is Types.LiteralType { Value: string }))
            return GetTypeAtIndexSingle(node, type, indexType);

        var stringLiteralResults = indexUnion.Types
            .Select(t => GetTypeAtIndexSingle(node, type, t))
            .Where(r => !Type.IsNever(r) && !Type.IsUnknown(r))
            .ToList();

        return stringLiteralResults.Count != 0
            ? BindType(node, TypeSimplifier.Simplify(new Types.UnionType(stringLiteralResults)))
            : ReportCannotUseToIndex(node, type, indexType);
    }

    private Type GetTypeAtIndexSingle(Node node, Type type, Type indexType) =>
        type switch
        {
            NativelyIndexableType indexable => GetTypeAtIndexNative(node, indexable, indexType),
            InstantiatedType instantiated => GetTypeAtIndex(node, instantiated.Expand(), indexType),
            _ => type
        };

    private Type GetTypeAtIndexNative(Node node, NativelyIndexableType indexable, Type indexType)
    {
        var result = indexable.GetTypeAtIndex(indexType);
        var (bodyType, cannotFindReason) = result;
        return bodyType != null
            ? BindType(node, bodyType.ValueType)
            : ReportCannotUseToIndex(node, indexable, indexType, cannotFindReason);
    }

    private static Type GetObjectValueType(Type type) =>
        type switch
        {
            Types.ArrayType array => array.ElementType,
            InterfaceType interfaceType => GetObjectValueType(interfaceType.ObjectType),
            ObjectType objectType => objectType.ValueUnion(),
            _ => Types.PrimitiveType.Never
        };

    /// <summary>
    /// Reports an invalid macro-reference diagnostic if needed and returns whether
    /// <paramref name="expression"/> classifies as an invocation macro reference, so
    /// callers can avoid re-running the classification.
    /// </summary>
    private bool CheckInvocationMacroReference(Expression expression)
    {
        if (!InvocationMacroReference.TryClassify(EmptyMacroContext, expression, out _, out var memberName))
            return false;

        if (InvocationMacroReference.IsValidReferenceContext(expression, _semanticModel) || InvocationMacroReference.IsDirectInvocationCallee(expression))
            return true;

        _diagnostics.Error(
            expression,
            InternalCodes.InvalidMacroReference,
            $"Invocation macro '{memberName}' cannot be used as a value. Call it directly (e.g. {memberName}(...)) or pass it as a function argument."
        );

        return true;
    }

    private void CheckInvalidAccessAssignment(ElementAccess elementAccess, Type type, Type indexType)
    {
        if (elementAccess.Parent is not AssignmentOperator assignmentOperator) return;
        _diagnostics.Error(
            assignmentOperator,
            InternalCodes.InvalidAccess,
            $"Cannot assign to '{type.Widen()}[{indexType.Widen()}]' because the expression will be replaced by a macro."
        );
    }

    private void CheckArity(Arguments arguments, Parameters? parameters, List<Type> argumentTypes, List<Type> parameterTypes, bool hasRestParameter = false)
    {
        var fixedParameterTypes = hasRestParameter ? parameterTypes.Take(parameterTypes.Count - 1).ToList() : parameterTypes;
        var requiredParameterTypes = new List<Type>();
        if (parameters == null)
        {
            requiredParameterTypes = fixedParameterTypes.FindAll(Type.IsNotOptional);
        }
        else
        {
            var declaredCount = hasRestParameter ? parameters.ParameterList.Count - 1 : parameters.ParameterList.Count;
            var loopBound = Math.Min(declaredCount, fixedParameterTypes.Count);
            for (var i = 0; i < loopBound; i++)
            {
                var parameterType = fixedParameterTypes[i];
                var parameter = parameters.ParameterList[i];
                if (parameter.EqualsValueClause != null || !Type.IsNotOptional(parameterType)) continue;

                requiredParameterTypes.Add(parameterType);
            }
        }

        var minimum = requiredParameterTypes.Count;
        var maximum = fixedParameterTypes.Count;
        var arityDisplay = hasRestParameter
            ? $"{minimum}+"
            : minimum == maximum
                ? maximum.ToString()
                : $"{minimum}-{maximum}";

        if ((hasRestParameter || argumentTypes.Count <= maximum) && argumentTypes.Count >= minimum) return;

        var s = hasRestParameter || minimum != maximum || maximum != 1 ? "s" : "";
        _diagnostics.Error(arguments, InternalCodes.InvocationArity, $"Function expects {arityDisplay} argument{s}, but {argumentTypes.Count} were provided.");
    }

    private Type BindNonGenericInvocation(Invocation invocation, List<Type> argumentTypes, Types.FunctionType functionType, DeclareFunctionSignature? declaration)
    {
        CheckArity(invocation.Arguments, declaration?.Parameters, argumentTypes, functionType.ParameterTypes, functionType.HasRestParameter);

        // COMMENT THIS OUT BECAUSE THE CHECK ALREADY DID IT SO NO NEED TO ADD CONSTRAINTS HERE
        // AddArgumentConstraints(invocation.Arguments, argumentTypes, functionType.ParameterTypes);
        return BindType(invocation, functionType.ReturnType);
    }

    private List<Type> CheckStatements(Node node, List<Statement> statements)
    {
        var current = _flowState;
        var types = new List<Type>(statements.Count);
        foreach (var statement in statements)
        {
            types.Add(Visit(statement, current));
            current = GetStatementExitState(statement, current);
        }

        _exitStates[node] = current;
        return types;
    }

    private Type CheckBody(Statement body, FlowState current)
    {
        var type = Visit(body, current);
        if (body is Block)
            return type;

        var exit = GetStatementExitState(body, current);
        _exitStates[body] = exit;

        return type;
    }

    private FlowState GetStatementExitState(Statement statement, FlowState entryState) =>
        statement switch
        {
            Block or If or While or For or After => _exitStates.GetValueOrDefault(statement, entryState),
            Return or Break or Continue => new FlowState(entryState) { IsUnreachable = true },
            _ => entryState
        };

    private void AssignLoopExitState(Node node)
    {
        var exits = _loopExitScopes.Pop();
        var bodyExit = exits.Aggregate(_flowState, MergeExitStates);
        _exitStates[node] = bodyExit;
    }

    private static FlowState MergeExitStates(FlowState left, FlowState right) =>
        left.IsUnreachable
            ? right
            : right.IsUnreachable
                ? left
                : left.Merge(right);

    private Type GetReturnType(FunctionDeclaration functionDeclaration)
    {
        if (functionDeclaration.ReturnType != null)
            return Visit(functionDeclaration.ReturnType);

        var possibleReturnTypes = functionDeclaration.Body is ExpressionBody body
            ? [Visit(body)]
            : GetOwnReturnStatements(functionDeclaration.Body).Select(Visit).ToList();

        return possibleReturnTypes.Count == 0
            ? Types.PrimitiveType.Void
            : TypeSimplifier.Simplify(new Types.UnionType(possibleReturnTypes));
    }

    private static List<Return> GetOwnReturnStatements(Node node)
    {
        var result = new List<Return>();
        CollectOwnReturnStatements(node, result);
        return result;
    }

    private static void CollectOwnReturnStatements(Node node, List<Return> result)
    {
        foreach (var child in node.Children)
        {
            if (child is Return returnStatement)
                result.Add(returnStatement);

            if (child is not FunctionDeclaration)
                CollectOwnReturnStatements(child, result);
        }
    }

    private Type ResolveHoistedType(Symbol symbol)
    {
        var type = GetTypeFromSymbol(symbol);
        if (ReferenceEquals(symbol, _resolvingHoisted) || type is not TypeVariable)
            return type;

        var outer = _resolvingHoisted;
        _resolvingHoisted = symbol;
        type = Visit(symbol.Declaration);
        _resolvingHoisted = outer;

        return type;
    }

    private bool TryGetEventParameterTypes(Node failNode, Type type, [MaybeNullWhen(false)] out List<Type> typeArguments)
    {
        if (!IsEventType(failNode, type, strictlyConsumer: false, out var instantiated))
        {
            typeArguments = null;
            return false;
        }

        typeArguments = instantiated.Arguments.TakeWhile(Type.IsDefined).ToList();
        return true;
    }

    private bool IsEventType(Node failNode, Type type, bool strictlyConsumer, [MaybeNullWhen(false)] out InstantiatedType instantiatedType)
    {
        instantiatedType = null;
        if (type is not InstantiatedType instantiated)
            return false;

        instantiatedType = instantiated;
        var isConsumerEvent = instantiated.GenericType.Equals(GetGenericEventType(failNode, true));
        if (strictlyConsumer)
            return isConsumerEvent;

        return isConsumerEvent || instantiated.GenericType.Equals(GetGenericEventType(failNode, false));
    }

    private InstantiatedType InstantiateEventType(Node failNode, bool isConsumer, List<Type> parameterTypes)
    {
        var genericType = GetGenericEventType(failNode, isConsumer);
        var fullArguments = FillGenericArguments(genericType.Parameters, parameterTypes);
        return new InstantiatedType(genericType, fullArguments);
    }

    private GenericType GetGenericEventType(Node failNode, bool isConsumer) => GetIntrinsicType<GenericType>(failNode, isConsumer ? "ConsumerEvent" : "Event");
    private Type GetIntrinsicType(Node failNode, string name) => GetIntrinsicType<Type>(failNode, name);

    private T GetIntrinsicType<T>(Node failNode, string name) where T : Type
    {
        var symbol = _semanticModel.FindIntrinsicDeclarationSymbol<Symbol>(name);
        if (symbol != null && GetTypeFromSymbol(symbol) is T type)
            return type;

        _diagnostics.CompilerError(failNode, $"Failed to find intrinsic type for name '{name}'");
        return null!;
    }

    private Type GetTypeFromSymbol(Symbol symbol) =>
        symbol is { IsGlobal: true, IsIntrinsic: false } && symbol.File.AbsolutePath != _semanticModel.Tree.File.AbsolutePath
            ? Visit(symbol.Declaration)
            : _semanticModel.GetType(symbol.Declaration);

    private T BindType<T>(Node node, T type)
        where T : Type
    {
        _semanticModel.TypeSolver.SetType(node, type);
        if (!_semanticModel.EmitDebugDiagnostics || node is Tree or ExpressionStatement)
            return type;

        var simplified = TypeSimplifier.Simplify(type);
        _diagnostics.Debug(node, $"Solved type '{(simplified is InterfaceType i ? $"{i.ObjectType} ({i.Name})" : simplified)}' for {node.GetType().Name}");

        return type;
    }

    private Types.PrimitiveType ReportCannotUseToIndex(Node node, Type objectType, Type indexType, string? cannotFindReason = "")
    {
        _diagnostics.Error(node, InternalCodes.InvalidAccess, $"Expression of type '{indexType}' cannot be used to index type '{objectType}'.{cannotFindReason}");
        return BindType(node, Types.PrimitiveType.Never);
    }
}