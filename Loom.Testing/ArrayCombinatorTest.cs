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
    [InlineData("let a = [1, 2, 3]; let b = a.select(fn(n) -> $\"{n}\");")]
    public void TypesTheLambdaParametersFromTheReceiver(string source) => Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));

    [Theory]
    [InlineData("let a = [1, 2, 3]; let b = a.select(fn(n) -> n * 2);", "number[]")]
    [InlineData("let a = [1, 2, 3]; let b = a.select(fn(n, i) -> n * i);", "number[]")]
    [InlineData("let a = [1, 2, 3]; let b = a.select(fn(n): string -> $\"{n}\");", "string[]")]
    [InlineData("let a = [1, 2, 3]; let b = a.select(fn(n) -> $\"{n}\");", "string[]")]
    [InlineData("let a = [1, 2, 3]; let b = a.where(fn(n) -> n > 1);", "number[]")]
    [InlineData("let a = [\"a\"]; let b = a.aggregate(\"\", fn(text, n) -> text + n);", "string")]
    [InlineData("let a = [1, 2, 3]; let b = a.select(fn(n) -> n * 2).where(fn(n) -> n > 2);", "number[]")]
    [InlineData("let a = [1, 2, 3]; let b = a.select(fn(n): string -> $\"{n}\").where(fn(s) -> s != \"\");", "string[]")]
    [InlineData("let a = [1, 2, 3]; let b = a.select(fn(n) -> $\"{n}\").where(fn(s) -> s != \"\");", "string[]")]
    [InlineData("let a = [1, 2, 3]; let b = a.any(fn(n) -> n > 1);", "bool")]
    [InlineData("let a = [1, 2, 3]; let b = a.all(fn(n) -> n > 1);", "bool")]
    [InlineData("let a = [1, 2, 3]; let b = a.count(fn(n) -> n > 1);", "number")]
    [InlineData("let a = [1, 2, 3]; let b = a.select_many(fn(n) -> [n, n * 2]);", "number[]")]
    [InlineData("let a = [1, 2, 3]; let b = a.select_many(fn(n): string[] -> [$\"{n}\"]);", "string[]")]
    [InlineData("let a = [1, 2, 3]; let b = a.select_many(fn(n) -> [$\"{n}\"]);", "string[]")]
    [InlineData("let a = [[1, 2], [3]]; let b = a.flatten();", "number[]")]
    [InlineData("let a = [[[1]]]; let b = a.flatten().flatten();", "number[]")]
    [InlineData("let a = [[1, 2], [3]]; let b = a.flatten().where(fn(n) -> n > 1);", "number[]")]
    public void InfersTheResultType(string source, string expected) => Assert.Equal(expected, Utility.GetLastStatementType(source + " let c = b;").ToString());

    [Fact]
    public void FlattenIsOnlyAMemberOfAnArrayOfArrays() =>
        Assert.Contains(
            Utility.GetTypeCheckerDiagnostics("let a = [1, 2, 3]; let b = a.flatten();").Set,
            diagnostic => diagnostic.Code == "L320"
        );

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

        // The loop fills the binding's own array rather than a temporary it would then be copied from.
        Assert.Contains("const b = table.create(#a)", rendered);
        Assert.Contains("b[_index] = n * 2", rendered);
        Assert.DoesNotContain("table.insert", rendered);
        Assert.DoesNotContain("_result", rendered);
    }

    [Fact]
    public void WherePresizesTheResultAndWritesThroughACounter()
    {
        var rendered = Utility.GetLuauAST("let a = [1, 2, 3]; let b = a.where(fn(n) -> n > 1);", typeCheck: true).Render();

        Assert.Contains("const b = table.create(#a)", rendered);
        Assert.Contains("_count += 1", rendered);
        Assert.Contains("b[_count] = n", rendered);
        Assert.DoesNotContain("table.insert", rendered);
        Assert.DoesNotContain("_result", rendered);
    }

    [Fact]
    public void AggregateCarriesTheAccumulatorInALocal()
    {
        var rendered = Utility.GetLuauAST("let a = [1, 2, 3]; let b = a.aggregate(0, fn(sum, n) -> sum + n);", typeCheck: true).Render();

        // The accumulator is the binding, so there is no second variable to copy it into afterwards.
        Assert.Contains("local b = 0", rendered);
        Assert.Contains("b = sum + n", rendered);
        Assert.DoesNotContain("_accumulator", rendered);
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
        Assert.Contains("b[_index] = d + 1", rendered);
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
        Assert.Contains("b[_index] = _callback(_element, _index)", rendered);
    }

    /// <summary>A lambda that returns early out of an 'if' cannot be spliced into the loop body as a single expression, so it is called like any other callback instead.</summary>
    [Fact]
    public void DoesNotInlineALambdaThatReturnsEarly()
    {
        var rendered = Utility.GetLuauAST(
                """
                let a = [1, 2, 3];
                let b = a.where(fn(n) {
                    if n > 1 {
                        return true;
                    }

                    return false;
                });
                """,
                typeCheck: true
            )
            .Render();

        Assert.Contains("function", rendered);
    }

    /// <summary>More parameters than the callback is ever handed cannot be satisfied by inlining, so it is called like any other callback instead.</summary>
    [Fact]
    public void DoesNotInlineALambdaWithMoreParametersThanTheCallbackIsGiven()
    {
        var rendered = Utility.GetLuauAST("let a = [1, 2, 3]; let b = a.where(fn(n, i, extra) -> n > 1);", typeCheck: true).Render();

        Assert.Contains("function", rendered);
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
        // 'mut' keeps the generated names, so '_result' is one the loop would otherwise have bound.
        var rendered = Utility.GetLuauAST("let a = [1, 2, 3]; mut b = a.where(fn(_result) -> _result > 1);", typeCheck: true).Render();

        Assert.Contains("function", rendered);
        Assert.DoesNotContain("for _, _result in", rendered);
    }

    /// <summary>A parameter only collides with a name the loop actually binds, which the binding's own is.</summary>
    [Fact]
    public void InlinesALambdaWhoseParameterOnlyShadowsANameNoLongerGenerated()
    {
        var rendered = Utility.GetLuauAST("let a = [1, 2, 3]; let b = a.where(fn(_result) -> _result > 1);", typeCheck: true).Render();

        Assert.DoesNotContain("function", rendered);
        Assert.Contains("for _, _result in a do", rendered);
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

    [Fact]
    public void AnyStopsAtTheFirstMatch()
    {
        var rendered = Utility.GetLuauAST("let a = [1, 2, 3]; let b = a.any(fn(n) -> n > 1);", typeCheck: true).Render();

        Assert.Contains("local b = false", rendered);
        Assert.Contains("b = true", rendered);
        Assert.Contains("break", rendered);
    }

    [Fact]
    public void AllStopsAtTheFirstFailure()
    {
        var rendered = Utility.GetLuauAST("let a = [1, 2, 3]; let b = a.all(fn(n) -> n > 1);", typeCheck: true).Render();

        Assert.Contains("local b = true", rendered);
        Assert.Contains("if n > 1 then continue end", rendered);
        Assert.Contains("b = false", rendered);
        Assert.Contains("break", rendered);
    }

    [Fact]
    public void CountBuildsNoArray()
    {
        var rendered = Utility.GetLuauAST("let a = [1, 2, 3]; let b = a.count(fn(n) -> n > 1);", typeCheck: true).Render();

        Assert.Contains("local b = 0", rendered);
        Assert.Contains("b += 1", rendered);
        Assert.DoesNotContain("table.create", rendered);
        Assert.DoesNotContain("table.insert", rendered);
    }

    [Fact]
    public void SelectManyCopiesEachSegmentInBulk()
    {
        var rendered = Utility.GetLuauAST("let a = [1, 2, 3]; let b = a.select_many(fn(n) -> [n, n * 2]);", typeCheck: true).Render();

        Assert.Contains("const _segment = {n, n * 2}", rendered);
        Assert.Contains("const _length = #_segment", rendered);
        Assert.Contains("table.move(_segment, 1, _length, _count + 1, b)", rendered);
        Assert.DoesNotContain("table.insert", rendered);
    }

    [Fact]
    public void FlattenCopiesEachSegmentInBulk()
    {
        var rendered = Utility.GetLuauAST("let a = [[1, 2], [3]]; let b = a.flatten();", typeCheck: true).Render();

        Assert.Contains("for _, _segment in a do", rendered);
        Assert.Contains("table.move(_segment, 1, _length, _count + 1, b)", rendered);
        Assert.DoesNotContain("table.insert", rendered);
    }

    [Fact]
    public void AQuantifierOnTheRightOfAndRunsOnlyWhenTheLeftAllowedIt()
    {
        var rendered = Utility.GetLuauAST(
                "let a = [1, 2, 3]; let b = a.any(fn(n) -> n > 1) && a.all(fn(n) -> n > 0);",
                typeCheck: true
            )
            .Render();

        Assert.Contains("local _and = _found", rendered);
        Assert.Contains("if _and then", rendered);
        Assert.DoesNotContain("_found and _satisfied", rendered);
    }

    [Fact]
    public void APredicateOnTheRightOfAndIsNotRunWhenTheLeftDecided()
    {
        var luau = Compile(
            "mut runs = 0; let a = [1, 2, 3]; let out = a.any(fn(n) -> n > 99) && a.all(fn(n) { runs += 1; return n > 0; });"
        );

        using var state = LuauState.Create();
        state.OpenLibraries();
        var returned = state.DoString(luau + "\nreturn tostring(out) .. \" \" .. tostring(runs)")[0];

        Assert.Equal("false 0", returned.ToString());
    }

    [Theory]
    [InlineData("numbers.any(fn(n) -> n > 4)", "true")]
    [InlineData("numbers.any(fn(n) -> n > 99)", "false")]
    [InlineData("numbers.all(fn(n) -> n > 0)", "true")]
    [InlineData("numbers.all(fn(n) -> n > 1)", "false")]
    [InlineData("numbers.any(fn(n, i) -> i == 5)", "true")]
    [InlineData("numbers.count(fn(n) -> n > 2)", "3")]
    [InlineData("numbers.count(fn(n) -> n > 99)", "0")]
    [InlineData("numbers.where(fn(n) -> n > 3).count(fn(n) -> n > 0)", "2")]
    public void TheEmittedQuantifierLoopAnswersCorrectly(string expression, string expected)
    {
        var luau = Compile($"let numbers = [1, 2, 3, 4, 5]; let out = {expression};");

        using var state = LuauState.Create();
        state.OpenLibraries();
        var returned = state.DoString(luau + "\nreturn tostring(out)")[0];

        Assert.Equal(expected, returned.ToString());
    }

    [Theory]
    [InlineData("let numbers = [1, 2, 3]; let out = numbers.select_many(fn(n) -> [n, n * 10]);", "1, 10, 2, 20, 3, 30")]
    [InlineData("let numbers = [1, 2, 3]; let out = numbers.select_many(fn(n): number[] -> []);", "")]
    [InlineData("let rows = [[1, 2], [3], []]; let out = rows.flatten();", "1, 2, 3")]
    [InlineData("let rows: number[][] = []; let out = rows.flatten();", "")]
    [InlineData("let rows = [[1, 2], [3]]; let out = rows.flatten().where(fn(n) -> n > 1);", "2, 3")]
    public void TheEmittedConcatenationLoopProducesTheRightArray(string source, string expected)
    {
        var luau = Compile(source);

        using var state = LuauState.Create();
        state.OpenLibraries();
        var returned = state.DoString(luau + "\nreturn table.concat(out, \", \")")[0];

        Assert.Equal(expected, returned.ToString());
    }

    [Fact]
    public void AnEmptyArrayAnswersEachQuantifierWithoutRunningThePredicate()
    {
        var luau = Compile(
            "let empty: number[] = []; let a = empty.any(fn(n) -> n > 0); let b = empty.all(fn(n) -> n > 0); let c = empty.count(fn(n) -> n > 0);"
        );

        using var state = LuauState.Create();
        state.OpenLibraries();
        var returned = state.DoString(luau + "\nreturn tostring(a) .. \" \" .. tostring(b) .. \" \" .. tostring(c)")[0];

        Assert.Equal("false true 0", returned.ToString());
    }

    private static string Compile(string source) => Utility.GetLuauAST(source, typeCheck: true).Render().Replace("const ", "local ");
}
