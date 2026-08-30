using Loom.Core.Diagnostics;
using Loom.Core.TypeChecking.Types;
using Loom.Testing;

namespace Loom.Testing.TypeChecking;

public partial class TypeCheckerTest
{
    [Fact]
    public void Checks_FunctionExpression_InfersReturnTypeFromBlockBody()
    {
        const string source = "let f = fn(x: number) { return x + 1; };";
        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));

        var functionType = Assert.IsType<FunctionType>(Utility.GetLastStatementType(source));
        Assert.True(functionType.ReturnType.Equals(PrimitiveType.Number));
    }

    [Fact]
    public void Checks_FunctionExpression_InfersReturnTypeFromArrowBody()
    {
        const string source = "let f = fn(x: number) -> x + 1;";
        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));

        var functionType = Assert.IsType<FunctionType>(Utility.GetLastStatementType(source));
        Assert.True(functionType.ReturnType.Equals(PrimitiveType.Number));
    }

    [Fact]
    public void Checks_FunctionExpression_AsHigherOrderArgument() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                fn call_with_ten(f: fn(n: number): number): number {
                    return f(10);
                }
                call_with_ten(fn(n: number): number { return n * 2; })
                """
            )
        );

    [Fact]
    public void Checks_FunctionExpression_CapturesOuterVariable()
    {
        const string source = """
            let x = 5;
            let f = fn(): number { return x; };
            f()
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
        Assert.True(Utility.GetLastStatementType(source).Equals(PrimitiveType.Number));
    }

    [Fact]
    public void ThrowsFor_FunctionExpression_ReturnTypeMismatch()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let f = fn(): number { return \"oops\"; };");
        Assert.Contains(diagnostics.Set, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Checks_FunctionExpression_ReturnedFromFunctionDeclaration_TypesAsNestedFunctionType()
    {
        const string source = "fn make_adder(x: number) -> fn(y: number): number { return x + y; };";
        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));

        var outer = Assert.IsType<FunctionType>(Utility.GetLastStatementType(source));
        var inner = Assert.IsType<FunctionType>(outer.ReturnType);
        Assert.True(inner.ReturnType.Equals(PrimitiveType.Number));
    }
}
