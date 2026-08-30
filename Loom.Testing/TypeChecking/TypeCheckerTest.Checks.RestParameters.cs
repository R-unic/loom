using Loom.Core.Diagnostics;
using Loom.Core.TypeChecking.Types;


namespace Loom.Testing.TypeChecking;

public partial class TypeCheckerTest
{
    [Fact]
    public void Checks_DeclareFunction_WithRestParameter_HasArrayParameterType()
    {
        var type = Utility.GetLastStatementType("declare fn my_print(..data: unknown[]): void");
        var functionType = Assert.IsType<FunctionType>(type);
        Assert.True(functionType.HasRestParameter);
        Assert.IsType<ArrayType>(functionType.ParameterTypes.Single());
    }

    [Fact]
    public void Checks_RestParameterCall_AllowsManyArguments()
    {
        const string source = """
            declare fn my_print(..data: unknown[]): void;
            my_print(1, 2, 3, 4, 5, 6, 7, 8, 9, 10)
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Checks_RestParameterCall_AllowsZeroArguments()
    {
        const string source = """
            declare fn my_print(..data: unknown[]): void;
            my_print()
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Checks_RestParameterCall_WithLeadingFixedParameters()
    {
        const string source = """
            fn sum(..values: number[]): number {
                mut total = 0;
                for value : values
                    total += value;

                return total;
            }

            sum(1, 2, 3, 4, 5)
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void ThrowsFor_RestParameterCall_ArgumentNotAssignableToElementType()
    {
        const string source = """
            fn sum(..values: number[]): number -> 0
            sum(1, "two", 3)
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '\"two\"' is not assignable to type 'number'.");
    }

    [Fact]
    public void ThrowsFor_RestParameter_NotLast()
    {
        var diagnostics = Utility.GetParserDiagnostics("fn foo(..a: number[], b: number) { }");
        var diagnostic = diagnostics.Find(d => d.Code == InternalCodes.RestParameterNotLast);
        Assert.NotNull(diagnostic);
    }

    [Fact]
    public void ThrowsFor_RestParameter_NonArrayType()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("declare fn foo(..a: number): void");
        var diagnostic = diagnostics.Find(d => d.Code == InternalCodes.InvalidRestParameterType);
        Assert.NotNull(diagnostic);
    }

    /// <summary>
    ///     A rest argument infers against the rest parameter's <em>element</em> type. Comparing it against the
    ///     array itself matches no inference rule, so the type parameter used to fall through to 'unknown'.
    /// </summary>
    [Fact]
    public void Checks_Inference_RestParameter_InfersElementTypeFromArguments()
    {
        var type = Utility.GetLastStatementType("declare fn of<T>(..values: T[]): T[]; of(1, 2)");
        var arrayType = Assert.IsType<ArrayType>(type);
        Assert.Equal(PrimitiveType.Number, arrayType.ElementType);
    }

    [Fact]
    public void Checks_Inference_RestParameter_AfterFixedParameter_InfersFromBoth()
    {
        var type = Utility.GetLastStatementType("declare fn of<T>(first: T, ..rest: T[]): T[]; of(1, 2, 3)");
        var arrayType = Assert.IsType<ArrayType>(type);
        Assert.Equal(PrimitiveType.Number, arrayType.ElementType);
    }

    [Fact]
    public void Checks_Inference_RestParameter_WithNoArguments_FallsBackToUnknown()
    {
        var type = Utility.GetLastStatementType("declare fn of<T>(..values: T[]): T[]; of()");
        var arrayType = Assert.IsType<ArrayType>(type);
        Assert.Equal(PrimitiveType.Unknown, arrayType.ElementType);
    }

    /// <summary>A tuple rest answers per position, so each argument infers a different type parameter.</summary>
    [Fact]
    public void Checks_Inference_TupleRestParameter_InfersEachPositionSeparately()
    {
        const string source = """
            declare fn pair<A, B>(..values: (A, B)): (B, A);
            pair(1, "x")
            """;

        var tupleType = Assert.IsType<TupleType>(Utility.GetLastStatementType(source));
        Assert.Equal([new LiteralType("x"), new LiteralType(1L)], tupleType.ElementTypes);
    }

    [Fact]
    public void ThrowsFor_RestParameterCall_WithArgumentOfWrongElementType()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("""declare fn nums(..values: number[]): void; nums(1, "a")""");
        Assert.Contains(diagnostics.Set, d => d.Code == InternalCodes.TypeMismatch);
    }
}
