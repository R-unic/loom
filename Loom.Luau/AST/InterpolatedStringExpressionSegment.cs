namespace Loom.Luau.AST;

public class InterpolatedStringExpressionSegment(LuauExpression expression) : InterpolatedStringSegment
{
    public LuauExpression Expression { get; } = expression;

    public override string Render(RenderState state) => "{" + state.ParenthesizeIfNeeded(Expression) + "}";
}
