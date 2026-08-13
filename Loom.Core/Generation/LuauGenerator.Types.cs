using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving.Symbols;
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
        "RobloxError",
        "Range",
        "Event",
        "ConsumerEvent",
        "Future",
        "FutureStatus",
        "CreatableInstance",
        "ServiceInstance",
        "Serialized",
        "DeserializeError",
        "DeserializeErrorKind",
        "Serializer"
    ];

    public override LuauNode VisitTypeName(TypeName typeName)
    {
        var symbol = _semanticModel.GetSymbol(typeName);
        if (symbol == null)
        {
            if (!_semanticModel.IsUnresolved(typeName))
                _diagnostics.Error(typeName, InternalCodes.CannotFindSymbol, $"Cannot find symbol for type '{typeName}'");

            return UnknownType;
        }

        // A branching type has no Luau alias to name - what it works out to goes here instead. A mapped one
        // does (see GenerateMappedTypeAlias), so it keeps its name and Luau's own 'keyof'/'index' do the work.
        if (NamesConditionalAlias(symbol))
            return RenderResolvedType(typeName);

        // 'Array<T, L>' is transparent sugar for 'T[]' - L only matters to the serializer, which reads
        // it straight off the type argument, so it never has anything to reach Luau with. Nothing named
        // 'Array' exists there, unlike 'T[]' itself.
        if (IsSerializationOnlyArrayAlias(symbol) && typeName.TypeArguments?.ArgumentsList is [var elementType, ..])
            return TableType.Array(Visit(elementType));

        var typeArguments = typeName.TypeArguments?.ArgumentsList?.ConvertAll(Visit);

        // Vector3/Vector2/CFrame's width parameter is the same kind of Loom-only metadata - Roblox's
        // real datatype has no type parameter, so an explicit 'Vector3<i16>' still has to reach Luau as
        // plain 'Vector3'.
        if (IsPhantomSizedDatatype(symbol))
            typeArguments = null;

        var luauTypeName = new Luau.AST.TypeName(typeName.Name.Text, typeArguments);
        if (IsLoomRuntimeType(symbol))
            return LuauFactory.QualifyRuntimeType(luauTypeName);

        var constraint = symbol.Declaration is TypeParameter { ColonTypeClause: { } clause } ? Visit(clause) : null;
        return constraint != null ? new Luau.AST.IntersectionType([luauTypeName, constraint]) : luauTypeName;
    }

    private bool NamesConditionalAlias(Symbol symbol) =>
        symbol.Declaration is Parsing.AST.TypeAlias
        && _semanticModel.GetType(symbol.Declaration) is TypeChecking.Types.ConditionalType or TypeChecking.Types.GenericType { UnderlyingType: TypeChecking.Types.ConditionalType };

    private static bool IsLoomRuntimeType(Symbol symbol) =>
        symbol is { IsIntrinsic: true, File.Name: "runtime.loom" or "None.loom" or "PluginSecurity.loom" } && _loomRuntimeTypeNames.Contains(symbol.Name);

    private static bool IsSerializationOnlyArrayAlias(Symbol symbol) => symbol is { IsIntrinsic: true, File.Name: "loom.loom", Name: "Array" };

    private static bool IsPhantomSizedDatatype(Symbol symbol) =>
        symbol is { IsIntrinsic: true, File.Name: "None.loom" or "PluginSecurity.loom", Name: "Vector3" or "Vector2" or "CFrame" };

    public override LuauNode VisitFunctionType(FunctionType functionType) =>
        new Luau.AST.FunctionType(
            MaybeVisit<Luau.AST.TypeParameters>(functionType.TypeParameters),
            functionType.Parameters?.ParameterList.ConvertAll(p => Visit(p.ColonTypeClause!)) ?? [],
            Visit(functionType.ReturnType)
        );

    public override LuauNode VisitConditionalType(Parsing.AST.ConditionalType conditionalType) => RenderResolvedType(conditionalType);
    public override LuauNode VisitTypeMatch(TypeMatch typeMatch) => RenderResolvedType(typeMatch);
    public override LuauNode VisitWildcardType(WildcardType wildcardType) => UnknownType;
    public override LuauNode VisitInferType(InferType inferType) => new Luau.AST.TypeName(inferType.Name.Text);

    /// <summary>
    ///     What a branching type worked out to, written out in its place. Luau can express the answer but
    ///     not the question, so a use whose subject was concrete emits the answer directly and needs no type
    ///     function at all.
    /// </summary>
    /// <remarks>
    ///     Where the subject is still a type parameter at emission there is no answer yet, and the fallback
    ///     is <c>unknown</c> - not <c>any</c>, which would silence every check downstream of it, where
    ///     <c>unknown</c> makes the consumer narrow. It is warned about rather than done quietly, since what
    ///     falls through here is precision the program was written expecting.
    /// </remarks>
    private LuauNode RenderResolvedType(TypeExpression node)
    {
        if (LuauTypeRenderer.Render(_semanticModel.GetType(node), _loomRuntimeTypeNames) is { } rendered)
            return rendered;

        // Inside a generic type's own declaration there is nothing to say: it is written over a parameter
        // by definition, and its uses are where the precision is either kept or lost. Reporting it here
        // would fire on every such declaration and name no use the author could do anything about.
        if (!node.IsDescendantOf<Parsing.AST.TypeAlias>() && !node.IsDescendantOf<InterfaceDeclaration>())
            _diagnostics.Warn(
                node,
                InternalCodes.UnresolvedTypeInOutput,
                $"'{_semanticModel.GetType(node)}' is still generic here, so it is emitted as 'unknown'.",
                "instantiate it with concrete type arguments to keep the precise type"
            );

        return UnknownType;
    }

    public override LuauNode VisitTypeOf(TypeOf typeOf) => new TypeOfType(Visit(typeOf.Expression));
    public override LuauNode VisitTypePredicateType(TypePredicateType typePredicateType) => Luau.AST.PrimitiveType.Boolean;
    public override LuauNode VisitIntersectionType(IntersectionType intersectionType) => new Luau.AST.IntersectionType(intersectionType.Types.ConvertAll(Visit));
    public override LuauNode VisitUnionType(UnionType unionType) => new Luau.AST.UnionType(unionType.Types.ConvertAll(Visit));
    public override LuauNode VisitArrayType(ArrayType arrayType) => TableType.Array(Visit(arrayType.ElementType));

    public override LuauNode VisitTupleType(Parsing.AST.TupleType tupleType) =>
        tupleType.Parent is ColonTypeClause { Parent: DeclareFunctionSignature or FunctionType }
            ? new Luau.AST.TupleType(tupleType.Types.ConvertAll(Visit))
            : TableType.Array(new Luau.AST.UnionType(tupleType.Types.ConvertAll(Visit)));
    public override LuauNode VisitOptionalType(OptionalType optionalType) => new Luau.AST.OptionalType(Visit(optionalType.NonNullableType));
    public override LuauNode VisitParenthesizedType(ParenthesizedType parenthesized) => new Luau.AST.ParenthesizedType(Visit(parenthesized.Type));
    public override LuauNode VisitKeyOf(KeyOf keyOf) => new Luau.AST.TypeName("keyof", [Visit(keyOf.Type)]);

    public override LuauNode VisitIndexedType(IndexedType indexedType) =>
        IndexesEnumType(indexedType) && _semanticModel.GetType(indexedType) is TypeChecking.Types.LiteralType literal && LiteralTypeOf(literal.Value) is { } expanded
            ? expanded
            : new Luau.AST.TypeName("index", [Visit(indexedType.TargetType), Visit(indexedType.IndexType)]);

    private bool IndexesEnumType(IndexedType indexedType) =>
        indexedType.TargetType is TypeName targetName && _semanticModel.GetSymbol(targetName, SymbolKind.EnumType) != null;

    public override LuauNode VisitTypeParameters(TypeParameters typeParameters) =>
        new Luau.AST.TypeParameters(typeParameters.ParameterList.ConvertAll(VisitTypeParameter));

    public override Luau.AST.TypeParameter VisitTypeParameter(TypeParameter typeParameter) =>
        new(typeParameter.Name.Text, typeParameter.EqualsTypeClause != null ? Visit(typeParameter.EqualsTypeClause.Type) : null);

    public override LuauNode VisitPrimitiveType(PrimitiveType primitiveType) =>
        primitiveType is { Kind: PrimitiveTypeKind.Void or PrimitiveTypeKind.None, Parent: ColonTypeClause { Parent: DeclareFunctionSignature or FunctionType } }
            ? new UnitType()
            : new Luau.AST.PrimitiveType(LuauOperatorMap.PrimitiveTypeKind(primitiveType.Kind));

    public override LuauNode VisitLiteralType(LiteralType literalType) =>
        LiteralTypeOf(literalType.Value)
        ?? (literalType.Parent is ColonTypeClause { Parent: DeclareFunctionSignature or FunctionType }
            ? new UnitType()
            : Luau.AST.PrimitiveType.Nil);

    private static LuauType? LiteralTypeOf(object? value) =>
        value switch
        {
            long or int or double => Luau.AST.PrimitiveType.Number,
            bool b => new BooleanLiteralType(b),
            string s => new StringLiteralType(s),
            _ => null
        };
}