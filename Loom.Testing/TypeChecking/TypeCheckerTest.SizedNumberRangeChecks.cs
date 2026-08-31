using Loom.Core.Diagnostics;

namespace Loom.Testing.TypeChecking;

public partial class TypeCheckerTest
{
    [Fact]
    public void WarnsFor_SizedNumberLiteral_AboveMaximum()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let x: u8 = 420;");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.NumberOutOfRange, "'420' is out of range for 'u8' (0 to 255).");
    }

    [Fact]
    public void WarnsFor_SizedNumberLiteral_BelowMinimum()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let x: i8 = -200;");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.NumberOutOfRange, "'-200' is out of range for 'i8' (-128 to 127).");
    }

    [Fact]
    public void WarnsFor_UnsignedSizedNumberLiteral_Negative()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let x: u8 = -1;");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.NumberOutOfRange, "'-1' is out of range for 'u8' (0 to 255).");
    }

    [Fact]
    public void WarnsFor_SizedNumberArgument_OutOfRange()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            fn takes(x: u8): void {}
            takes(999);
            """
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.NumberOutOfRange, "'999' is out of range for 'u8' (0 to 255).");
    }

    [Fact]
    public void WarnsFor_SizedNumberPropertyInitializer_OutOfRange()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface Foo { x: u8 }
            let foo = new Foo { x: 300 };
            """
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.NumberOutOfRange, "'300' is out of range for 'u8' (0 to 255).");
    }

    [Fact]
    public void DoesNotWarnFor_SizedNumberLiteral_InRange() =>
        Assert.DoesNotContain(Utility.GetTypeCheckerDiagnostics("let x: u8 = 200;").Set, d => d.Code == InternalCodes.NumberOutOfRange);

    [Fact]
    public void DoesNotWarnFor_PlainNumberLiteral_OutOfSizedRange() =>
        Assert.DoesNotContain(Utility.GetTypeCheckerDiagnostics("let x: number = 420;").Set, d => d.Code == InternalCodes.NumberOutOfRange);
}
