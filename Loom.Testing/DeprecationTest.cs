using Loom.Core.Diagnostics;

namespace Loom.Testing;

[Collection("Assembly")]
public class DeprecationTest
{
    [Fact]
    public void CallingADeprecatedFunction_Warns()
    {
        const string source = """
            [deprecated]
            fn old_way(): number {
                return 1;
            }

            let n = old_way();
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertNoErrors(diagnostics);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.DeprecatedMember, "'old_way' is deprecated.");
    }

    [Fact]
    public void TheWarningCarriesTheSuppliedMessageAsItsHint()
    {
        const string source = """
            [deprecated("use new_way instead")]
            fn old_way(): number {
                return 1;
            }

            let n = old_way();
            """;

        Utility.AssertDiagnostic(
            Utility.GetTypeCheckerDiagnostics(source),
            InternalCodes.DeprecatedMember,
            "'old_way' is deprecated.",
            "use new_way instead"
        );
    }

    [Fact]
    public void CallingANonDeprecatedFunction_DoesNotWarn()
    {
        const string source = """
            fn new_way(): number {
                return 1;
            }

            let n = new_way();
            """;

        Assert.DoesNotContain(
            Utility.GetTypeCheckerDiagnostics(source).Set,
            diagnostic => diagnostic.Code == InternalCodes.DeprecatedMember
        );
    }

    [Fact]
    public void DeprecationIsAWarningRatherThanAnError()
    {
        const string source = """
            [deprecated]
            fn old_way(): number {
                return 1;
            }

            let n = old_way();
            """;

        var deprecation = Assert.Single(Utility.GetTypeCheckerDiagnostics(source).Set, d => d.Code == InternalCodes.DeprecatedMember);
        Assert.Equal(DiagnosticSeverity.Warn, deprecation.Severity);
    }
}
