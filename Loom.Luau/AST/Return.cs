namespace Loom.Luau.AST;

public class Return(LuauExpression? expression = null) : LuauStatement
{
    public LuauExpression? Expression { get; } = expression;

    public override string Render(RenderState state) => Expression == null ? "return" : $"return {Expression.Render(state)}";
}