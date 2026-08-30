using Loom.Core.Diagnostics;
using Loom.Testing;

namespace Loom.Testing.TypeChecking;

public partial class TypeCheckerTest
{
    [Fact]
    public void Checks_StringWithSizedTypeArgument_NoErrors() =>
        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics("let x: string<u8> = \"a\";"));

    [Fact]
    public void ThrowsFor_StringTypeArgument_NotSized()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let x: string<number> = \"a\";");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidTypeArguments,
            "string's length type must be a sized type like 'u8', but is 'number'."
        );
    }

    [Fact]
    public void ThrowsFor_StringTypeArgument_Signed()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let x: string<i8> = \"a\";");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidTypeArguments,
            "string's length type must be unsigned, but is 'I8'.",
            "lengths are never negative; use u8, u16, or u32."
        );
    }

    [Fact]
    public void ThrowsFor_StringTypeArgument_WrongCount()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let x: string<u8, u16> = \"a\";");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidTypeArguments,
            "'string' takes exactly one type argument, its length-prefix width."
        );
    }

    [Fact]
    public void Vector3WithTypeArgument_ParsesAndChecksWithNoErrors() =>
        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics("interface Holder { position: Vector3<i16> }"));

    [Fact]
    public void ArrayAlias_WithBothArguments_NoErrors() =>
        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics("let x: Array<string, u8> = [\"a\"];"));

    [Fact]
    public void ArrayAlias_WithOnlyElementArgument_DefaultsLengthType_NoErrors() =>
        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics("let x: Array<string> = [\"a\"];"));

    [Fact]
    public void ArrayAlias_IsAssignableToPlainArray() =>
        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics("let x: string[] = [\"a\"] as Array<string, u8>;"));
}
