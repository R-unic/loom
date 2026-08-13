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

        if (!_semanticModel.IsUnresolved(typeName))
            _diagnostics.Error(typeName, InternalCodes.CannotFindSymbol, $"Cannot find symbol for declaration of type '{typeName.Name.Text}'.");

        return BindType(typeName, Types.PrimitiveType.Never);
    }

    private static bool IsTupleMarkerSymbol(Symbol symbol) => symbol is { IsIntrinsic: true, Name: "Tuple", File.Name: "loom.loom" };

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
