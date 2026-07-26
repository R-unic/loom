using Loom.Luau.AST;

namespace Loom.Core.Generation;

/// <summary>Maps Loom operator tokens and primitive-type kinds onto their Luau AST counterparts.</summary>
internal static class LuauOperatorMap
{
    public static PrimitiveTypeKind PrimitiveTypeKind(TypeChecking.Types.PrimitiveTypeKind kind) =>
        kind switch
        {
            TypeChecking.Types.PrimitiveTypeKind.Number => Luau.AST.PrimitiveTypeKind.Number,
            TypeChecking.Types.PrimitiveTypeKind.String => Luau.AST.PrimitiveTypeKind.String,
            TypeChecking.Types.PrimitiveTypeKind.Bool => Luau.AST.PrimitiveTypeKind.Boolean,
            TypeChecking.Types.PrimitiveTypeKind.Unknown => Luau.AST.PrimitiveTypeKind.Unknown,
            TypeChecking.Types.PrimitiveTypeKind.Never => Luau.AST.PrimitiveTypeKind.Never,
            _ => Luau.AST.PrimitiveTypeKind.Nil
        };

    public static string BitwiseOperator(string op) =>
        op switch
        {
            "&" or "&=" => "band",
            "|" or "|=" => "bor",
            "~" or "~=" => "bxor",
            "<<" or "<<=" => "lshift",
            ">>>" or ">>>=" => "rshift",
            ">>" or ">>=" => "arshift",
            _ => op
        };

    public static string BinaryOperator(string op) =>
        op switch
        {
            "&&" => "and",
            "||" or "??" => "or",
            "!=" => "~=",
            _ => op
        };

    public static string UnaryOperator(string op) =>
        op switch
        {
            "!" => "not ",
            _ => op
        };
}