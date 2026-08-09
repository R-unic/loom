using Loom.Core.Diagnostics;

namespace Loom.Testing;

[Collection("Assembly")]
public class ResultCombinatorTest
{
    private const string Fetch = """
        fn fetch(): Result<number, string> {
            return Result.ok(1);
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
                return Result.ok(n);
            }

            let chained = fetch().and_then(step);
            """);

        Assert.Contains("step(_result.value)", rendered);
        Assert.Contains("else _result", rendered);
        Assert.DoesNotContain("ok = true, value = step", rendered);
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
        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(Fetch + "let n: number = fetch().unwrap();"));
        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(Fetch + "let n: number = fetch().unwrap_or(0);"));

        Utility.AssertDiagnostic(
            Utility.GetTypeCheckerDiagnostics(Fetch + "let s: string = fetch().unwrap();"),
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
