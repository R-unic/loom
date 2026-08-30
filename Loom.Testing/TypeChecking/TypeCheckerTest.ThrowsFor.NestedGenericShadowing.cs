using Loom.Core.Diagnostics;
using Loom.Testing;


namespace Loom.Testing.TypeChecking;

public partial class TypeCheckerTest
{
    /// <remarks>
    ///     A function that declares a parameter of its own shadows the enclosing generic's. TypeParameter
    ///     equality is name-blind, so the two Ts below are indistinguishable by shape - binding the inner one
    ///     to whatever Box was instantiated with typed the call by the interface's argument instead of its
    ///     own, silently and with no diagnostic.
    /// </remarks>
    [Fact]
    public void Checks_ANestedGenericsOwnTypeParameter_ShadowsTheEnclosingOne()
    {
        var type = Utility.GetLastStatementType(
            """
            interface Box<T> {
                value: T;
                map: fn<T>(other: T): T;
            }

            declare let b: Box<number>;
            b.map::<string>("hi")
            """
        );

        Assert.Equal("string", type.ToString());
    }

    [Fact]
    public void ThrowsFor_ANestedGenericCall_MismatchingItsOwnTypeArgument()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface Box<T> {
                value: T;
                map: fn<T>(other: T): T;
            }

            declare let b: Box<number>;
            let bad: number = b.map::<string>("hi");
            """
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type 'string' is not assignable to type 'number'.");
    }

    /// <remarks>A function that only <em>uses</em> the enclosing parameter does not declare it, so it still binds.</remarks>
    [Fact]
    public void Checks_ANestedFunctionUsingTheEnclosingTypeParameter_StillBindsIt()
    {
        var type = Utility.GetLastStatementType(
            """
            interface Box<T> {
                value: T;
                get: fn(): T;
            }

            declare let b: Box<number>;
            b.get()
            """
        );

        Assert.Equal("number", type.ToString());
    }
}
