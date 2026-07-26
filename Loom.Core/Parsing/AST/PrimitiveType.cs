using Loom.Core.Text;
using Loom.Core.TypeChecking.Types;

namespace Loom.Core.Parsing.AST;

public class PrimitiveType(Token name)
    : TypeExpression([name], [])
{
    public PrimitiveTypeKind Kind { get; } = name.Text switch
    {
        "number" => PrimitiveTypeKind.Number,
        "string" => PrimitiveTypeKind.String,
        "bool" => PrimitiveTypeKind.Bool,
        "never" => PrimitiveTypeKind.Never,
        "unknown" => PrimitiveTypeKind.Unknown,
        "void" => PrimitiveTypeKind.Void,
        "none" => PrimitiveTypeKind.None,
        _ => PrimitiveTypeKind.Unknown
    };

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitPrimitiveType(this);
}