using Loom.Core.Diagnostics;
using NuLua;
using NuLua.Luau;

namespace Loom.Testing.Generation;

/// <summary>
///     A bare, zero-required-argument function value already meets the "call it, stop at first nil"
///     protocol <see cref="IteratorTest" />'s <c>Iterator&lt;T&gt;</c> is wrapped to satisfy - which is
///     what lets a native Luau library's own closure-returning iterator (<c>pairs</c>, <c>each</c> in an
///     ECS library, and the like) drive a <c>for</c> loop directly, with no adapter.
/// </summary>
[Collection("Assembly")]
public class FunctionalIteratorTest
{
    [Fact]
    public void BindsTheLoopNameToTheFunctionsReturnType()
    {
        var type = Utility.GetLastStatementType(
            """
            declare fn make_counter(): fn(): number?;

            mut last: number? = none;
            for value : make_counter() {
                last = value;
            }

            last
            """
        );

        Assert.Equal("number?", type.ToString());
    }

    [Fact]
    public void BindsEachLoopName_ToTheCorrespondingTupleElement()
    {
        var type = Utility.GetLastStatementType(
            """
            declare fn make_pairs(): fn(): (number, string);

            mut lastKey = 0;
            mut lastValue = "";
            for key, value : make_pairs() {
                lastKey = key;
                lastValue = value;
            }

            lastValue
            """
        );

        Assert.Equal("string", type.ToString());
    }

    /// <remarks>Fewer names than the function returns is fine - the rest are simply discarded, as with any other collection.</remarks>
    [Fact]
    public void AllowsBindingFewerNames_ThanTheFunctionReturns() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                declare fn make_pairs(): fn(): (number, string);

                for key : make_pairs() {
                    print(key);
                }
                """
            )
        );

    [Fact]
    public void ReportsANameBeyondWhatTheFunctionReturns() =>
        Utility.AssertDiagnostic(
            Utility.GetTypeCheckerDiagnostics(
                """
                declare fn make_pairs(): fn(): (number, string);

                for key, value, extra : make_pairs() {
                    print(key);
                }
                """
            ),
            InternalCodes.NotImplemented,
            "This iterator function returns 2 value(s) per step, so at most 2 name(s) is permitted."
        );

    /// <remarks>Nothing calls a native for's iterator with meaningful arguments, so one that requires any could never be called correctly - it is rejected the same way any other non-iterable value is.</remarks>
    [Fact]
    public void RejectsAFunction_ThatRequiresArguments() =>
        Utility.AssertDiagnostic(
            Utility.GetTypeCheckerDiagnostics(
                """
                declare fn make_iterator(): fn(step: number): number;

                for value : make_iterator() {
                    print(value);
                }
                """
            ),
            InternalCodes.TypeMismatch,
            "Type 'fn(number): number' is not assignable to type 'object'."
        );

    [Fact]
    public void RunsToCompletion_OverAStatefulClosure() =>
        Run(
            """
            declare fn make_counter(): fn(): number?;

            mut total = 0;
            mut count = 0;
            for value : make_counter() {
                total += value;
                count += 1;
            }
            """,
            """
            local remaining = 3
            local function make_counter()
                return function()
                    if remaining <= 0 then
                        return nil
                    end
                    remaining -= 1
                    return remaining + 1
                end
            end
            """,
            """
            assert(count == 3, "three elements, got " .. count)
            assert(total == 6, "3 + 2 + 1, got " .. total)
            """
        );

    private static void Run(string source, string prelude, string assertions)
    {
        var emitted = Utility.GetLuauAST(source, true).Render();
        using var state = LuauState.Create();
        state.OpenLibraries();

        try
        {
            state.DoString($"{prelude}\n{Strip(emitted)}\n{assertions}\n");
        }
        catch (Exception exception)
        {
            Assert.Fail($"the emitted loop did not run: {exception.Message}\n\n{emitted}");
        }
    }

    /// <summary>
    ///     Drops what only the compiler's own output needs — the runtime require, the ambient
    ///     <c>declare fn</c> stub, and the type aliases, which name types the interpreter has no
    ///     declarations for. Neither has any bearing on how the loop runs.
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
