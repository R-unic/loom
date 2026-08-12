using Loom.Core.Diagnostics;
using Loom.Core.Generation.Macros;
using Loom.Core.Parsing.AST;
using Loom.Core.TypeChecking.Types;

namespace Loom.Core.TypeChecking;

using Type = Types.Type;

public sealed partial class TypeChecker
{
    public override Type VisitQualifiedName(QualifiedName qualifiedName)
    {
        var type = GetTypeOfNamedAccess(qualifiedName, qualifiedName.Identifier, qualifiedName.Names);
        CheckMemberAccess(qualifiedName);

        return type;
    }

    public override Type VisitPropertyAccess(PropertyAccess propertyAccess)
    {
        var type = GetTypeOfNamedAccess(propertyAccess, propertyAccess.Expression, propertyAccess.Names);
        CheckMemberAccess(propertyAccess);

        return type;
    }

    public override Type VisitElementAccess(ElementAccess elementAccess)
    {
        if (TryGetNarrowedType(elementAccess, out var narrowedType))
            return BindType(elementAccess, narrowedType);

        var type = Visit(elementAccess.Expression);
        var isOptionalChain = elementAccess.IsOptional;
        if (elementAccess.IsOptional)
        {
            type = type.NonNullable();
        }
        else if (Type.IsOptional(type))
        {
            _diagnostics.Error(elementAccess, InternalCodes.PossiblyNoneAccess, $"'{type}' is possibly 'none'. Use '?[' to index a value that might be 'none'.");
            isOptionalChain = true;
            type = type.NonNullable();
        }

        var indexType = Visit(elementAccess.IndexExpression);
        var result = GetElementAccessType(elementAccess, type, indexType);
        if (isOptionalChain && !Type.IsNever(result))
            result = TypeSimplifier.Simplify(new Types.UnionType([result, Types.PrimitiveType.None]));

        return BindType(elementAccess, result);
    }

    private Type GetElementAccessType(ElementAccess elementAccess, Type type, Type indexType)
    {
        switch (type)
        {
            case Types.TypeParameter { Constraint: ObjectType or InterfaceType or InstantiatedType } parameter:
                return new Types.IndexedType(parameter, indexType);
            case Types.ArrayType when indexType.IsAssignableTo(Intrinsics.Range):
                CheckInvalidAccessAssignment(elementAccess, type, indexType);
                return type;
            case Types.TupleType tupleType when indexType is Types.LiteralType { Value: long or int }:
                return GetTupleElementAccessType(elementAccess, tupleType, indexType);
        }

        var indexIsRangeOrNumber = indexType.IsAssignableTo(Intrinsics.Range) || indexType.IsAssignableTo(Types.PrimitiveType.Number);
        if (!indexIsRangeOrNumber || !type.IsAssignableTo(Types.PrimitiveType.String))
            return IndexType(elementAccess, type, indexType, $"Cannot index value of type '{type}'.");

        CheckInvalidAccessAssignment(elementAccess, type, indexType);
        return Types.PrimitiveType.String;
    }

    private Type GetTupleElementAccessType(ElementAccess elementAccess, Types.TupleType tupleType, Type indexType)
    {
        var index = Convert.ToInt32(((Types.LiteralType)indexType).Value);
        if (index < 1 || index > tupleType.ElementTypes.Count)
        {
            _diagnostics.Error(
                elementAccess,
                InternalCodes.TupleIndexOutOfRange,
                $"Index {index} is out of range for tuple type '{tupleType}' with {tupleType.ElementTypes.Count} element(s)."
            );

            return Types.PrimitiveType.Never;
        }

        return tupleType.ElementTypes[index - 1];
    }

    private Type IndexType(Node node, Type type, Type indexType, string errorMessage)
    {
        if (IsUnawaitedFutureMember(node, type, indexType))
            return ReportCannotUseToIndex(node, type, indexType);

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

            case Types.PrimitiveType { Kind: PrimitiveTypeKind.String }:
                return GetTypeAtIndex(node, Intrinsics.StringMembers, indexType);
        }

