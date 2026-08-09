using NuLua;
using NuLua.Luau;

namespace Loom.Testing;

[Collection("Assembly")]
public class ArrayCombinatorTest
{
    [Theory]
    [InlineData("let a = [1, 2, 3]; let b = a.select(fn(n) -> n * 2);")]
    [InlineData("let a = [1, 2, 3]; let b = a.select(fn(n, i) -> n * i);")]
    [InlineData("let a = [1, 2, 3]; let b = a.where(fn(n) -> n > 1);")]
    [InlineData("let a = [1, 2, 3]; let b = a.where(fn(n, i) -> n > i);")]
    [InlineData("let a = [1, 2, 3]; let b = a.aggregate(0, fn(sum, n) -> sum + n);")]
    [InlineData("let a = [\"a\", \"b\"]; let b = a.aggregate(\"\", fn(text, n) -> text + n);")]
    [InlineData("fn triple(n: number): number -> n * 3; let a = [1, 2, 3]; let b = a.select(triple);")]
    public void TypesTheLambdaParametersFromTheReceiver(string source) => Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));

    [Theory]
    [InlineData("let a = [1, 2, 3]; let b = a.select(fn(n) -> n * 2);", "number[]")]
    [InlineData("let a = [1, 2, 3]; let b = a.select(fn(n, i) -> n * i);", "number[]")]
    [InlineData("let a = [1, 2, 3]; let b = a.select(fn(n): string -> `{n}`);", "string[]")]
    [InlineData("let a = [1, 2, 3]; let b = a.where(fn(n) -> n > 1);", "number[]")]
    [InlineData("let a = [\"a\"]; let b = a.aggregate(\"\", fn(text, n) -> text + n);", "string")]
    [InlineData("let a = [1, 2, 3]; let b = a.select(fn(n) -> n * 2).where(fn(n) -> n > 2);", "number[]")]
    [InlineData("let a = [1, 2, 3]; let b = a.select(fn(n): string -> `{n}`).where(fn(s) -> s != \"\");", "string[]")]
    public void InfersTheResultType(string source, string expected) => Assert.Equal(expected, Utility.GetLastStatementType(source + " let c = b;").ToString());

    [Theory]
    [InlineData("let a = [1, 2, 3]; let b: number[] = a.select(fn(n) -> n * 2);")]
    [InlineData("let a = [1, 2, 3]; let b: number[] = a.where(fn(n) -> n > 1);")]
    [InlineData("let a = [1, 2, 3]; let b: number = a.aggregate(0, fn(sum, n) -> sum + n);")]
    [InlineData("let a = [1, 2, 3]; let b: number = a.select(fn(n) -> n * 2).aggregate(0, fn(sum, n) -> sum + n);")]
    public void TheResultSatisfiesAnAnnotation(string source) => Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));

    [Fact]
    public void SelectPresizesTheResultAndWritesAtTheSourceIndex()
    {
        var rendered = Utility.GetLuauAST("let a = [1, 2, 3]; let b = a.select(fn(n) -> n * 2);", typeCheck: true).Render();

        Assert.Contains("table.create(#a)", rendered);
        Assert.Contains("_result[_index] = n * 2", rendered);
        Assert.DoesNotContain("table.insert", rendered);
    }

    [Fact]
    public void WherePresizesTheResultAndWritesThroughACounter()
    {
        var rendered = Utility.GetLuauAST("let a = [1, 2, 3]; let b = a.where(fn(n) -> n > 1);", typeCheck: true).Render();

        Assert.Contains("table.create(#a)", rendered);
        Assert.Contains("_count += 1", rendered);
        Assert.Contains("_result[_count] = n", rendered);
        Assert.DoesNotContain("table.insert", rendered);
    }

    [Fact]
    public void AggregateCarriesTheAccumulatorInALocal()
    {
        var rendered = Utility.GetLuauAST("let a = [1, 2, 3]; let b = a.aggregate(0, fn(sum, n) -> sum + n);", typeCheck: true).Render();

        Assert.Contains("local _accumulator = 0", rendered);
        Assert.Contains("_accumulator = sum + n", rendered);
        Assert.Contains("const b = _accumulator", rendered);
    }

    [Fact]
    public void SplicesALambdaBodyIntoTheLoopInsteadOfCallingIt()
    {
        var rendered = Utility.GetLuauAST("let a = [1, 2, 3]; let b = a.select(fn(n) -> n * 2);", typeCheck: true).Render();

        Assert.DoesNotContain("function", rendered);
    }

    [Fact]
    public void SplicesTheStatementsALambdaBodyRunsBeforeItsValue()
    {
        var rendered = Utility.GetLuauAST("let a = [1, 2, 3]; let b = a.select(fn(n) { let d = n * 2; return d + 1; });", typeCheck: true).Render();

        Assert.Contains("const d = n * 2", rendered);
        Assert.Contains("_result[_index] = d + 1", rendered);
        Assert.DoesNotContain("function", rendered);
    }

    [Fact]
    public void EvaluatesACallbackThatIsNotALambdaOnceRatherThanPerElement()
    {
        var rendered = Utility.GetLuauAST(
                "fn make(): (number, number) -> number -> fn(n: number, i: number) -> n * 2; let a = [1, 2, 3]; let b = a.select(make());",
                typeCheck: true
            )
            .Render();

        Assert.Contains("const _callback = make()", rendered);
        Assert.Contains("_result[_index] = _callback(_element, _index)", rendered);
    }

    [Fact]
    public void HoistsTheReceiverSoItIsEvaluatedOnce()
    {
        var rendered = Utility.GetLuauAST(
                "fn source(): number[] -> [1, 2, 3]; let b = source().where(fn(n) -> n > 1);",
                typeCheck: true
            )
            .Render();

        Assert.Contains("const _source = source()", rendered);
        Assert.Contains("table.create(#_source)", rendered);
        Assert.Contains("for _, n in _source do", rendered);
    }

    [Fact]
    public void DoesNotCaptureTheLoopVariablesWhenALambdaParameterShadowsThem()
    {
        var rendered = Utility.GetLuauAST("let a = [1, 2, 3]; let b = a.where(fn(_result) -> _result > 1);", typeCheck: true).Render();

        Assert.Contains("function", rendered);
        Assert.DoesNotContain("for _, _result in", rendered);
    }

    [Theory]
    [InlineData("numbers.select(fn(n) -> n * 2)", "2, 4, 6, 8, 10")]
    [InlineData("numbers.select(fn(n, i) -> n * i)", "1, 4, 9, 16, 25")]
    [InlineData("numbers.where(fn(n) -> n > 2)", "3, 4, 5")]
    [InlineData("numbers.where(fn(n) -> n > 99)", "")]
    [InlineData("numbers.where(fn(n, i) -> i % 2 == 1)", "1, 3, 5")]
    [InlineData("numbers.select(fn(n) -> n * 2).where(fn(n) -> n > 4)", "6, 8, 10")]
    public void TheEmittedLoopProducesTheRightArray(string expression, string expected)
    {
        var luau = Compile($"let numbers = [1, 2, 3, 4, 5]; let out = {expression};");

        using var state = LuauState.Create();
        state.OpenLibraries();
        var returned = state.DoString(luau + "\nreturn table.concat(out, \", \")")[0];

        Assert.Equal(expected, returned.ToString());
    }

    [Theory]
    [InlineData("numbers.aggregate(0, fn(sum, n) -> sum + n)", "15")]
    [InlineData("numbers.aggregate(1, fn(product, n) -> product * n)", "120")]
    [InlineData("numbers.aggregate(0, fn(sum, n, i) -> sum + i)", "15")]
    [InlineData("numbers.where(fn(n) -> n > 3).aggregate(0, fn(sum, n) -> sum + n)", "9")]
    [InlineData("numbers.where(fn(n) -> n > 99).aggregate(7, fn(sum, n) -> sum + n)", "7")]
    public void TheEmittedLoopReducesToTheRightValue(string expression, string expected)
    {
        var luau = Compile($"let numbers = [1, 2, 3, 4, 5]; let out = {expression};");

        using var state = LuauState.Create();
        state.OpenLibraries();
        var returned = state.DoString(luau + "\nreturn tostring(out)")[0];

        Assert.Equal(expected, returned.ToString());
    }

    [Fact]
    public void TheSelectedArrayHasTheSameLengthAsItsSource()
    {
        var luau = Compile("let numbers = [1, 2, 3, 4, 5]; let out = numbers.select(fn(n) -> n * 2);");

        using var state = LuauState.Create();
        state.OpenLibraries();
        var returned = state.DoString(luau + "\nreturn #out")[0];

        Assert.Equal("5", returned.ToString());
    }

    [Fact]
    public void AnInlinedReducerSeesTheAccumulatorItWasHanded()
    {
        var luau = Compile("let numbers = [1, 2, 3]; let out = numbers.aggregate(0, fn(sum, n) -> sum + n * 10);");

        using var state = LuauState.Create();
        state.OpenLibraries();
        var returned = state.DoString(luau + "\nreturn tostring(out)")[0];

        Assert.Equal("60", returned.ToString());
    }

    private static string Compile(string source) => Utility.GetLuauAST(source, typeCheck: true).Render().Replace("const ", "local ");
}
