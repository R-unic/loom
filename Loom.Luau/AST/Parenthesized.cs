namespace Loom.Luau.AST;

public class Parenthesized(LuauExpression expression) : LuauExpression
{
    public LuauExpression Expression { get; } = expression;

    public override string Render(RenderState state) => $"({Expression.Render(state)})";
}