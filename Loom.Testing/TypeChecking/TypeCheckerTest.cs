using Loom.Core.Diagnostics;
using Loom.Testing;

namespace Loom.Testing.TypeChecking;

[Collection("Assembly")]
public partial class TypeCheckerTest
{
    [Fact]
    public void ThrowsFor_MacroReference_InVariableDeclaration()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let x = Result::ok;");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidMacroReference,
            "Invocation macro 'ok' cannot be used as a value. Call it directly (e.g. ok(...)) or pass it as a function argument."
        );
    }

    [Fact]
    public void ThrowsFor_MacroReference_InArrayLiteral()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let x = [Result::ok];");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidMacroReference,
            "Invocation macro 'ok' cannot be used as a value. Call it directly (e.g. ok(...)) or pass it as a function argument."
        );
    }

    [Fact]
    public void Allows_UseAfterIf_WhenElseBranchTerminates()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            fn test(c: bool): number {
                mut x: number;
                if c {
                    x = 1;
                } else {
                    return 0;
                }
                return x + 1;
            }
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Allows_MacroReference_AsFunctionArgument()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            declare fn consume<T, E>(callback: fn(value: T): Result<T, E>): void;
            consume(Result::ok);
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void WarnsFor_NullCoalescing_NonOptional()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("1 ?? 2");
        Assert.Contains(diagnostics.Set, d => d.Code == InternalCodes.RedundantCode);
    }

    [Fact]
    public void WarnsFor_UseRangeLiteral()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("new Range { minimum: 69, maximum: 420 }");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.SimplifiableCode, "Use a range literal.");
    }

    [Fact]
    public void WarnsFor_ToSetOnAnArrayLiteral()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("[1, 2, 1].to_set();");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.SimplifiableCode, "Use 'Set::of(...)' instead of '.to_set()' on an array literal.");
    }

    [Theory]
    [InlineData("let a = [1, 2, 1]; a.to_set();")]
    [InlineData("fn source(): number[] -> [1, 2, 1]; source().to_set();")]
    [InlineData("let a = [1, 2]; [1, 2, ..a].to_set();")]
    public void DoesNotWarnFor_ToSetOnAnythingElse(string source)
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Assert.DoesNotContain(diagnostics.Set, d => d.Code == InternalCodes.SimplifiableCode);
    }












    [Fact]
    public void Checks_WaitForChild_IsGuaranteedWithoutATimeoutAndOptionalWithOne()
    {
        const string guaranteed = """
            async fn f(instance: Instance): Part {
                return await instance.wait_for_child::<Part>("Torso");
            }
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(guaranteed));

        const string timedOut = """
            async fn f(instance: Instance): Part {
                return await instance.wait_for_child::<Part>("Torso", 5);
            }
            """;

        Utility.AssertDiagnostic(
            Utility.GetTypeCheckerDiagnostics(timedOut),
            InternalCodes.TypeMismatch,
            "Type 'Part?' is not assignable to type 'Part'."
        );
    }

}
