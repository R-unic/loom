using Loom.Core.Diagnostics;
using Loom.Core.TypeChecking.Types;
using Loom.Testing;

namespace Loom.Testing.TypeChecking;

public partial class TypeCheckerTest
{
    [Fact]
    public void Checks_IsExpression_TypesAsBool()
    {
        const string source = """
            interface Foo {}
            let value = none as never as Foo;
            value is Foo
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
        Assert.True(Utility.GetLastStatementType(source).Equals(PrimitiveType.Bool));
    }

    [Fact]
    public void Narrows_VariableType_FromIsExpression() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                interface Foo { some_field: number }
                let value = none as never as Foo | number;
                if value is Foo {
                    value.some_field
                }
                """
            )
        );

    [Fact]
    public void Narrows_VariableType_FromIsExpression_ElseBranch() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                interface Foo { some_field: number }
                let value = none as never as Foo | number;
                if value is number {
                    value + 1
                } else {
                    value.some_field
                }
                """
            )
        );

    [Fact]
    public void Checks_IsExpression_ObjectPatternField_BindsNarrowedType() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                interface Foo { some_field: number }
                let value = none as never as Foo;
                if value is Foo { some_field: x } {
                    x + 1
                }
                """
            )
        );

    [Fact]
    public void ThrowsFor_IsExpression_IncompatiblePattern()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            mut value: string = "hello";
            value is number
            """
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Pattern of type 'number' cannot match value of type 'string'.");
    }

    [Fact]
    public void Checks_IsNotExpression_TypesAsBool()
    {
        const string source = """
            interface Foo {}
            let value = none as never as Foo;
            value is not Foo
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
        Assert.True(Utility.GetLastStatementType(source).Equals(PrimitiveType.Bool));
    }

    [Fact]
    public void Narrows_VariableType_FromIsNotExpression() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                interface Foo { some_field: number }
                let value = none as never as Foo | number;
                if value is not Foo {
                    value + 1
                }
                """
            )
        );

    [Fact]
    public void Narrows_VariableType_FromIsNotExpression_ElseBranch() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                interface Foo { some_field: number }
                let value = none as never as Foo | number;
                if value is not number {
                    value.some_field
                } else {
                    value + 1
                }
                """
            )
        );

    [Fact]
    public void ThrowsFor_IsNotExpression_IncompatiblePattern()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            mut value: string = "hello";
            value is not number
            """
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Pattern of type 'number' cannot match value of type 'string'.");
    }
}
