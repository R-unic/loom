using NuLua;
using NuLua.Luau;
using Loom.Testing;

namespace Loom.Testing.Runtime;

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
    ///     A chain that is only measured counts instead of collecting, so the answer has to survive never
    ///     building the array it used to be read off.
    /// </summary>
    [Theory]
    [InlineData("numbers.where(fn(n) -> n > 2).length", "3")]
    [InlineData("numbers.where(fn(n) -> n > 9).length", "0")]
    [InlineData("numbers.select(fn(n) -> n * 2).where(fn(n) -> n > 4).length", "3")]
    [InlineData("numbers.where(fn(n) -> n > 1).where(fn(n, i) -> i <= 2).length", "2")]
    [InlineData("numbers.select_many(fn(n) -> [n, n * 10]).length", "10")]
    [InlineData("numbers.where(fn(n) -> n > 3).select_many(fn(n) -> [n, n * 10]).length", "4")]
    // 'select' is length-preserving but still has to run, so it keeps collecting.
    [InlineData("numbers.select(fn(n) -> n * 2).length", "5")]
    [InlineData("numbers.length", "5")]
    public void MeasuresWithoutCollecting(string expression, string expected) =>
        Assert.Equal(expected, Run($"{Numbers}let outcome = {expression};"));

    [Theory]
    [InlineData("rows.flatten().length", "4")]
    [InlineData("rows.select_many(fn(r) -> r).length", "4")]
    [InlineData("rows.flatten().where(fn(n) -> n > 1).length", "3")]
    [InlineData("rows.where(fn(r) -> r.length > 1).flatten().length", "3")]
    public void MeasuresAcrossASpread(string expression, string expected) =>
        Assert.Equal(expected, Run($"let rows = [[1, 2, 3], [4]];\nlet outcome = {expression};"));

    /// <summary>
    ///     A chain accumulates into the name it is bound to rather than into a temporary, so the answer
    ///     has to survive the counter and the binding being one variable - including where the loop binds
    ///     that same name itself and the two have to stay apart.
    /// </summary>
    [Theory]
    [InlineData("let n = numbers.where(fn(n) -> n > 2).length;", "n", "3")]
    [InlineData("let big = numbers.where(fn(big) -> big > 2).length;", "big", "3")]
    [InlineData("let i = numbers.select(fn(v, i) -> v * i).where(fn(v) -> v > 4).length;", "i", "3")]
    [InlineData("let kept = numbers.where(fn(n) -> n > 2).length;", "kept", "3")]
    [InlineData("let total = numbers.select(fn(n) -> n * 2).aggregate(0, fn(a, n) -> a + n);", "total", "30")]
    [InlineData("let sum = numbers.aggregate(0, fn(sum, n) -> sum + n);", "sum", "15")]
    [InlineData("mut kept = numbers.where(fn(n) -> n > 2).length;", "kept", "3")]
    public void AccumulatesIntoTheNameItIsBoundTo(string declaration, string read, string expected) =>
        Assert.Equal(expected, Run($"{Numbers}{declaration}\nlet outcome = {read};"));

    /// <summary>
    ///     Measuring drops the array, not the work that filled it: the predicate still sees every element.
    /// </summary>
    [Fact]
    public void MeasuringStillRunsTheCallbacks()
    {
        const string source = """
            mut tested = 0;

            fn big(n: number): bool {
                tested += 1;
                return n > 2;
            }

            let numbers = [1, 2, 3, 4, 5];
            let kept = numbers.where(big).length;
            let outcome = tested * 10 + kept;
            """;

        Assert.Equal("53", Run(source));
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

    /// <summary>
    ///     The unfused fallback. A callback whose body contains a shape the identifier scan does not model -
    ///     a spread, here - makes fusing unsafe, so each combinator emits a loop of its own instead. That
    ///     path computes the same answers or the refusal to fuse is not the safe direction it claims to be.
    /// </summary>
    [Theory]
    [InlineData("numbers.aggregate(0, fn(a, n) -> total_of(..[a, n]))", "15")]
    [InlineData("numbers.select(fn(n) -> total_of(..[n, n])).join(\",\")", "2,4,6,8,10")]
    [InlineData("numbers.where(fn(n) -> total_of(..[n]) > 3).join(\",\")", "4,5")]
    [InlineData("numbers.count(fn(n) -> total_of(..[n]) > 3)", "2")]
    [InlineData("numbers.any(fn(n) -> total_of(..[n]) > 4)", "true")]
    [InlineData("numbers.all(fn(n) -> total_of(..[n]) > 0)", "true")]
    public void ComputesWhenFusingIsDeclined(string expression, string expected)
    {
        const string helper = """
            fn total_of(..values: number[]): number {
                mut sum = 0;
                for v : values
                    sum += v;

                return sum;
            }

            """;

        Assert.Equal(expected, Run($"{helper}{Numbers}let outcome = {expression};"));
    }

    /// <summary>
    ///     Every combinator driven by a named function rather than a lambda. A callback written inline is
    ///     inlined into the fused loop; one passed by name cannot be, so each combinator falls back to
    ///     emitting a loop of its own that calls it - a second lowering per combinator that a chain of
    ///     lambdas never reaches.
    /// </summary>
    [Theory]
    [InlineData("numbers.select(twice).join(\",\")", "2,4,6,8,10")]
    [InlineData("numbers.where(big).join(\",\")", "4,5")]
    [InlineData("numbers.aggregate(0, add).", "15")]
    [InlineData("numbers.select_many(pair).join(\",\")", "1,10,2,20,3,30,4,40,5,50")]
    [InlineData("numbers.any(big).", "true")]
    [InlineData("numbers.all(big).", "false")]
    [InlineData("numbers.count(big).", "2")]
    public void ComputesWithACallbackPassedByName(string expression, string expected)
    {
        const string helpers = """
            fn twice(n: number): number -> n * 2;
            fn big(n: number): bool -> n > 3;
            fn add(total: number, n: number): number -> total + n;
            fn pair(n: number): number[] -> [n, n * 10];

            """;

        Assert.Equal(expected, Run($"{helpers}{Numbers}let outcome = {expression.TrimEnd('.')};"));
    }

    /// <summary>
    ///     The members that lower to a <c>table</c> call rather than a loop. They fuse with nothing, so what
    ///     has to be right is the call itself - a wrong argument order here is a silent no-op or a value
    ///     inserted at the wrong index, and both read as the array simply not changing.
    /// </summary>
    [Theory]
    [InlineData("let xs = mut [1, 2, 3]; xs.push(4); let outcome = xs.join(\",\");", "1,2,3,4")]
    [InlineData("let xs = mut [1, 2, 3]; xs.insert(1, 0); let outcome = xs.join(\",\");", "0,1,2,3")]
    [InlineData("let xs = mut [1, 2, 3]; xs.pop(); let outcome = xs.join(\",\");", "1,2")]
    [InlineData("let xs = mut [1, 2, 3]; xs.remove(1); let outcome = xs.join(\",\");", "2,3")]
    [InlineData("let xs = [1, 2, 3]; let outcome = xs.index_of(2);", "2")]
    [InlineData("let xs = [1, 2, 3]; let outcome = xs.has(2);", "true")]
    [InlineData("let xs = [1, 2, 3]; let outcome = xs.has(9);", "false")]
    [InlineData("let xs = [1, 2, 3]; let outcome = xs.length;", "3")]
    [InlineData("let xs = [\"a\", \"b\"]; let outcome = xs.join(\"-\");", "a-b")]
    [InlineData("let xs = mut [1, 2, 3]; xs.remove_value(2); let outcome = xs.join(\",\");", "1,3")]
    [InlineData("let xs = mut [1, 2, 3]; xs.remove_value(9); let outcome = xs.join(\",\");", "1,2,3")]
    [InlineData("let xs = mut [1, 2, 3]; xs.clear(); let outcome = xs.length;", "0")]
    [InlineData("let xs = [1, 2, 3]; let outcome = xs.reverse().join(\",\");", "3,2,1")]
    [InlineData("let xs: number[] = []; let outcome = xs.reverse().join(\",\");", "")]
    public void ComputesTheTableBackedMembers(string source, string expected) => Assert.Equal(expected, Run(source));

    /// <remarks>A set is a table keyed by its members, so membership is a lookup rather than a scan.</remarks>
    [Fact]
    public void ToSet_KeysTheTableByItsMembers() =>
        Assert.Equal("true", Run("let xs = [1, 2, 2, 3]; let s = xs.to_set(); let outcome = s.has(2) && !s.has(9);"));

    /// <summary>Cloning copies into a new table, so mutating the source afterwards leaves the clone untouched.</summary>
    [Fact]
    public void Clone_IsIndependentOfTheSource() =>
        Assert.Equal(
            "1,2,3,4|1,2,3",
            Run("let xs = mut [1, 2, 3]; let cloned = xs.clone(); xs.push(4); let outcome = xs.join(\",\") + \"|\" + cloned.join(\",\");")
        );

    [Theory]
    [InlineData("[1, 2, 3].find(fn(n) -> n > 1)", "2")]
    [InlineData("[1, 2, 3].find(fn(n) -> n > 9)", "nil")]
    [InlineData("[1, 2, 3].find_index(fn(n) -> n > 1)", "2")]
    [InlineData("[1, 2, 3].find_index(fn(n) -> n > 9)", "nil")]
    public void FindAndFindIndex_StopAtTheFirstMatch(string expression, string expected) =>
        Assert.Equal(expected, Run($"let outcome = {expression};"));

    private static string Run(string source)
    {
        var luau = Utility.GetLuauAST(source, typeCheck: true).Render().Replace("const ", "local ");

        using var state = LuauState.Create();
        state.OpenLibraries();

        return state.DoString($"{luau}{Environment.NewLine}return tostring(outcome)")[0].ToString();
    }
}
