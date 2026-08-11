using NuLua;
using NuLua.Luau;

namespace Loom.Testing;

/// <summary>
///     Executes the lowered set operations instead of only reading them. A snapshot proves the output did
///     not change; these prove it computes the right answer - which matters more here than for most macros,
///     since every operation but <c>has</c> lowers to a loop that has to build the result table itself.
/// </summary>
/// <remarks>Loom spells an immutable binding <c>const</c>, which Luau has no keyword for.</remarks>
[Collection("Assembly")]
public class SetRuntimeTest
{
    [Theory]
    [InlineData("Set.of(1, 2, 3).size", "3")]
    [InlineData("Set.of(1, 2, 2, 3).size", "3")]
    [InlineData("Set.empty::<number>().size", "0")]
    [InlineData("Set.of(1).is_empty", "false")]
    [InlineData("Set.empty::<number>().is_empty", "true")]
    [InlineData("Set.of(1, 2, 3).has(2)", "true")]
    [InlineData("Set.of(1, 2, 3).has(9)", "false")]
    [InlineData("Set.of(\"a\", \"b\").has(\"b\")", "true")]
    [InlineData("Set.of(1, 2).union(Set.of(2, 3)).size", "3")]
    [InlineData("Set.of(1, 2).union(Set.of(2, 3)).has(3)", "true")]
    [InlineData("Set.of(1, 2, 3).intersect(Set.of(2, 3, 4)).size", "2")]
    [InlineData("Set.of(1, 2, 3).intersect(Set.of(2, 3, 4)).has(1)", "false")]
    [InlineData("Set.of(1, 2, 3).difference(Set.of(2)).size", "2")]
    [InlineData("Set.of(1, 2, 3).difference(Set.of(2)).has(2)", "false")]
    [InlineData("Set.of(1, 2).is_subset_of(Set.of(1, 2, 3))", "true")]
    [InlineData("Set.of(1, 4).is_subset_of(Set.of(1, 2, 3))", "false")]
    [InlineData("Set.empty::<number>().is_subset_of(Set.of(1))", "true")]
    [InlineData("[1, 2, 2, 3].to_set().size", "3")]
    [InlineData("[1, 2, 2, 3].to_set().has(2)", "true")]
    public void Executes(string expression, string expected) =>
        Assert.Equal(expected, Run($"let answer = {expression};", "return tostring(answer)"));

    [Theory]
    [InlineData("m.add(4);", "4")]
    [InlineData("m.remove(1);", "2")]
    [InlineData("m.remove(9);", "3")]
    [InlineData("m.add(1);", "3")]
    public void MutationChangesSize(string mutation, string expected) =>
        Assert.Equal(expected, Run($"let m = MutSet.of(1, 2, 3);\n{mutation}\nlet answer = m.size;", "return tostring(answer)"));

    [Fact]
    public void RemoveClearsMembership() =>
        Assert.Equal("false", Run("let m = MutSet.of(1, 2);\nm.remove(2);\nlet answer = m.has(2);", "return tostring(answer)"));

    /// <summary>A mutable set is assignable to an immutable one, so the algebra members accept one directly.</summary>
    [Fact]
    public void MutableSetPassesToAnImmutableParameter() =>
        Assert.Equal("3", Run("let m = MutSet.of(2, 3);\nlet answer = Set.of(1, 2).union(m).size;", "return tostring(answer)"));

    /// <summary>Key order is unspecified, so the members are sorted before they are compared.</summary>
    [Fact]
    public void ToArrayReturnsEveryMemberOnce() =>
        Assert.Equal("1,2,3", Run("let answer = Set.of(3, 1, 2, 1).to_array();", "table.sort(answer)\nreturn table.concat(answer, \",\")"));

    /// <summary>
    ///     A set is the same plain table however it was built, so one from an array is interchangeable with
    ///     one from the constructors.
    /// </summary>
    [Fact]
    public void ArrayToSetProducesTheSameShapeAsTheConstructor() =>
        Assert.Equal("true", Run("let answer = [1, 2].to_set().is_subset_of(Set.of(1, 2, 3));", "return tostring(answer)"));

    /// <summary>
    ///     The receiver is read more than once by every lowering that loops, so a call in receiver position
    ///     has to be evaluated exactly once.
    /// </summary>
    [Fact]
    public void ReceiverIsEvaluatedOnce()
    {
        const string source = """
            mut calls = 0;
            fn build(): Set<number> {
                calls += 1;
                return Set.of(1, 2);
            }

            let answer = build().union(Set.of(3));
            """;

        Assert.Equal("1", Run(source, "return tostring(calls)"));
    }

    private static string Run(string source, string epilogue)
    {
        var luau = Utility.GetLuauAST(source, typeCheck: true).Render().Replace("const ", "local ");

        using var state = LuauState.Create();
        state.OpenLibraries();

        return state.DoString($"{luau}{Environment.NewLine}{epilogue}")[0].ToString();
    }
}
