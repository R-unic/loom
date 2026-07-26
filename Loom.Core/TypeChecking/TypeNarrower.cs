using System.Diagnostics.CodeAnalysis;
using Loom.Core.FlowAnalysis;
using Loom.Core.Parsing;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Core.Text;
using LiteralType = Loom.Core.TypeChecking.Types.LiteralType;
using PrimitiveType = Loom.Core.TypeChecking.Types.PrimitiveType;
using Type = Loom.Core.TypeChecking.Types.Type;
using UnionType = Loom.Core.TypeChecking.Types.UnionType;

namespace Loom.Core.TypeChecking;

public sealed class TypeNarrower
{
    private readonly SemanticModel _semanticModel;

    private readonly Literal _trueLiteral = new(TokenFactory.Keyword(SyntaxKind.TrueLiteral), true);

    public TypeNarrower(SemanticModel semanticModel)
    {
        _semanticModel = semanticModel;
        _semanticModel.TypeSolver.SetType(_trueLiteral, new LiteralType(true));
    }

    public bool TryGetNarrowedType(Expression expression, FlowState current, [MaybeNullWhen(false)] out Type narrowedType)
    {
        if (GetFlowAddress(expression) is { } address && current.NarrowedTypes.TryGetValue(address, out var narrowed))
        {
            narrowedType = narrowed;
            _semanticModel.TypeSolver.SetType(expression, narrowed);
            return true;
        }

        if (TryResolveViaNarrowedPrefix(expression, current) is { } resolved)
        {
            narrowedType = resolved;
            _semanticModel.TypeSolver.SetType(expression, resolved);
            return true;
        }

        narrowedType = null;
        return false;
    }

    public BranchStates ComputeBranchStates(Expression condition, FlowState current) =>
        condition switch
        {
            BinaryOperator { Operator.Kind: SyntaxKind.EqualsEquals or SyntaxKind.BangEquals } binary => NarrowEquality(binary, current),
            BinaryOperator { Operator.Kind: SyntaxKind.AmpersandAmpersand or SyntaxKind.AmpersandAmpersandEquals } and => NarrowLogicalAnd(and, current),
            BinaryOperator { Operator.Kind: SyntaxKind.PipePipe or SyntaxKind.PipePipeEquals } or => NarrowLogicalOr(or, current),
            UnaryOperator { Operator.Kind: SyntaxKind.Bang } not => NarrowLogicalNot(not, current),
            Parenthesized p => ComputeBranchStates(p.Expression, current),
            _ => NarrowBooleanCondition(condition, current)
        };

    private BranchStates NarrowBooleanCondition(Expression expression, FlowState current)
    {
        var type = GetBaseExpressionType(expression, current);
        if (type == null || !type.IsAssignableTo(PrimitiveType.Bool))
            return new BranchStates(current, current);

        var trueBuilder = current.ToBuilder();
        var falseBuilder = current.ToBuilder();
        ApplyBinaryNarrowing(
            expression,
            _trueLiteral,
            SyntaxKind.EqualsEquals,
            current,
            trueBuilder,
            falseBuilder
        );

        return new BranchStates(trueBuilder.ToImmutable(), falseBuilder.ToImmutable());
    }

    private BranchStates NarrowEquality(BinaryOperator binaryOperator, FlowState current)
    {
        if (!TryGetExpressionAndLiteral(binaryOperator.Left, binaryOperator.Right, out var expression, out var literal)
            && !TryGetExpressionAndLiteral(binaryOperator.Right, binaryOperator.Left, out expression, out literal))
            return new BranchStates(current, current);

        var trueBuilder = current.ToBuilder();
        var falseBuilder = current.ToBuilder();
        ApplyBinaryNarrowing(
            expression,
            literal,
            binaryOperator.Operator.Kind,
            current,
            trueBuilder,
            falseBuilder
        );

        return new BranchStates(trueBuilder.ToImmutable(), falseBuilder.ToImmutable());
    }

    private BranchStates NarrowLogicalAnd(BinaryOperator andOp, FlowState current)
    {
        var (leftTrue, leftFalse) = ComputeBranchStates(andOp.Left, current);
        var (rightTrue, _) = ComputeBranchStates(andOp.Right, leftTrue);
        var falseState = MergeStates(leftFalse, ApplyBranchState(andOp.Right, leftTrue, false));
        return new BranchStates(rightTrue, falseState);
    }

