namespace Loom.Luau.AST;

/// <summary>
///     An array expanded into the argument list around it. Renders as the <c>table.unpack</c> it is, but
///     stays a node of its own so a macro building something other than a call out of its arguments can
///     tell one from an ordinary argument rather than having to recognise the emitted call.
/// </summary>
public class Spread(LuauExpression operand) : LuauExpression
{
    public LuauExpression Operand { get; } = operand;

    public override string Render(RenderState state) => LuauFactory.TableCall("unpack", [Operand]).Render(state);
}
