namespace Loom.Luau.AST;

public class ParenthesizedType(LuauType type) : LuauType
{
    public LuauType Type { get; } = type;

    public override string Render(RenderState state) => $"({Type.Render(state)})";
}