namespace Loom.Luau.AST;

public class PrimitiveType(PrimitiveTypeKind kind) : LuauType
{
    public static readonly PrimitiveType Number = new(PrimitiveTypeKind.Number);
    public static readonly PrimitiveType String = new(PrimitiveTypeKind.String);
    public static readonly PrimitiveType Boolean = new(PrimitiveTypeKind.Boolean);
    public static readonly PrimitiveType Never = new(PrimitiveTypeKind.Never);
    public static readonly PrimitiveType Unknown = new(PrimitiveTypeKind.Unknown);
    public static readonly PrimitiveType Any = new(PrimitiveTypeKind.Any);
    public static readonly PrimitiveType Nil = new(PrimitiveTypeKind.Nil);

    public PrimitiveTypeKind Kind { get; } = kind;

    public override string Render(RenderState state) => Kind.ToString().ToLower();
}