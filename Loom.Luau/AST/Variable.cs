namespace Loom.Luau.AST;

public abstract class Variable(string name, LuauType? declaredType) : LuauStatement
{
    public string Name { get; } = name;
    public LuauType? DeclaredType { get; } = declaredType;

    protected string RenderType(RenderState state) => DeclaredType != null ? ": " + DeclaredType.Render(state) : "";
}