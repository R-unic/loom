namespace Loom.Luau.AST;

public class TableTypeProperty(LuauVisibility? visibility, string name, LuauType type) : LuauType
{
    public LuauVisibility? Visibility { get; } = visibility;
    public string Name { get; } = name;
    public LuauType Type { get; } = type;

    public override string Render(RenderState state) => $"{RenderState.RenderVisibility(Visibility)}{Name}: {Type.Render(state)}";
}