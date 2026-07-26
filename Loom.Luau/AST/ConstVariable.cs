namespace Loom.Luau.AST;

public class ConstVariable(string name, LuauType? declaredType, LuauExpression initializer)
    : Variable(name, declaredType)
{
    public LuauExpression Initializer { get; } = initializer;

    public override string Render(RenderState state) => $"const {Name}{RenderType(state)} = {Initializer.Render(state)}";
}