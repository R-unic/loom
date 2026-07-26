namespace Loom.Luau.AST;

public class TableInitializer(LuauExpression value) : LuauExpression
{
    public LuauExpression Value { get; } = value;

    public override string Render(RenderState state) => Value.Render(state);
}