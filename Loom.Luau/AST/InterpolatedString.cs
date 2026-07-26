namespace Loom.Luau.AST;

public class InterpolatedString(List<InterpolatedStringSegment> segments) : LuauExpression
{
    public List<InterpolatedStringSegment> Segments { get; } = segments;

    public override string Render(RenderState state) => "`" + string.Concat(Segments.ConvertAll(s => s.Render(state))) + "`";
}