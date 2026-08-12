using NuLua;
using NuLua.Luau;

namespace Loom.Testing;

/// <summary>
///     Executes fused combinator chains instead of only reading them. A snapshot proves the emitted
///     loop did not change; this proves the one loop computes what the several loops used to, which is
///     the whole claim fusing makes.
/// </summary>
/// <remarks>
///     The cases that matter are the ones where fusing had to renumber something or bind a name: an
///     index read after a filter is an index into the filtered array, and a stage whose callback shares
///     a parameter name with the stage above it must shadow rather than read it. Both are invisible in
///     a snapshot and wrong answers here.
/// </remarks>
[Collection("Assembly")]
public class ArrayCombinatorRuntimeTest
{
    private const string Numbers = "let numbers = [1, 2, 3, 4, 5];\n";

    [Theory]
    // Map then filter then reduce - the chain that used to be three loops and two arrays.
    [InlineData("numbers.select(fn(n) -> n * 2).where(fn(n) -> n > 4).aggregate(0, fn(sum, n) -> sum + n)", "24")]
    // Every stage reusing one parameter name, so each binding has to shadow the one above it.
    [InlineData("numbers.select(fn(n) -> n + 1).select(fn(n) -> n * 10).aggregate(0, fn(a, n) -> a + n)", "200")]
    // A filter renumbers what follows it: the indices seen here are 1..3, not the original positions.
    [InlineData("numbers.where(fn(n) -> n > 2).select(fn(n, i) -> i).aggregate(0, fn(a, n) -> a + n)", "6")]
    [InlineData("numbers.where(fn(n) -> n > 2).select(fn(n, i) -> n * i).aggregate(0, fn(a, n) -> a + n)", "26")]
    // Two filters in a row, so the second renumbers what the first already renumbered.
    [InlineData("numbers.where(fn(n) -> n > 1).where(fn(n, i) -> i <= 2).aggregate(0, fn(a, n) -> a + n)", "5")]
    // Terminals that short-circuit still see the mapped values.
    [InlineData("numbers.select(fn(n) -> n * 2).any(fn(n) -> n > 9)", "true")]
    [InlineData("numbers.select(fn(n) -> n * 2).any(fn(n) -> n > 100)", "false")]
    [InlineData("numbers.select(fn(n) -> n * 2).all(fn(n) -> n > 1)", "true")]
    [InlineData("numbers.select(fn(n) -> n * 2).all(fn(n) -> n > 3)", "false")]
    [InlineData("numbers.where(fn(n) -> n > 1).count(fn(n) -> n % 2 == 0)", "2")]
    // The array-producing terminals, whose result has to come out dense and in order.
    [InlineData("numbers.select(fn(n) -> n * 2).where(fn(n) -> n > 4).length", "3")]
    [InlineData("numbers.where(fn(n) -> n > 2).select(fn(n) -> n * 2).join(\",\")", "6,8,10")]
    [InlineData("numbers.select(fn(n) -> n * 2).where(fn(n) -> n > 4).join(\",\")", "6,8,10")]
    [InlineData("numbers.where(fn(n) -> n > 3).select_many(fn(n) -> [n, n * 10]).join(\",\")", "4,40,5,50")]
    public void Computes(string expression, string expected) => Assert.Equal(expected, Run($"{Numbers}let outcome = {expression};"));

    /// <summary>
    ///     A stage that turns one element into several nests a loop inside the fused body, and everything
    ///     below it is positioned against the flattened run rather than the row it came from.
    /// </summary>
    [Theory]
    [InlineData("rows.flatten().where(fn(n) -> n > 1).join(\",\")", "2,3,4")]
    [InlineData("rows.flatten().select(fn(n) -> n * 2).join(\",\")", "2,4,6,8")]
    [InlineData("rows.flatten().select(fn(n, i) -> i).join(\",\")", "1,2,3,4")]
    [InlineData("rows.flatten().aggregate(0, fn(a, n) -> a + n)", "10")]
    [InlineData("rows.flatten().count(fn(n) -> n % 2 == 0)", "2")]
    [InlineData("rows.select_many(fn(r) -> r).where(fn(n) -> n > 2).join(\",\")", "3,4")]
    [InlineData("rows.where(fn(r) -> r.length > 1).flatten().join(\",\")", "1,2,3")]
    // 'any' and 'all' leave the loop early, which a nested one cannot do - these stay unfused and correct.
    [InlineData("rows.flatten().any(fn(n) -> n > 3)", "true")]
    [InlineData("rows.flatten().all(fn(n) -> n > 3)", "false")]
    public void ComputesAcrossASpread(string expression, string expected) =>
        Assert.Equal(expected, Run($"let rows = [[1, 2, 3], [4]];\nlet outcome = {expression};"));