    private BranchStates NarrowLogicalOr(BinaryOperator orOp, FlowState current)
    {
        var (leftTrue, leftFalse) = ComputeBranchStates(orOp.Left, current);
        var (_, rightFalse) = ComputeBranchStates(orOp.Right, leftFalse);
        var trueState = MergeStates(leftTrue, ApplyBranchState(orOp.Right, leftFalse, true));
        return new BranchStates(trueState, rightFalse);
    }

    private BranchStates NarrowLogicalNot(UnaryOperator notOp, FlowState current)
    {
        var (trueState, falseState) = ComputeBranchStates(notOp.Operand, current);
        return new BranchStates(falseState, trueState);
    }

    private FlowState ApplyBranchState(Expression expr, FlowState state, bool useTrue = false)
    {
        var (trueState, falseState) = ComputeBranchStates(expr, state);
        return useTrue ? trueState : falseState;
    }

    private static FlowState MergeStates(FlowState a, FlowState b)
    {
        if (a.NarrowedTypes.IsEmpty && b.NarrowedTypes.IsEmpty)
            return a;

        var builder = a.NarrowedTypes.ToBuilder();
        foreach (var key in a.NarrowedTypes.Keys.Concat(b.NarrowedTypes.Keys).Distinct())
        {
            var aType = ResolveEffectiveType(key, a);
            var bType = ResolveEffectiveType(key, b);

            if (aType != null && bType != null)
                builder[key] = TypeSimplifier.Simplify(new UnionType([aType, bType]));
            else
                builder.Remove(key);
        }

        return new FlowState(a.DefinitelyInitialized, a.MaybeInitialized, a.IsUnreachable, builder.ToImmutable());
    }

    private static Type? ResolveEffectiveType(FlowAddress address, FlowState state)
    {
        if (state.NarrowedTypes.TryGetValue(address, out var direct))
            return direct;

        return address switch
        {
            { Parent: not null, FieldName: not null } => ResolveEffectiveType(address.Parent, state) is { } parentType
                ? TypeSimplifier.GetMemberPropertyType(parentType, address.FieldName)
                : null,
            { Parent: not null, ElementIndex: not null } => ResolveEffectiveType(address.Parent, state) is { } parentType
                ? TypeSimplifier.GetMemberElementType(parentType, new LiteralType(address.ElementIndex))
                : null,
            _ => null
        };
    }

    private bool TryGetExpressionAndLiteral(
        Expression expr1,
        Expression expr2,
        [MaybeNullWhen(false)] out Expression expression,
        [MaybeNullWhen(false)] out Expression literal)
    {
        if (!_semanticModel.IsCompileTimeConstant(expr2))
        {
            expression = null;
            literal = null;
            return false;
        }

        expression = expr1;
        literal = expr2;
        return true;
    }

    private void ApplyBinaryNarrowing(
        Expression expression,
        Expression literal,
        SyntaxKind operatorKind,
        FlowState currentState,
        FlowState.Builder trueState,
        FlowState.Builder falseState)
    {
        var address = GetFlowAddress(expression);
        if (address == null) return;

        var baseType = _semanticModel.GetType(expression);
        var literalType = _semanticModel.GetType(literal);
        var isNone = literal is Literal { Value: null };
        var isEquals = operatorKind == SyntaxKind.EqualsEquals;
        switch (expression)
        {
            case PropertyAccess propertyAccess:
            {
                var propertyNames = propertyAccess.Names.ConvertAll(n => n.Name.Text);
                NarrowBaseByProperty(
                    propertyAccess.Expression,
                    literal,
                    propertyNames,
                    literalType,
                    isEquals,
                    currentState,
                    trueState,
                    falseState
                );

                break;
            }
            case QualifiedName qualifiedName:
            {
                var propertyNames = qualifiedName.Names.ConvertAll(n => n.Name.Text);
                NarrowBaseByProperty(
                    qualifiedName.Identifier,
                    literal,
                    propertyNames,
                    literalType,
                    isEquals,
                    currentState,
                    trueState,
                    falseState
                );

                break;
            }
            case ElementAccess { IndexExpression: Literal { Value: not null and not bool } } elementAccess:
            {
                var indexLiteralType = _semanticModel.GetType(elementAccess.IndexExpression);
                NarrowBaseByElement(
                    elementAccess.Expression,
                    indexLiteralType,
                    literalType,
                    isEquals,
                    currentState,
                    trueState,
                    falseState
                );

                break;
            }
        }

        var otherType = isNone ? baseType.NonNullable() : RemoveType(baseType, literalType);
        (trueState.NarrowedTypes[address], falseState.NarrowedTypes[address]) = AssignNarrowed(isEquals, literalType, otherType);
    }

