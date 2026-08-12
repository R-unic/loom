using Loom.Luau.AST;

namespace Loom.Core.Generation.Macros;

/// <summary>
///     Answers whether a macro may read its operand more than once.
/// </summary>
/// <remarks>
///     <para>
///         A macro that expands one Loom expression into several Luau ones has to decide what to do with
///         an operand it needs twice. Naming it into a local is always correct; repeating it is only
///         correct when evaluating it twice is the same as evaluating it once. Repeating a call is not:
///         <c>s[next_index()]</c> lowered to <c>string.sub(s, next_index(), next_index())</c> advances
///         twice and indexes with two different numbers.
///     </para>
///     <para>
///         So this errs one way only. Everything it does not recognise is treated as effectful, which
///         costs a local Luau compiles into a register and never costs correctness. That is why the list
///         is short: nothing is gained by growing it speculatively.
///     </para>
///     <para>
///         An operator is deliberately absent. <c>#t</c> and <c>-v</c> read as arithmetic, but on a table
///         they run <c>__len</c> and <c>__unm</c>, and a metamethod is a call like any other.
///     </para>
///     <para>
///         A call is where this would grow if Loom learned to prove a function has no effects - a
///         <c>[pure]</c> attribute, or an inferred equivalent. The receiver would have to arrive with
///         that fact attached, since nothing about a function is knowable from the Luau tree alone.
///     </para>
/// </remarks>
internal static class LuauEffects
{
    /// <summary>Whether evaluating <paramref name="expression" /> twice is the same as evaluating it once.</summary>
    public static bool IsRepeatable(LuauExpression expression) =>
        expression switch
        {
            Identifier or NumberLiteral or StringLiteral or BooleanLiteral or NilLiteral => true,
            Parenthesized parenthesized => IsRepeatable(parenthesized.Expression),
            _ => false
        };
}
