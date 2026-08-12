using Loom.Core.Text;
using Loom.Core.TypeChecking.Serialization;
using Loom.Core.TypeChecking.Types;

namespace Loom.Core.Parsing.AST;

public class PrimitiveType(Token name, TypeArguments? typeArguments = null)
    : TypeExpression([name], [typeArguments])
{
    /// <summary>The argument list on a length-widened <c>string&lt;u8&gt;</c>, or <c>null</c> for every other primitive (including a bare <c>string</c>).</summary>
    public TypeArguments? TypeArguments { get; } = typeArguments;

    public PrimitiveTypeKind Kind { get; } = name.Text switch
    {
        "number" => PrimitiveTypeKind.Number,
        "string" => PrimitiveTypeKind.String,
        "bool" => PrimitiveTypeKind.Bool,
        "never" => PrimitiveTypeKind.Never,
        "unknown" => PrimitiveTypeKind.Unknown,
        "void" => PrimitiveTypeKind.Void,
        "none" => PrimitiveTypeKind.None,
        "u8" or "u16" or "u32" or "i8" or "i16" or "i32" or "f32" or "f64" => PrimitiveTypeKind.Number,
        _ => PrimitiveTypeKind.Unknown
    };

    /// <summary>The wire width named by <c>u8</c>/<c>i16</c>/etc., or <c>null</c> for a plain <c>number</c> (or any other primitive).</summary>
    public NumberType? Width { get; } = name.Text switch
    {
        "u8" => NumberType.U8,
        "u16" => NumberType.U16,
        "u32" => NumberType.U32,
        "i8" => NumberType.I8,
        "i16" => NumberType.I16,
        "i32" => NumberType.I32,
        "f32" => NumberType.F32,
        "f64" => NumberType.F64,
        _ => null
    };

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitPrimitiveType(this);
}