    private static (Type True, Type False) AssignNarrowed(bool isEquals, Type whenEqual, Type whenNotEqual) =>
        isEquals ? (whenEqual, whenNotEqual) : (whenNotEqual, whenEqual);

    private void NarrowBaseByProperty(
        Expression baseExpression,
        Expression literalExpression,
        List<string> propertyPath,
        Type literalType,
        bool isEquals,
        FlowState currentState,
        FlowState.Builder trueState,
        FlowState.Builder falseState)
    {
        var baseAddress = GetFlowAddress(baseExpression);
        if (baseAddress == null) return;

        var baseType = GetBaseExpressionType(baseExpression, currentState);
        if (baseType == null) return;

        var unionAddress = baseAddress;
        var currentType = baseType;
        var pathIndex = 0;
        while (currentType is not UnionType && pathIndex < propertyPath.Count)
        {
            var name = propertyPath[pathIndex];
            var nextAddress = FlowAddress.Field(unionAddress, name);
            currentType = currentState.NarrowedTypes.TryGetValue(nextAddress, out var narrowedStep)
                ? narrowedStep
                : TypeSimplifier.GetMemberPropertyType(currentType, name);

            if (currentType == null) return;
            unionAddress = nextAddress;
            pathIndex++;
        }

        if (currentType is not UnionType union) return;

        var remainingPath = propertyPath.Skip(pathIndex).ToList();
        var constantValue = _semanticModel.GetConstantValue(literalExpression);
        var trueMembers = new List<Type>();
        var falseMembers = new List<Type>();
        foreach (var member in union.Types)
        {
            var propertyType = GetTypeAtPath(member, remainingPath);
            if (propertyType == null) continue;

            var matches = constantValue != null && propertyType is LiteralType propertyLiteral
                ? Equals(propertyLiteral.Value, constantValue)
                : propertyType.IsAssignableTo(literalType) && literalType.IsAssignableTo(propertyType);

            if (matches)
                trueMembers.Add(member);
            else
                falseMembers.Add(member);
        }

        var trueBaseType = TypeSimplifier.Simplify(BuildUnionOrNever(trueMembers));
        var falseBaseType = TypeSimplifier.Simplify(BuildUnionOrNever(falseMembers));
        (trueState.NarrowedTypes[unionAddress], falseState.NarrowedTypes[unionAddress]) = AssignNarrowed(isEquals, trueBaseType, falseBaseType);
    }

    private void NarrowBaseByElement(
        Expression baseExpression,
        Type indexType,
        Type literalType,
        bool isEquals,
        FlowState currentState,
        FlowState.Builder trueState,
        FlowState.Builder falseState)
    {
        var baseAddress = GetFlowAddress(baseExpression);
        if (baseAddress == null) return;

        var baseType = GetBaseExpressionType(baseExpression, currentState);
        if (baseType is not UnionType union) return;

        var trueMembers = new List<Type>();
        var falseMembers = new List<Type>();
        foreach (var member in union.Types)
        {
            var elementType = TypeSimplifier.GetMemberElementType(member, indexType);
            if (elementType == null) continue;

            if (elementType.IsAssignableTo(literalType) && literalType.IsAssignableTo(elementType))
                trueMembers.Add(member);
            else
                falseMembers.Add(member);
        }

        var trueBaseType = TypeSimplifier.Simplify(BuildUnionOrNever(trueMembers));
        var falseBaseType = TypeSimplifier.Simplify(BuildUnionOrNever(falseMembers));
        (trueState.NarrowedTypes[baseAddress], falseState.NarrowedTypes[baseAddress]) = AssignNarrowed(isEquals, trueBaseType, falseBaseType);
    }

