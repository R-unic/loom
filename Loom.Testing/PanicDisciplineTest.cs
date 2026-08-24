using Loom.Core.Diagnostics;

namespace Loom.Testing;

[Collection("Assembly")]
public class PanicDisciplineTest
{
    private const string Fetch = """
        fn fetch(): Result<number, string> {
            return BaseResult::ok(1);
        }


        """;

    [Fact]
    public void Unwrap_InsideAFallibleFunction_IsAllowed() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                Fetch + """
                    [fallible]
                    fn load(): number {
                        return fetch().unwrap();
                    }
                    """
            )
        );

    [Fact]
    public void Unwrap_InsideAPlainFunction_NamesTheFunctionAndOffersBothFixes() =>
        Utility.AssertDiagnostic(
            Utility.GetTypeCheckerDiagnostics(
                Fetch + """
                    fn load(): number {
                        return fetch().unwrap();
                    }
                    """
            ),
            InternalCodes.PanicOutsideFallibleFunction,
            "'unwrap' can panic, but 'load' is not marked '[fallible]'.",
            "return a 'Result<T, Error>' and propagate with '?', or mark 'load' with '[fallible]' if you really need to panic"
        );

    [Fact]
    public void Unwrap_AtTopLevel_OmitsTheFallibleSuggestion() =>
        Utility.AssertDiagnostic(
            Utility.GetTypeCheckerDiagnostics(Fetch + "let n = fetch().unwrap();"),
            InternalCodes.PanicOutsideFallibleFunction,
            "'unwrap' can panic, and this code cannot recover from it.",
            "handle the error instead - 'match', 'unwrap_or', or move this into a function returning 'Result<T, Error>'"
        );

    [Fact]
    public void Expect_IsPanicking() =>
        Utility.AssertDiagnostic(
            Utility.GetTypeCheckerDiagnostics(Fetch + """let n = fetch().expect("boom");"""),
            InternalCodes.PanicOutsideFallibleFunction,
            "'expect' can panic, and this code cannot recover from it."
        );

    [Fact]
    public void Error_IsPanicking()
    {
        Utility.AssertDiagnostic(
            Utility.GetTypeCheckerDiagnostics("""error("boom");"""),
            InternalCodes.PanicOutsideFallibleFunction,
            "'error' can panic, and this code cannot recover from it."
        );

        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                [fallible]
                fn boom(): void {
                    error("boom");
                }
                """
            )
        );
    }

    [Fact]
    public void CallingAFallibleFunction_IsItselfPanicking()
    {
        const string source = """
            [fallible]
            fn risky(): number {
                error("boom");
                return 1;
            }

            fn caller(): number {
                return risky();
            }
            """;

        Utility.AssertDiagnostic(
            Utility.GetTypeCheckerDiagnostics(source),
            InternalCodes.PanicOutsideFallibleFunction,
            "'risky' can panic, but 'caller' is not marked '[fallible]'."
        );
    }

    [Fact]
    public void CallingAFallibleFunction_FromAFallibleFunction_IsAllowed()
    {
        const string source = """
            [fallible]
            fn risky(): number {
                error("boom");
                return 1;
            }

            [fallible]
            fn caller(): number {
                return risky();
            }
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
    }

    [Fact]
    public void PropagatingWithTheQuestionOperator_IsNotPanicking() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                Fetch + """
                    fn load(): Result<number, string> {
                        let n = fetch()?;
                        return BaseResult::ok(n);
                    }
                    """
            )
        );

    [Fact]
    public void NonPanickingCombinators_AreAllowedAnywhere() =>
        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(Fetch + "let n = fetch().unwrap_or(0);"));

    [Fact]
    public void InsideAnEventHandler_TheFallibleSuggestionIsOmitted()
    {
        const string source = """
            fn fetch(): Result<number, string> {
                return BaseResult::ok(1);
            }

            [fallible]
            fn outer(): void {
                let handler = fn(): void {
                    let n = fetch().unwrap();
                };
            }
            """;

        Utility.AssertDiagnostic(
            Utility.GetTypeCheckerDiagnostics(source),
            InternalCodes.PanicOutsideFallibleFunction,
            "'unwrap' can panic, and this code cannot recover from it."
        );
    }
}
