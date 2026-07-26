using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Core.Resolving.Symbols;
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
}
