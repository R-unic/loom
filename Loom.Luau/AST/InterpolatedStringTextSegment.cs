namespace Loom.Luau.AST;

public class InterpolatedStringTextSegment(string value) : InterpolatedStringSegment
{
    public string Value { get; } = value;

    public override string Render(RenderState state) => Value.Replace("`", "\\`");
}