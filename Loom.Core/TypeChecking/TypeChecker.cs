using System.Diagnostics.CodeAnalysis;
using Loom.Core.Diagnostics;
using Loom.Core.FlowAnalysis;
using Loom.Core.Generation.Macros;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Core.Resolving.Symbols;
using Loom.Core.TypeChecking.Types;
using Attribute = Loom.Core.Parsing.AST.Attribute;

namespace Loom.Core.TypeChecking;

using Type = Types.Type;

/// <summary>
///     Fifth stage of the compiler pipeline (Lexer -&gt; Parser -&gt; Resolver -&gt; FlowAnalyzer -&gt; TypeChecker
///     -&gt; LuauGenerator): infers and checks types for the whole tree, entered via <see cref="Check" />.
///     Split across partial files by concern - <c>.Invocations</c> (calling functions/overloads/generics),
///     <c>.MemberAccess</c> (property/element/index lookups), <c>.ControlFlow</c> (loops/if/return and their
///     exit-state bookkeeping), <c>.Declarations</c> (functions/variables/type aliases/imports),
///     <c>.TypeNodes</c> (visiting type-expression syntax into <see cref="Types.Type" />s),
///     <c>.Operators</c> (assignment/binary/unary/ternary), and <c>.ErrorPropagation</c> (the '?' operator).
///     This file keeps the entry point, per-node dispatch, literals, and the small set of methods every
///     partial relies on (<see cref="BindType{T}" />, event-type helpers).
/// </summary>
public sealed partial class TypeChecker
    : Visitor<Type>
{
    private readonly DiagnosticBag _diagnostics;
    private readonly Dictionary<Node, FlowState> _exitStates = [];
    private readonly FlowAnalyzer _flowAnalyzer;
    private readonly TypeInferrer _inferrer;
    private readonly Stack<List<FlowState>> _loopExitScopes = [];
    private readonly TypeNarrower _narrower;
    private readonly HashSet<Symbol> _resolvingHoisted = [];
    private readonly SemanticModel _semanticModel;
    private FlowState _flowState;

    public TypeChecker(SemanticModel semanticModel, FlowAnalyzer flowAnalyzer)
        : base(_ => Types.PrimitiveType.Never)
    {
        _semanticModel = semanticModel;
        _diagnostics = new DiagnosticBag(options: semanticModel.Diagnostics.Options);
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
        CheckSerializableInterfaces();

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

        if (!symbol.IsAmbient && eventDeclaration.Attributes != null)
            foreach (var attribute in eventDeclaration.Attributes.AttributeList)
            {
                CheckPassiveDecorator(attribute);
                CheckAttributeUsage(attribute, AttributeTargetsFlag.Event);
            }

        return BindType(eventDeclaration, type);
    }

    public override Type VisitAttribute(Attribute attribute)
    {
        var expressionType = Visit(attribute.Expression);
        if (expressionType is not Types.FunctionType functionType)
        {
            _diagnostics.Error(attribute, InternalCodes.NonFunctionAttribute, "Only functions may be used as attributes.");
            return BindType(attribute, Types.PrimitiveType.Never);
        }

        if (!attribute.IsInvoked)
            return BindType(attribute, functionType);

        return functionType.TypeParameters.Count == 0
            ? CheckNonGenericInvocation(attribute, functionType)
            : CheckGenericInvocation(attribute, functionType);
    }

    public override Type VisitAs(As @as)
    {
        var expressionType = Visit(@as.Expression);
        var castedType = TypeSimplifier.Simplify(Visit(@as.Type));
        if (Type.IsNotUnknown(expressionType) && Type.IsNotNever(castedType) && Type.IsNotUnknown(castedType))
            _semanticModel.TypeSolver.AddConstraint(expressionType, castedType, @as);

        return BindType(@as, castedType);
    }

    public override Type VisitNullForgiving(NullForgiving nullForgiving)
    {
        var expressionType = Visit(nullForgiving.Expression);
        if (!Type.IsOptional(expressionType))
            _diagnostics.Warn(
                nullForgiving,
                InternalCodes.RedundantCode,
                $"Null-forgiving operator has no effect since '{expressionType}' is not optional."
            );
        
        return BindType(nullForgiving, expressionType.NonNullable());
    }

    public override Type VisitIs(Is @is)
    {
        var expressionType = Visit(@is.Expression);
        CheckPattern(@is.Pattern, expressionType);
        return BindType(@is, Types.PrimitiveType.Bool);
    }

    public override Type VisitNameOf(NameOf nameOf) =>
        BindType(nameOf, new Types.LiteralType(nameOf.TypeArguments?.ArgumentsList.FirstOrDefault()?.ToString() ?? nameOf.Name?.ToString()));

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

    public override Type VisitTupleExpression(TupleExpression tupleExpression) =>
        BindType(tupleExpression, new Types.TupleType(tupleExpression.Expressions.ConvertAll(Visit)));

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
                return BindType(identifier, contextualType);

            if (symbol is InjectedPropertyVariableSymbol propertyVariableSymbol)
            {
                var interfaceType = (InterfaceType)_semanticModel.GetType(propertyVariableSymbol.From.Declaration);
                return GetTypeAtIndexNative(identifier, interfaceType, new Types.LiteralType(propertyVariableSymbol.Name));
            }

            var declaredType = ResolveHoistedType(symbol);
            return BindType(identifier, declaredType);
        }

        // a name the resolver already reported as unbound has no symbol by definition - its error is the one
        // the user acts on, and repeating it here phrased as a failed symbol lookup only reads like a compiler bug
        if (!_semanticModel.IsUnresolved(identifier))
            _diagnostics.Error(identifier, InternalCodes.CannotFindSymbol, $"Cannot find symbol for declaration of variable '{identifier.Name.Text}'.");

        return BindType(identifier, Types.PrimitiveType.Never);
    }

    private bool TryGetEventParameterTypes(Node failNode, Type type, [MaybeNullWhen(false)] out List<Type> typeArguments)
    {
        if (!IsEventType(failNode, type, false, out var instantiated))
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

    private T BindType<T>(Node node, T type)
        where T : Type
    {
        _semanticModel.TypeSolver.SetType(node, type);
        return type;
    }
}
