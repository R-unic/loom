using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving.Symbols;
using Loom.Core.TypeChecking.Serialization;
using Loom.Core.TypeChecking.Types;
using ArrayType = Loom.Core.Parsing.AST.ArrayType;
using FunctionType = Loom.Core.Parsing.AST.FunctionType;
using IndexedType = Loom.Core.Parsing.AST.IndexedType;
using IntersectionType = Loom.Core.Parsing.AST.IntersectionType;
using LiteralType = Loom.Core.Parsing.AST.LiteralType;
using OptionalType = Loom.Core.Parsing.AST.OptionalType;
using PrimitiveType = Loom.Core.Parsing.AST.PrimitiveType;
using TypeName = Loom.Core.Parsing.AST.TypeName;
using TypeParameter = Loom.Core.Parsing.AST.TypeParameter;
using TypePredicateType = Loom.Core.Parsing.AST.TypePredicateType;
using UnionType = Loom.Core.Parsing.AST.UnionType;

namespace Loom.Core.TypeChecking;

using Type = Types.Type;
using Loom.Core.TypeChecking.Solving;
using Loom.Core.TypeChecking.Intrinsic;

public sealed partial class TypeChecker
{
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
                HasRestParameter(functionType.Parameters),
                functionType.AsyncKeyword != null
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

        // Deferred rather than rejected, the same way VisitIndexedType defers 'T[K]': what an unconstrained
        // parameter stands for is known only once the generic is instantiated, and SubstituteKeyOfType
        // resolves it there. Answering 'never' here made every 'keyof(T)' over a bare parameter empty.
        if (targetType is Types.TypeParameter or TypeVariable or Types.IndexedType or KeyOfType)
            return BindType(keyOf, new KeyOfType(targetType));

        if (targetType is not (ObjectType or InterfaceType))
        {
            _diagnostics.Error(keyOf, InternalCodes.InvalidKeyOf, $"Cannot access keys of type '{targetType.Widen()}'.");
            return BindType(keyOf, Types.PrimitiveType.Never);
        }