        _diagnostics.Error(node, InternalCodes.InvalidAccess, errorMessage);
        return BindType(node, Types.PrimitiveType.Never);
    }

    private Type GetTypeOfNamedAccess(Expression accessExpression, Expression targetExpression, List<DotName> names)
    {
        var type = Visit(targetExpression);
        if (TryGetNarrowedType(accessExpression, out var narrowedType))
            return BindType(accessExpression, narrowedType);

        var isOptionalChain = false;
        foreach (var name in names)
        {
            if (name.IsOptional)
            {
                isOptionalChain = true;
                type = type.NonNullable();
            }
            else if (Type.IsOptional(type))
            {
                _diagnostics.Error(accessExpression, InternalCodes.PossiblyNoneAccess, $"'{type}' is possibly 'none'. Use '?.' to access '{name.Name.Text}'.");
                isOptionalChain = true;
                type = type.NonNullable();
            }

            var indexType = new Types.LiteralType(name.Name.Text);

            // an awaited chain of yielding calls resolves each future on the way past, so one 'await' covers
            // the chain; anything else reading a member off a future still takes the parenthesised form
            if (!name.IsOptional && TryReadThroughFuture(accessExpression, type, names.Count, out var settled))
                type = settled;

            type = IndexType(accessExpression, type, indexType, $"Cannot access property '{indexType.Value}' on type '{type}'.");
            if (Type.IsNever(type))
                return type;
        }

        if (isOptionalChain)
            type = TypeSimplifier.Simplify(new Types.UnionType([type, Types.PrimitiveType.None]));

        var isMacroReference = CheckInvocationMacroReference(accessExpression);
        if (isMacroReference
            && InvocationMacroReference.IsValidReferenceContext(accessExpression, _semanticModel)
            && GetContextualType(accessExpression) is Types.FunctionType contextualType)
            return BindType(accessExpression, contextualType);

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

    /// <summary>
    ///     Whether reading <paramref name="indexType" /> off <paramref name="type" /> is the common mistake
    ///     of reaching for a member of the awaited value on the future itself. Asked before the expansion
    ///     in <see cref="IndexType" />, since that is the last point a future is recognisable as one - past
    ///     it there is only an interface, which no failed lookup can tell apart from any other. A member
    ///     the future genuinely has ('status', 'value') is not one of these and reads normally.
    /// </summary>
    private bool IsUnawaitedFutureMember(Node node, Type type, Type indexType) =>
        indexType is Types.LiteralType { Value: string }
        && IsFutureType(node, type)
        && TypeSimplifier.Expanded(type) is NativelyIndexableType future
        && future.GetTypeAtIndex(indexType).BodyType == null;

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

    private void CheckInvalidAccessAssignment(ElementAccess elementAccess, Type type, Type indexType)
    {
        if (elementAccess.Parent is not AssignmentOperator assignmentOperator) return;
        _diagnostics.Error(
            assignmentOperator,
            InternalCodes.InvalidAccess,
            $"Cannot assign to '{type.Widen()}[{indexType.Widen()}]' because the expression will be replaced by a macro."
        );
    }

    private Types.PrimitiveType ReportCannotUseToIndex(Node node, Type objectType, Type indexType, string? cannotFindReason = "")
    {
        // reading a member off a future nobody awaited is the common shape of this failure. An awaited chain
        // of yielding *calls* reads through (TryReadThroughFuture), so what is left here is a field read,
        // which takes the parenthesised form - 'await' takes the whole postfix chain, as it does in JS.
        if (indexType is Types.LiteralType { Value: string member } && IsFutureType(node, objectType))
        {
            _diagnostics.Error(
                node,
                InternalCodes.UnawaitedFutureAccess,
                $"Cannot access property '{member}' on type '{objectType}' - it belongs to the awaited value, not to the future.",
                $"write '(await ...).{member}', or await the call if '{member}' is the next step of a chain"
            );

            return BindType(node, Types.PrimitiveType.Never);
        }

        _diagnostics.Error(node, InternalCodes.InvalidAccess, $"Expression of type '{indexType}' cannot be used to index type '{objectType}'.{cannotFindReason}");
        return BindType(node, Types.PrimitiveType.Never);
    }
}
