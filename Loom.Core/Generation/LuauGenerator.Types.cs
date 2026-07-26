using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Luau;
using Loom.Luau.AST;
using ArrayType = Loom.Core.Parsing.AST.ArrayType;
using FunctionType = Loom.Core.Parsing.AST.FunctionType;
using IndexedType = Loom.Core.Parsing.AST.IndexedType;
using IntersectionType = Loom.Core.Parsing.AST.IntersectionType;
using LiteralType = Loom.Core.Parsing.AST.LiteralType;
using OptionalType = Loom.Core.Parsing.AST.OptionalType;
using ParenthesizedType = Loom.Core.Parsing.AST.ParenthesizedType;
using PrimitiveType = Loom.Core.Parsing.AST.PrimitiveType;
using PrimitiveTypeKind = Loom.Core.TypeChecking.Types.PrimitiveTypeKind;
using TypeName = Loom.Core.Parsing.AST.TypeName;
using TypeParameter = Loom.Core.Parsing.AST.TypeParameter;
using TypeParameters = Loom.Core.Parsing.AST.TypeParameters;
using UnionType = Loom.Core.Parsing.AST.UnionType;

namespace Loom.Core.Generation;

public sealed partial class LuauGenerator
{
    private static readonly HashSet<string> _loomRuntimeTypeNames =
    [
        "ResultOk",
        "ResultError",
        "Result",
        "Range",
        "Event",
        "ConsumerEvent",
        "CreatableInstance",
        "ServiceInstance"
    ];

    public override LuauNode VisitTypeName(TypeName typeName)
    {
        var symbol = _semanticModel.GetSymbol(typeName);
        if (symbol == null)
        {
            _diagnostics.Error(typeName, InternalCodes.CannotFindSymbol, $"Cannot find symbol for type '{typeName}'");
            return new NilLiteral();
        }

        var typeArguments = typeName.TypeArguments?.ArgumentsList.ConvertAll(Visit);
        var luauTypeName = new Luau.AST.TypeName(typeName.Name.Text, typeArguments);
        if (IsLoomRuntimeType(symbol))
            return LuauFactory.QualifyRuntimeType(luauTypeName);

        var constraint = symbol.Declaration is TypeParameter { ColonTypeClause: { } clause } ? Visit(clause) : null;
        return constraint != null ? new Luau.AST.IntersectionType([luauTypeName, constraint]) : luauTypeName;
    }

    private static bool IsLoomRuntimeType(Symbol symbol) =>
        symbol is { IsIntrinsic: true, File.Name: "runtime.loom" or "None.loom" or "PluginSecurity.loom" } && _loomRuntimeTypeNames.Contains(symbol.Name);

    public override LuauNode VisitFunctionType(FunctionType functionType) =>
        new Luau.AST.FunctionType(
            MaybeVisit<Luau.AST.TypeParameters>(functionType.TypeParameters),
            functionType.Parameters?.ParameterList.ConvertAll(p => Visit(p.ColonTypeClause!)) ?? [],
            Visit(functionType.ReturnType)
        );

    public override LuauNode VisitTypeOf(TypeOf typeOf) => new TypeOfType(Visit(typeOf.Expression));
    public override LuauNode VisitIntersectionType(IntersectionType intersectionType) => new Luau.AST.IntersectionType(intersectionType.Types.ConvertAll(Visit));
    public override LuauNode VisitUnionType(UnionType unionType) => new Luau.AST.UnionType(unionType.Types.ConvertAll(Visit));
    public override LuauNode VisitArrayType(ArrayType arrayType) => TableType.Array(Visit(arrayType.ElementType));
    public override LuauNode VisitOptionalType(OptionalType optionalType) => new Luau.AST.OptionalType(Visit(optionalType.NonNullableType));
    public override LuauNode VisitParenthesizedType(ParenthesizedType parenthesized) => new Luau.AST.ParenthesizedType(Visit(parenthesized.Type));
    public override LuauNode VisitKeyOf(KeyOf keyOf) => new Luau.AST.TypeName("keyof", [Visit(keyOf.Type)]);

    public override LuauNode VisitIndexedType(IndexedType indexedType) =>
        new Luau.AST.TypeName("index", [Visit(indexedType.TargetType), Visit(indexedType.IndexType)]);

    public override LuauNode VisitTypeParameters(TypeParameters typeParameters) =>
        new Luau.AST.TypeParameters(typeParameters.ParameterList.ConvertAll(VisitTypeParameter));

    public override Luau.AST.TypeParameter VisitTypeParameter(TypeParameter typeParameter) =>
        new(typeParameter.Name.Text, typeParameter.EqualsTypeClause != null ? Visit(typeParameter.EqualsTypeClause.Type) : null);

    public override LuauNode VisitPrimitiveType(PrimitiveType primitiveType) =>
        primitiveType is { Kind: PrimitiveTypeKind.Void or PrimitiveTypeKind.None, Parent: ColonTypeClause { Parent: DeclareFunctionSignature or FunctionType } }
            ? new UnitType()
            : new Luau.AST.PrimitiveType(LuauOperatorMap.PrimitiveTypeKind(primitiveType.Kind));

    public override LuauNode VisitLiteralType(LiteralType literalType) =>
        literalType.Value switch
        {
            long or int or double => Luau.AST.PrimitiveType.Number,
            bool b => new BooleanLiteralType(b),
            string s => new StringLiteralType(s),
            _ when literalType.Parent is ColonTypeClause { Parent: DeclareFunctionSignature or FunctionType } => new UnitType(),
            _ => Luau.AST.PrimitiveType.Nil
        };
}