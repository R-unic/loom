using NuLua;
using NuLua.Luau;

namespace Loom.Testing;

/// <summary>
///     Executes the lowered combinators instead of only reading them. A snapshot proves the output
///     did not change; this proves it computes the right answer and short-circuits on the error arm.
/// </summary>
/// <remarks>Loom spells an immutable binding <c>const</c>, which Luau has no keyword for.</remarks>
[Collection("Assembly")]
public class ResultCombinatorRuntimeTest
{
    private const string Helpers = """
        fn double(n: number): number {
            return n * 2;
        }

        fn step(n: number): Result<number, string> {
            return BaseResult::ok(n + 1);
        }

        fn recover(message: string): number {
            return 3;
        }

        fn ok(n: number): Result<number, string> {
            return BaseResult::ok(n);
        }

        fn err(message: string): Result<number, string> {
            return BaseResult::err(message);
        }

        fn upper(message: string): string {
            return message.upper();
        }

        fn recovered(message: string): Result<number, string> {
            return BaseResult::ok(9);
        }


        """;

    [Theory]
    [InlineData("ok(21).map(double).unwrap()", "42")]
    [InlineData("ok(21).and_then(step).unwrap()", "22")]
    [InlineData("ok(21).unwrap_or(0)", "21")]
    [InlineData("ok(21).expect(\"unreachable\")", "21")]
    [InlineData("err(\"bad\").unwrap_or(7)", "7")]
    [InlineData("err(\"bad\").unwrap_or_else(recover)", "3")]
    [InlineData("err(\"bad\").map(double).ok", "false")]
    [InlineData("err(\"bad\").and_then(step).error", "bad")]
    [InlineData("ok(21).map_err(upper).unwrap()", "21")]
    [InlineData("err(\"bad\").map_err(upper).error", "BAD")]
    [InlineData("ok(21).or_else(recovered).unwrap()", "21")]
    [InlineData("err(\"bad\").or_else(recovered).unwrap()", "9")]
    [InlineData("err(\"bad\").unwrap_err()", "bad")]
    [InlineData("err(\"bad\").expect_err(\"unreachable\")", "bad")]
    public void Executes(string expression, string expected)
    {
        var returned = Run($"{Helpers}let outcome = {expression};", "return tostring(outcome)");

        Assert.Equal(expected, returned);
    }

    [Fact]
    public void UnwrapOnTheErrorArmRaisesTheError()
    {
        var exception = Assert.ThrowsAny<Exception>(() => Run($"{Helpers}let outcome = err(\"exploded\").unwrap();", "return 0"));

        Assert.Contains("exploded", exception.Message);
    }

    [Fact]
    public void ExpectOnTheErrorArmRaisesTheSuppliedMessage()
    {
        var exception = Assert.ThrowsAny<Exception>(() => Run($"{Helpers}let outcome = err(\"bad\").expect(\"could not fetch\");", "return 0"));

        Assert.Contains("could not fetch", exception.Message);
    }

    [Fact]
    public void UnwrapErrOnTheOkArmRaisesTheValue()
    {
        var exception = Assert.ThrowsAny<Exception>(() => Run($"{Helpers}let outcome = ok(21).unwrap_err();", "return 0"));

        Assert.Contains("21", exception.Message);
    }

    [Fact]
    public void ExpectErrOnTheOkArmRaisesTheSuppliedMessage()
    {
        var exception = Assert.ThrowsAny<Exception>(() => Run($"{Helpers}let outcome = ok(21).expect_err(\"expected a failure\");", "return 0"));

        Assert.Contains("expected a failure", exception.Message);
    }

    [Fact]
    public void MapErrOnTheOkArmDoesNotCallTheTransform()
    {
        const string source = """
            fn boom(message: string): string {
                error("transform ran");
                return message;
            }

            fn ok(n: number): Result<number, string> {
                return BaseResult::ok(n);
            }

            let outcome = ok(21).map_err(boom);
            """;

        Assert.Equal("true", Run(source, "return tostring(outcome.ok)"));
    }

    [Fact]
    public void OrElseOnTheOkArmDoesNotCallTheFallback()
    {
        const string source = """
            fn boom(message: string): Result<number, string> {
                error("fallback ran");
                return BaseResult::ok(0);
            }

            fn ok(n: number): Result<number, string> {
                return BaseResult::ok(n);
            }

            let outcome = ok(21).or_else(boom);
            """;

        Assert.Equal("21", Run(source, "return tostring(outcome.value)"));
    }

    [Fact]
    public void MapOnTheErrorArmDoesNotCallTheTransform()
    {
        const string source = """
            fn boom(n: number): number {
                error("transform ran");
                return n;
            }

            fn err(message: string): Result<number, string> {
                return BaseResult::err(message);
            }

            let outcome = err("bad").map(boom);
            """;

        Assert.Equal("false", Run(source, "return tostring(outcome.ok)"));
    }

    private static string Run(string source, string epilogue)
    {
        var luau = Utility.GetLuauAST(source, typeCheck: true).Render().Replace("const ", "local ");

        using var state = LuauState.Create();
        state.OpenLibraries();

        return state.DoString($"{luau}{Environment.NewLine}{epilogue}")[0].ToString();
    }
}
