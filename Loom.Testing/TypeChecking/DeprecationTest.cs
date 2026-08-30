using Loom.Core.Diagnostics;
using Loom.Testing;

namespace Loom.Testing.TypeChecking;

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

    [Fact]
    public void ReadingADeprecatedPropertyWarnsToo()
    {
        const string source = """
            declare interface Legacy {
                [deprecated("use replacement instead")]
                old_field: number;
                replacement: number;
            }

            declare let legacy: Legacy;
            let n = legacy.old_field;
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertNoErrors(diagnostics);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.DeprecatedMember, "'old_field' is deprecated.", "use replacement instead");
    }

    [Fact]
    public void ReadingANonDeprecatedPropertyDoesNotWarn()
    {
        const string source = """
            declare interface Legacy {
                [deprecated]
                old_field: number;
                replacement: number;
            }

            declare let legacy: Legacy;
            let n = legacy.replacement;
            """;

        Assert.DoesNotContain(
            Utility.GetTypeCheckerDiagnostics(source).Set,
            diagnostic => diagnostic.Code == InternalCodes.DeprecatedMember
        );
    }
}
