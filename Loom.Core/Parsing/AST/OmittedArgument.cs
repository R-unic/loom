using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

/// <summary>
///     Stands in for a defaulted parameter a caller skipped by name, at a position an argument after it
///     still has to fill - never produced by the parser, only synthesized by <see cref="TypeChecking.TypeChecker" />
///     (and independently by <see cref="Generation.LuauGenerator" />) once a call is known to mix named
///     arguments with a gap. A trailing omission needs none of this: the call is simply shorter, exactly as
///     it is today. <paramref name="anchor" /> is the call's own closing paren, borrowed only so this node
///     has a location to report against rather than an empty one.
/// </summary>
public class OmittedArgument(Token anchor) : Expression([anchor], [])
{
    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitOmittedArgument(this);
}
