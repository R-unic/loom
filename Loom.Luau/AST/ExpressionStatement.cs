namespace Loom.Luau.AST;

public sealed class ExpressionStatement(LuauExpression expression) : LuauStatement
{
    public LuauExpression Expression { get; } = expression;

    public override string Render(RenderState state) => Expression.Render(state);
}