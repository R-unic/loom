using Loom.Core.Diagnostics;
using NuLua;
using NuLua.Luau;

namespace Loom.Testing;

/// <summary>
///     A type that implements <c>Iterator&lt;T&gt;</c> drives a <c>for</c> loop itself. The loop calls
///     <c>next</c> until it answers none, so the iterator carries its own position.
/// </summary>
/// <remarks>
///     The emitted loop is executed rather than only read: what has to be right is that Luau's generic
///     <c>for</c> stops at the first nil and that the receiver is evaluated once, and neither of those is
///     visible in the text of the output.
/// </remarks>
[Collection("Assembly")]
public class IteratorTest
{
    private const string Countdown = """
        interface Countdown { mut remaining: number }

        implement Iterator<number> for Countdown {
            fn next(): number? {
                if remaining <= 0 {
                    return none;
                }

                remaining -= 1;
                return remaining + 1;
            }
        }
        """;

    [Fact]
    public void ProducesEachElementUntilNextAnswersNone() =>
        Run(
            $$"""
            {{Countdown}}

            mut total = 0;
            mut count = 0;
            for value : new Countdown { remaining: 3 } {
                total += value;
                count += 1;
            }
            """,
            """
            assert(count == 3, "three elements, got " .. count)
            assert(total == 6, "3 + 2 + 1, got " .. total)
            """
        );

    /// <remarks>An iterator that starts exhausted runs the body no times rather than once.</remarks>
    [Fact]
    public void ProducesNothingWhenTheIteratorStartsExhausted() =>
        Run(
            $$"""
            {{Countdown}}

            mut count = 0;
            for value : new Countdown { remaining: 0 } {
                count += 1;
            }
            """,
            """
            assert(count == 0, "no elements, got " .. count)
            """
        );

    /// <summary>
    ///     The receiver is bound before the loop. Re-evaluating an expression that produces an iterator
    ///     would produce a fresh one every step, and the loop would never finish.
    /// </summary>
    /// <remarks>
    ///     This is also the case that a type reaching the loop through a return type has to get right: the
    ///     trait is implemented outside the interface's declaration, so nothing about it is in the type the
    ///     signature names unless the compiler puts it there.
    /// </remarks>
    [Fact]
    public void EvaluatesTheIteratorExpressionOnce() =>
        Run(
            $$"""
            {{Countdown}}

            mut made = 0;
            fn build(): Countdown {
                made += 1;
                return new Countdown { remaining: 2 };
            }

            mut count = 0;
            for value : build() {
                count += 1;
            }
            """,
            """
            assert(made == 1, "built once, got " .. made)
            assert(count == 2, "two elements, got " .. count)
            """
        );

    [Fact]
    public void BindsTheLoopNameToTheIteratedElementType()
    {
        var type = Utility.GetLastStatementType(
            $$"""
            {{Countdown}}

            mut last = 0;
            for value : new Countdown { remaining: 1 } {
                last = value;
            }

            last
            """
        );

        Assert.Equal("number", type.ToString());
    }

    /// <remarks>'next' answers one value, so there is no second name for the loop to bind.</remarks>
    [Fact]
    public void ReportsASecondLoopName() =>
        Utility.AssertDiagnostic(
            Utility.GetTypeCheckerDiagnostics(
                $$"""
                {{Countdown}}

                for value, index : new Countdown { remaining: 1 } {
                    print(value);
                }
                """
            ),
            InternalCodes.NotImplemented,
            "An iterator's 'next' answers one value, so only one name is permitted."
        );

    private static void Run(string source, string assertions)
    {
        var emitted = Utility.GetLuauAST(source, true).Render();
        using var state = LuauState.Create();
        state.OpenLibraries();

        try
        {
            state.DoString($"{Strip(emitted)}\n{assertions}\n");
        }
        catch (Exception exception)
        {
            Assert.Fail($"the emitted loop did not run: {exception.Message}\n\n{emitted}");
        }
    }

    /// <summary>
    ///     Drops what only the compiler's own output needs — the runtime require and the type aliases,
    ///     which name types the interpreter has no declarations for — and spells <c>const</c> the way Luau
    ///     does. Neither has any bearing on how the loop runs.
    /// </summary>
    private static string Strip(string emitted) =>
        string.Join(
                '\n',
                emitted.Split('\n')
                    .Where(line => !line.Contains("require(", StringComparison.Ordinal))
                    .Where(line => !line.StartsWith("type ", StringComparison.Ordinal))
                    .Where(line => !line.StartsWith("  ", StringComparison.Ordinal) || !line.TrimEnd().EndsWith(',') || !line.Contains(':'))
                    .Where(line => !line.StartsWith("} &", StringComparison.Ordinal))
            )
            .Replace("const ", "local ");
}
