namespace Loom.Luau.AST;

public record ElseIfBranch(LuauExpression Condition, Chunk Branch);

public class IfStatement(LuauExpression condition, Chunk thenBranch, List<ElseIfBranch> elseIfBranches, Chunk? elseBranch) : LuauStatement
{
    private readonly bool _isSimple = thenBranch.IsSimple
        && elseIfBranches is { Count: 0 } or [{ Branch.Statements: [Continue or Break or Return { Expression: null }] }]
        && elseBranch is { IsSimple: true } or null;
    public LuauExpression Condition { get; } = condition;
    public Chunk ThenBranch { get; } = thenBranch;
    public List<ElseIfBranch> ElseIfBranches { get; } = elseIfBranches;
    public Chunk? ElseBranch { get; } = elseBranch;

    private string NewLine => _isSimple ? "" : "\n";
    private string Spacer => _isSimple ? " " : "";

    public override string Render(RenderState state) =>
        $"if {Condition.Render(state)} then{NewLine}{Spacer}"
        + RenderBranch(state, ThenBranch)
        + string.Join(
            Spacer,
            ElseIfBranches.ConvertAll(elseIf => $"{Spacer}elseif {elseIf.Condition.Render(state)} then{NewLine}{Spacer}" + RenderBranch(state, elseIf.Branch))
        )
        + (ElseBranch != null ? $"{Spacer}else{NewLine}{Spacer}" + RenderBranch(state, ElseBranch) : "")
        + $"{Spacer}end";

    private string RenderBranch(RenderState state, Chunk branch)
    {
        var render = branch.Render(state);
        return _isSimple ? render.Trim() : state.Indented(render);
    }
}