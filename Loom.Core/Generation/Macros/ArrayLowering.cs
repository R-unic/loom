using Loom.Luau;
using Loom.Luau.AST;

namespace Loom.Core.Generation.Macros;

/// <summary>
///     The names and the one segment-append every array combinator lowering writes, shared so a stage
///     emitted on its own and the same stage emitted inside a fused loop cannot drift apart.
/// </summary>
internal static class ArrayLowering
{
    public const string SourceName = "_source";
    public const string ResultName = "_result";
    public const string CountName = "_count";
    public const string AccumulatorName = "_accumulator";
    public const string CallbackName = "_callback";
    public const string ElementName = "_element";
    public const string IndexName = "_index";
    public const string SegmentName = "_segment";
    public const string LengthName = "_length";
    public const string FoundName = "_found";
    public const string SatisfiedName = "_satisfied";
    public const string DiscardName = "_";

    /// <summary>
    ///     Adds every element of <paramref name="array" /> to <paramref name="set" /> as a key, which is
    ///     all a set is. Shared so a set built from an array and a set built from spread members are the
    ///     same table and need no conversion between them.
    /// </summary>
    public static void AddElementsToSet(LuauState state, List<LuauStatement> body, Identifier set, LuauExpression array)
    {
        var elementName = state.Scope.AddIdentifier(ElementName);
        body.Add(
            new ForStatement(
                [DiscardName, elementName],
                array,
                new Chunk([new ExpressionStatement(new BinaryOperator(new ElementAccess(set, new Identifier(elementName)), "=", new BooleanLiteral(true)))])
            )
        );
    }

    /// <summary>
    ///     Copies a segment onto the end of the result with one <c>table.move</c> rather than an
    ///     element-at-a-time loop, carrying the write position in <paramref name="count" /> so the
    ///     append point is never re-derived.
    /// </summary>
    public static void AppendSegment(LuauState state, List<LuauStatement> body, Identifier result, Identifier count, LuauExpression segment)
    {
        if (segment is not Identifier)
        {
            var segmentName = state.Scope.AddIdentifier(SegmentName);
            body.Add(new ConstVariable(segmentName, null, segment));
            segment = new Identifier(segmentName);
        }

        var lengthName = state.Scope.AddIdentifier(LengthName);
        var length = new Identifier(lengthName);
        body.Add(new ConstVariable(lengthName, null, new UnaryOperator("#", segment)));
        body.Add(
            new ExpressionStatement(
                LuauFactory.TableCall("move", [segment, new NumberLiteral(1), length, new BinaryOperator(count, "+", new NumberLiteral(1)), result])
            )
        );

        body.Add(new ExpressionStatement(new BinaryOperator(count, "+=", length)));
    }
}
