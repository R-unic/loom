using Loom.Core.Diagnostics;

namespace Loom.Testing;

[Collection("Assembly")]
public class ResultCombinatorTest
{
    private const string Fetch = """
        fn fetch(): Result<number, string> {
            return BaseResult::ok(1);
        }


        """;

    [Fact]
    public void Unwrap_RaisesTheErrorAndYieldsTheValue()
    {
        var rendered = Render("let n = fetch().unwrap();");

        Assert.Contains("if not _result.ok then", rendered);
        Assert.Contains("error(_result.error)", rendered);
        Assert.Contains("_result.value", rendered);
    }

    [Fact]
    public void Expect_RaisesTheSuppliedMessage()
    {
        var rendered = Render("""let n = fetch().expect("no number");""");

        Assert.Contains("""error("no number")""", rendered);
        Assert.DoesNotContain("error(_result.error)", rendered);
    }

    [Fact]
    public void UnwrapErr_RaisesTheValueAndYieldsTheError()
    {
        var rendered = Render("let e = fetch().unwrap_err();");

        Assert.Contains("if _result.ok then", rendered);
        Assert.Contains("error(_result.value)", rendered);
        Assert.Contains("_result.error", rendered);
    }

    [Fact]
    public void ExpectErr_RaisesTheSuppliedMessage()
    {
        var rendered = Render("""let e = fetch().expect_err("expected a failure");""");

        Assert.Contains("""error("expected a failure")""", rendered);
        Assert.DoesNotContain("error(_result.value)", rendered);
    }

    [Fact]
    public void UnwrapOr_SelectsTheFallbackWithoutRaising()
    {
        var rendered = Render("let n = fetch().unwrap_or(0);");

        Assert.Contains("if _result.ok then _result.value else 0", rendered);
        Assert.DoesNotContain("error(", rendered);
    }

    [Fact]
    public void UnwrapOrElse_CallsTheHandlerWithTheError()
    {
        var rendered = Render("""
            fn recover(message: string): number {
                return 0;
            }

            let n = fetch().unwrap_or_else(recover);
            """);

        Assert.Contains("recover(_result.error)", rendered);
    }

    [Fact]
    public void Map_RebuildsTheOkArmAndPassesTheErrorArmThrough()
    {
        var rendered = Render("""
            fn double(n: number): number {
                return n * 2;
            }

            let mapped = fetch().map(double);
            """);

        Assert.Contains("double(_result.value)", rendered);
        Assert.Contains("ok = true", rendered);
        Assert.Contains("else _result", rendered);
    }

    [Fact]
    public void AndThen_ChainsWithoutRewrappingTheError()
    {
        var rendered = Render("""
            fn step(n: number): Result<number, string> {
                return BaseResult::ok(n);
            }

            let chained = fetch().and_then(step);
            """);

        Assert.Contains("step(_result.value)", rendered);
        Assert.Contains("else _result", rendered);
        Assert.DoesNotContain("ok = true, value = step", rendered);
    }

    [Fact]
    public void MapErr_RebuildsTheErrorArmAndPassesTheOkArmThrough()
    {
        var rendered = Render("""
            fn rename(message: string): string {
                return "renamed: " + message;
            }

            let renamed = fetch().map_err(rename);
            """);

        Assert.Contains("rename(_result.error)", rendered);
        Assert.Contains("ok = false", rendered);
        Assert.Contains("if _result.ok then _result else", rendered);
    }

    [Fact]
    public void OrElse_ChainsWithoutRewrappingTheOkArm()
    {
        var rendered = Render("""
            fn recover(message: string): Result<number, string> {
                return BaseResult::ok(0);
            }

            let recovered = fetch().or_else(recover);
            """);

        Assert.Contains("recover(_result.error)", rendered);
        Assert.Contains("if _result.ok then _result else", rendered);
    }

    [Fact]
    public void Combinators_EvaluateTheReceiverOnlyOnce()
    {
        var rendered = Render("let n = fetch().unwrap();");

        Assert.Equal(1, CountOccurrences(rendered, "= fetch()"));
    }

    [Fact]
    public void Combinators_TypeCheckAgainstTheResultsValueType()
    {
        const string good = """
            [fallible]
            fn take(): void {
                let n: number = fetch().unwrap();
            }
            """;

        const string bad = """
            [fallible]
            fn take(): void {
                let s: string = fetch().unwrap();
            }
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(Fetch + good));
        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(Fetch + "let n: number = fetch().unwrap_or(0);"));

        Utility.AssertDiagnostic(
            Utility.GetTypeCheckerDiagnostics(Fetch + bad),
            InternalCodes.TypeMismatch,
            "Type 'number' is not assignable to type 'string'."
        );
    }

    private static string Render(string body) => Utility.GetLuauAST(Fetch + body, typeCheck: true).Render();

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var index = haystack.IndexOf(needle, StringComparison.Ordinal); index >= 0; index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal))
            count++;

        return count;
    }
}
