using Loom.Core.Diagnostics;
using Loom.Core.TypeChecking.Types;

namespace Loom.Testing;

[Collection("Assembly")]
public class ErrorHandlingIntrinsicsTest
{
    [Fact]
    public void RobloxError_IsAvailableAsAResultErrorType()
    {
        const string source = """
            fn fetch(): Result<number, RobloxError> {
                return Result::ok(1);
            }

            let outcome = fetch();
            outcome.ok ? outcome.value : 0
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
        Assert.True(Utility.GetLastStatementType(source).IsAssignableTo(PrimitiveType.Number));
    }

    [Fact]
    public void RobloxError_CarriesAMessageAndAnOptionalTraceback()
    {
        const string source = """
            fn describe(error: RobloxError): string {
                return error.message;
            }

            fn trace(error: RobloxError): string? {
                return error.traceback;
            }
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
    }

    [Fact]
    public void RobloxError_TracebackIsOptionalSoItCannotBeUsedAsAString()
    {
        const string source = """
            fn trace(error: RobloxError): string {
                return error.traceback;
            }
            """;

        Utility.AssertDiagnostic(
            Utility.GetTypeCheckerDiagnostics(source),
            InternalCodes.TypeMismatch,
            "Type 'string?' is not assignable to type 'string'."
        );
    }

    [Fact]
    public void Fallible_AppliesToAFunctionAndEmitsNoDecorator()
    {
        const string source = """
            [fallible]
            fn risky(): number {
                return 1;
            }
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));

        var rendered = Utility.GetLuauAST(source).Render();
        Assert.DoesNotContain("decorator", rendered);
        Assert.DoesNotContain("fallible", rendered);
        Assert.Contains("function risky", rendered);
    }

    [Fact]
    public void Deprecated_AppliesBareOrWithAMessageAndEmitsNoDecorator()
    {
        const string bare = """
            [deprecated]
            fn old_way(): number {
                return 1;
            }
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(bare));

        const string withMessage = """
            [deprecated("use new_way instead")]
            fn old_way(): number {
                return 1;
            }
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(withMessage));

        var rendered = Utility.GetLuauAST(withMessage).Render();
        Assert.DoesNotContain("decorator", rendered);
        Assert.DoesNotContain("deprecated", rendered);
    }

    [Fact]
    public void Fallible_CannotBeAppliedToAnInterface()
    {
        const string source = """
            [fallible]
            interface Thing {
                value: number;
            }
            """;

        Utility.AssertDiagnostic(
            Utility.GetTypeCheckerDiagnostics(source),
            InternalCodes.AttributeTargetNotAllowed,
            "Attribute 'fallible' is not valid on Interface."
        );
    }
}
