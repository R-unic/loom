namespace Loom.Luau.AST;

public class ComputedPropertyTableInitializer(LuauExpression key, LuauExpression value)
    : TableInitializer(value)
{
    public LuauExpression Key { get; } = key;

    public override string Render(RenderState state) => $"[{Key.Render(state)}] = {Value.Render(state)}";
}