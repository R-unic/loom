namespace Loom.Luau.AST;

public record ElseIfExpressionBranch(LuauExpression Condition, LuauExpression Branch);

public class IfExpression(
    LuauExpression condition,
    LuauExpression thenBranch,
    List<ElseIfExpressionBranch> elseIfBranches,
    LuauExpression? elseBranch
) : LuauExpression
{
    public LuauExpression Condition { get; } = condition;
    public LuauExpression ThenBranch { get; } = thenBranch;
    public List<ElseIfExpressionBranch> ElseIfBranches { get; } = elseIfBranches;
    public LuauExpression? ElseBranch { get; } = elseBranch;

    public override string Render(RenderState state) =>
        $"if {Condition.Render(state)} then "
        + state.Indented(ThenBranch.Render(state))
        + string.Join("", ElseIfBranches.ConvertAll(elseIf => $" elseif {elseIf.Condition.Render(state)} then " + state.Indented(elseIf.Branch.Render(state))))
        + (ElseBranch != null ? " else " + state.Indented(ElseBranch.Render(state)) : "");
}