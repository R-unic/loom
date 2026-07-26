namespace Loom.Luau.AST;

public class LocalVariable(string name, LuauType? declaredType, LuauExpression? initializer)
    : Variable(name, declaredType)
{
    public LuauExpression? Initializer { get; } = initializer;

    public override string Render(RenderState state) => "local " + Name + RenderType(state) + (Initializer != null ? " = " + Initializer.Render(state) : "");
}