        // The interface itself, not its own ObjectType: unwrapping it drops every base it inherits from, and
        // an interface merged from single-key constraints keeps all of its keys there and none of its own.
        var type = ((NativelyIndexableType)targetType).KeyUnion();
        return BindType(keyOf, type);
    }

    public override Type VisitTypeOf(TypeOf typeOf) => BindType(typeOf, Visit(typeOf.Expression));

    public override Type VisitTypePredicateType(TypePredicateType typePredicateType)
    {
        Visit(typePredicateType.Subject);
        int? parameterIndex = null;
        if (typePredicateType.Subject is Identifier subjectIdentifier)
        {
            var symbol = _semanticModel.GetSymbol(subjectIdentifier);
            var parameters = typePredicateType.FirstAncestorOfType<DeclareFunctionSignature>()?.Parameters?.ParameterList;
            var index = parameters?.FindIndex(p => _semanticModel.GetDeclarationSymbol(p, SymbolKind.Parameter) == symbol) ?? -1;
            if (index < 0)
            {
                _diagnostics.Error(
                    typePredicateType,
                    InternalCodes.InvalidTypePredicateSubject,
                    "Type predicate subject must be a parameter of the enclosing function."
                );

                return BindType(typePredicateType, Types.PrimitiveType.Bool);
            }

            parameterIndex = index;
        }

        var targetType = Visit(typePredicateType.Type);
        return BindType(typePredicateType, new Types.TypePredicateType(parameterIndex, targetType));
    }

    public override Type VisitIndexedType(IndexedType indexedType)
    {
        var targetType = Visit(indexedType.TargetType);
        var indexType = Visit(indexedType.IndexType);
        if (targetType is InstantiatedType instantiated)
            targetType = instantiated.Expand();

        // Ahead of the guard below, because a type parameter is neither an object nor an interface and is
        // not meant to be one yet - what it stands for is known only once the generic is instantiated, and
        // resolving the index is SubstituteIndexedType's to do there. Answering 'never' here instead made
        // 'fn pick<T, K>(key: K): T[K]' return never for every call, which is assignable to whatever the
        // caller annotated and so said nothing.
        if (indexedType.EnumerateDescendants<TypeName>().Any(n => _semanticModel.GetType(n) is Types.TypeParameter))
            return BindType(indexedType, new Types.IndexedType(targetType, indexType));

        if (targetType is not (ObjectType or InterfaceType))
        {
            _diagnostics.Error(indexedType, InternalCodes.InvalidAccess, $"Type '{indexType}' cannot be used to index type '{targetType}'.");
            return BindType(indexedType, Types.PrimitiveType.Never);
        }

        return BindType(indexedType, GetTypeAtIndex(indexedType, targetType, indexType));
    }

    public override Type VisitArrayType(ArrayType arrayType) => BindType(arrayType, new Types.ArrayType(Visit(arrayType.ElementType), arrayType.MutKeyword != null));
    public override Type VisitTupleType(Parsing.AST.TupleType tupleType) => BindType(tupleType, new Types.TupleType(tupleType.Types.ConvertAll(Visit)));
    public override Type VisitOptionalType(OptionalType optionalType) => BindType(optionalType, new Types.OptionalType(Visit(optionalType.NonNullableType)));

    public override Type VisitPrimitiveType(PrimitiveType primitiveType)
    {
        if (primitiveType.Width is { } width)
            return BindType(primitiveType, new SizedNumberType(width));

        return BindType(primitiveType, primitiveType.TypeArguments != null ? VisitSizedStringType(primitiveType) : new Types.PrimitiveType(primitiveType.Kind));
    }

    /// <summary>
    ///     'string' is the only primitive a type argument ever reaches the type checker for - the parser
    ///     deliberately never parses one for any other primitive (see <c>Parser.Types.cs</c>), but the
    ///     'is'/match-pattern parse paths aren't restricted the same way, so this still has to reject one
    ///     turning up on, say, 'number&lt;u8&gt;' rather than assume it can only be 'string'.
    /// </summary>
    private Type VisitSizedStringType(PrimitiveType primitiveType)
    {
        if (primitiveType.Kind != PrimitiveTypeKind.String)
        {
            _diagnostics.Error(primitiveType, InternalCodes.InvalidTypeArguments, $"'{primitiveType.Kind.ToString().ToLower()}' does not take a type argument.");
            return new Types.PrimitiveType(primitiveType.Kind);
        }

        if (primitiveType.TypeArguments!.ArgumentsList is not [var argument])
        {
            _diagnostics.Error(primitiveType, InternalCodes.InvalidTypeArguments, "'string' takes exactly one type argument, its length-prefix width.");
            return Types.PrimitiveType.String;
        }

        var argumentType = Visit(argument);
        if (argumentType is not SizedNumberType sized)
        {
            _diagnostics.Error(argument, InternalCodes.InvalidTypeArguments, $"string's length type must be a sized type like 'u8', but is '{argumentType}'.");
            return Types.PrimitiveType.String;
        }

        if (!sized.NumberType.IsUnsigned())
        {
            _diagnostics.Error(
                argument,
                InternalCodes.InvalidTypeArguments,
                $"string's length type must be unsigned, but is '{sized.NumberType}'.",
                "lengths are never negative; use u8, u16, or u32."
            );

            return Types.PrimitiveType.String;
        }

        return new SizedStringType(sized.NumberType);
    }

    public override Type VisitLiteralType(LiteralType literalType) => BindType(literalType, new Types.LiteralType(literalType.Value));

    public override Type VisitTypeName(TypeName typeName)
    {
        var symbol = _semanticModel.GetSymbol(typeName);
        if (symbol != null)
        {
            if (IsTupleMarkerSymbol(symbol))
                return BindType(typeName, IntrinsicTypes.TupleMarker);

            var declaredType = GetHoistedType(symbol);
            if (symbol is { Kind: SymbolKind.EnumType } && declaredType is ObjectType objectType)
                return BindType(typeName, typeName.Parent is IndexedType or KeyOf ? objectType : objectType.PropertyUnion());

            if (declaredType is GenericType genericType)
                return InstantiateGenericType(typeName, typeName.TypeArguments, genericType);

            if (typeName.TypeArguments == null)
                return BindType(typeName, declaredType);

            _diagnostics.Error(typeName, InternalCodes.NotGeneric, $"Type '{typeName.Name.Text}' is not generic and cannot receive type arguments.");
            return BindType(typeName, Types.PrimitiveType.Never);
        }

        if (!_semanticModel.IsUnresolved(typeName))
            _diagnostics.Error(typeName, InternalCodes.CannotFindSymbol, $"Cannot find symbol for declaration of type '{typeName.Name.Text}'.");

        return BindType(typeName, Types.PrimitiveType.Never);
    }

    private static bool IsTupleMarkerSymbol(Symbol symbol) => symbol is { IsIntrinsic: true, Name: "Tuple", File.Name: "loom.loom" };

    public override Type VisitWildcardType(WildcardType wildcardType) => BindType(wildcardType, Types.PrimitiveType.Unknown);

    /// <summary>
    ///     The binder a <c>let</c> in a type pattern stands for. Bound to its own declaration node so a use
    ///     of the name inside the arm resolves to this very instance - which is what
    ///     <see cref="ConditionalArm.Binders" /> matching by reference relies on.
    /// </summary>
    public override Type VisitInferType(InferType inferType)
    {
        if (_semanticModel.GetType(inferType) is Types.TypeParameter existing)
            return existing;

        var constraint = MaybeVisit(inferType.ColonTypeClause);
        return BindType(inferType, new Types.TypeParameter(inferType.Name.Text, constraint));
    }

    public override Type VisitConditionalType(Parsing.AST.ConditionalType conditionalType)
    {
        var subject = Visit(conditionalType.CheckType);
        var thenArm = BuildConditionalArm(conditionalType.TargetType, conditionalType.ThenType);

        // The 'else' is the same arm list with a pattern nothing fails - which is what makes the two-armed
        // form and the n-armed one the same type, rather than two that have to be evaluated separately.
        var elseArm = new ConditionalArm(Types.PrimitiveType.Unknown, Visit(conditionalType.ElseType), []);
        return BindType(conditionalType, Resolve(new Types.ConditionalType(subject, [thenArm, elseArm], false)));
    }

    public override Type VisitTypeMatch(TypeMatch typeMatch)
    {
        var subject = Visit(typeMatch.Subject);
        var arms = typeMatch.Arms.ConvertAll(arm => BuildConditionalArm(arm.Pattern, arm.Result));
        return BindType(typeMatch, Resolve(new Types.ConditionalType(subject, arms, typeMatch.EachKeyword != null)));
    }

    private static Type Resolve(Types.ConditionalType conditional) => ConditionalTypeEvaluator.TryEvaluate(conditional) ?? conditional;

    /// <summary>
    ///     The binders are visited ahead of the pattern so the arm's result can name one: a use resolves
    ///     through the symbol to the <see cref="InferType" /> declaration's bound type, which has to exist
    ///     by then.
    /// </summary>
    private ConditionalArm BuildConditionalArm(TypeExpression pattern, TypeExpression result)
    {
        var binders = pattern.EnumerateSelfAndDescendants<InferType>()
            .Select(Visit)
            .OfType<Types.TypeParameter>()
            .ToList();

        return new ConditionalArm(Visit(pattern), Visit(result), binders);
    }

    /// <summary>
    ///     A mapped type's binder stands for one key at a time, so it is a type parameter like any other -
    ///     bound to the declaration node the resolver declared its name against, which is how <c>T[K]</c> in
    ///     the member type reaches it.
    /// </summary>
    public override Types.TypeParameter VisitMappedTypeDeclaration(MappedTypeDeclaration mappedTypeDeclaration) =>
        _semanticModel.GetType(mappedTypeDeclaration) as Types.TypeParameter
        ?? BindType(mappedTypeDeclaration, new Types.TypeParameter(mappedTypeDeclaration.Name.Text));

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
}