    private Type? GetBaseExpressionType(Expression expression, FlowState currentState)
    {
        if (TryGetNarrowedType(expression, currentState, out var narrowed))
            return narrowed;

        switch (expression)
        {
            case Identifier:
                return _semanticModel.GetDeclarationType(expression) ?? _semanticModel.GetType(expression);

            case PropertyAccess property:
            {
                var parent = GetBaseExpressionType(property.Expression, currentState);
                return parent == null
                    ? null
                    : GetTypeAtPath(parent, property.Names.ConvertAll(n => n.Name.Text));
            }

            case QualifiedName qualified:
            {
                var parent = GetBaseExpressionType(qualified.Identifier, currentState);
                return parent == null
                    ? null
                    : GetTypeAtPath(parent, qualified.Names.ConvertAll(n => n.Name.Text));
            }

            case ElementAccess { IndexExpression: Literal { Value: not (null or bool) } } element:
            {
                var parent = GetBaseExpressionType(element.Expression, currentState);
                if (parent == null)
                    return null;

                var indexType = _semanticModel.GetType(element.IndexExpression);
                return TypeSimplifier.GetMemberElementType(parent, indexType);
            }

            default:
                return _semanticModel.GetType(expression);
        }
    }

    private Type? TryResolveViaNarrowedPrefix(Expression expression, FlowState current)
    {
        Expression baseExpression;
        List<string> path;
        switch (expression)
        {
            case QualifiedName qualifiedName:
                baseExpression = qualifiedName.Identifier;
                path = qualifiedName.Names.ConvertAll(n => n.Name.Text);
                break;
            case PropertyAccess propertyAccess:
                baseExpression = propertyAccess.Expression;
                path = propertyAccess.Names.ConvertAll(n => n.Name.Text);
                break;
            default:
                return null;
        }

        if (GetFlowAddress(baseExpression) is not { } address)
            return null;

        var narrowedBase = current.NarrowedTypes.GetValueOrDefault(address);
        var narrowedIndex = narrowedBase != null ? 0 : -1;
        for (var i = 0; i < path.Count; i++)
        {
            address = FlowAddress.Field(address, path[i]);
            if (!current.NarrowedTypes.TryGetValue(address, out var narrowed)) continue;

            narrowedBase = narrowed;
            narrowedIndex = i + 1;
        }

        if (narrowedIndex < 0 || narrowedBase == null)
            return null;

        var remainingPath = path.Skip(narrowedIndex).ToList();
        return GetTypeAtPath(narrowedBase, remainingPath);
    }

    private static Type? GetTypeAtPath(Type type, List<string> path)
    {
        var final = type;
        foreach (var part in path)
        {
            final = TypeSimplifier.GetMemberPropertyType(final, part);
            if (final == null)
                return null;
        }

        return final;
    }

    private static Type BuildUnionOrNever(List<Type> types) =>
        types.Count switch
        {
            0 => PrimitiveType.Never,
            1 => types.First(),
            _ => new UnionType(types)
        };

    private static Type RemoveType(Type source, Type toRemove)
    {
        if (source.Equals(toRemove))
            return PrimitiveType.Never;

        if (source.Equals(PrimitiveType.Bool) && toRemove is LiteralType { Value: bool value })
            return new LiteralType(!value);

        if (source is not UnionType union)
            return source;

        var remaining = union.Types.Where(t => !toRemove.IsAssignableTo(t)).ToList();
        return remaining.Count switch
        {
            0 => PrimitiveType.Never,
            1 => remaining.First(),
            _ => new UnionType(remaining)
        };
    }

    private FlowAddress? GetFlowAddress(Expression expr) =>
        expr switch
        {
            Identifier identifier => GetIdentifierFlowAddress(identifier),
            QualifiedName qualifiedName => BuildFieldChain(qualifiedName.Identifier, qualifiedName.Names),
            PropertyAccess propertyAccess => BuildFieldChain(propertyAccess.Expression, propertyAccess.Names),
            ElementAccess elementAccess => GetElementAddress(elementAccess),
            _ => null
        };

    private FlowAddress? BuildFieldChain(Expression baseExpr, List<DotName> dotNames)
    {
        var address = GetFlowAddress(baseExpr);
        return address == null
            ? null
            : dotNames.Select(name => name.Name.Text).Aggregate(address, FlowAddress.Field);
    }

    private FlowAddress? GetElementAddress(ElementAccess elementAccess)
    {
        if (GetFlowAddress(elementAccess.Expression) is not { } baseAddress)
            return null;

        if (elementAccess.IndexExpression is Literal { Value: not null and not bool } literal)
            return FlowAddress.Element(baseAddress, literal.Value);

        return null;
    }

    private FlowAddress? GetIdentifierFlowAddress(Identifier identifier)
    {
        var symbol = _semanticModel.GetSymbol(identifier);
        return symbol != null ? FlowAddress.Variable(symbol) : null;
    }

    public sealed record BranchStates(FlowState True, FlowState False);
}