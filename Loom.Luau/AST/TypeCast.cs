namespace Loom.Luau.AST;

public class TypeCast(LuauExpression expression, LuauType type) : LuauExpression
{
    public LuauExpression Expression { get; } = expression;
    public LuauType Type { get; } = type;

    public override string Render(RenderState state) => $"{state.ParenthesizeIfNeeded(Expression)} :: {Type.Render(state)}";
}