namespace Loom.Luau.AST;

public class TypeParameter(string name, LuauType? defaultType = null) : LuauNode
{
    public bool OfFunction { get; set; }
    public string Name { get; } = name;
    public LuauType? DefaultType { get; } = defaultType;

    public override string Render(RenderState state) =>
        Name
        + (OfFunction
            ? ""
            : DefaultType != null
                ? " = " + DefaultType.Render(state)
                : "");
}