    /// <summary>
    ///     The hazard fusing introduces: <c>total</c> is a variable from outside the loop, and the stage
    ///     above binds a parameter of the same name. Inlined naively, the predicate would compare against
    ///     the mapped element instead of the 100 it was written against.
    /// </summary>
    [Fact]
    public void AStageDoesNotCaptureANameTheStageBelowItMeant()
    {
        const string source = """
            let total = 100;
            let numbers = [1, 2, 3, 4, 5];
            let outcome = numbers.select(fn(total) -> total * 2).where(fn(n) -> n < total).length;
            """;

        Assert.Equal("5", Run(source));
    }

    /// <summary>A chain over an empty array must still produce an empty array rather than nil holes.</summary>
    [Fact]
    public void AnEmptyChainProducesAnEmptyResult() =>
        Assert.Equal("0", Run("let numbers: number[] = [];\nlet outcome = numbers.select(fn(n) -> n * 2).where(fn(n) -> n > 0).length;"));

    /// <summary>
    ///     Fusing makes a stage run once per element rather than once per array, so a callback that is
    ///     called the wrong number of times shows up here as the wrong count.
    /// </summary>
    [Fact]
    public void EveryStageRunsOncePerElementThatReachesIt()
    {
        const string source = """
            mut mapped = 0;
            mut tested = 0;

            fn twice(n: number): number {
                mapped += 1;
                return n * 2;
            }

            fn big(n: number): bool {
                tested += 1;
                return n > 4;
            }

            let numbers = [1, 2, 3, 4, 5];
            let kept = numbers.select(twice).where(big).length;
            let outcome = mapped * 100 + tested * 10 + kept;
            """;

        // Five elements mapped, five tested, three kept - one pass, not one pass per stage.
        Assert.Equal("553", Run(source));
    }

    /// <summary>
    ///     Fusing interleaves the stages rather than running each to completion, so a chain of callbacks
    ///     with side effects sees them in a different order than it used to. This pins that down: it is
    ///     the semantics a combinator chain is written against, not an accident of the lowering.
    /// </summary>
    [Fact]
    public void StagesInterleaveRatherThanRunningOneAtATime()
    {
        const string source = """
            mut trace = "";

            fn twice(n: number): number {
                trace += "m";
                return n * 2;
            }

            fn big(n: number): bool {
                trace += "t";
                return n > 2;
            }

            let numbers = [1, 2];
            let kept = numbers.select(twice).where(big).length;
            let outcome = trace;
            """;

        Assert.Equal("mtmt", Run(source));
    }

    /// <summary>
    ///     And a terminal that stops early now stops the stages above it too, so a mapping callback runs
    ///     only for the elements the answer actually depended on.
    /// </summary>
    [Fact]
    public void AShortCircuitingTerminalStopsTheStagesAboveIt()
    {
        const string source = """
            mut mapped = 0;

            fn twice(n: number): number {
                mapped += 1;
                return n * 2;
            }

            let numbers = [1, 2, 3, 4, 5];
            let found = numbers.select(twice).any(fn(n) -> n > 2);
            let outcome = mapped;
            """;

        // Stops at the second element, rather than mapping all five and then looking.
        Assert.Equal("2", Run(source));
    }

    private static string Run(string source)
    {
        var luau = Utility.GetLuauAST(source, typeCheck: true).Render().Replace("const ", "local ");

        using var state = LuauState.Create();
        state.OpenLibraries();

        return state.DoString($"{luau}{Environment.NewLine}return tostring(outcome)")[0].ToString();
    }
}
