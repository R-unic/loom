using Loom.Core.Diagnostics;
using Loom.Core.TypeChecking.Types;
using Loom.Testing;


namespace Loom.Testing.TypeChecking;

public partial class TypeCheckerTest
{
    [Fact]
    public void Checks_TupleLiteral_InfersPositionalElementTypes()
    {
        var type = Utility.GetLastStatementType("(\"abc\", 420)");
        var tuple = Assert.IsType<TupleType>(type);
        Assert.Equal(2, tuple.ElementTypes.Count);
        Assert.Equal(new LiteralType("abc"), tuple.ElementTypes[0]);
        Assert.Equal(new LiteralType(420L), tuple.ElementTypes[1]);
    }

    [Fact]
    public void Checks_TupleIndex_LiteralReturnsExactElementType()
    {
        var type = Utility.GetLastStatementType("let t: (string, number) = (\"abc\", 420); t[1];");
        Assert.Equal(PrimitiveType.String, type);
    }

    [Fact]
    public void Checks_TupleIndex_LiteralReturnsExactElementType_SecondPosition()
    {
        var type = Utility.GetLastStatementType("let t: (string, number) = (\"abc\", 420); t[2];");
        Assert.Equal(PrimitiveType.Number, type);
    }

    [Fact]
    public void Checks_TupleIndex_NonLiteralIndex_ReturnsElementUnion()
    {
        var type = Utility.GetLastStatementType("let t: (string, number) = (\"abc\", 420); let i: number = 1; t[i];");
        var union = Assert.IsType<UnionType>(type);
        Assert.Contains(PrimitiveType.String, union.Types);
        Assert.Contains(PrimitiveType.Number, union.Types);
    }

    [Fact]
    public void ThrowsFor_TupleIndex_OutOfRange()
    {
        const string source = """
            let t: (string, number) = ("abc", 420);
            t[3];
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TupleIndexOutOfRange,
            "Index 3 is out of range for tuple type '(string, number)' with 2 element(s)."
        );
    }

    [Fact]
    public void Checks_TupleVariable_DeclaredTypeChecksLiteralElements()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let t: (string, number) = (\"abc\", 420);");
        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void ThrowsFor_TupleLiteral_ElementTypeMismatch()
    {
        const string source = "let t: (string, number) = (420, \"abc\");";
        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '420' is not assignable to type 'string'.");
    }

    [Fact]
    public void ThrowsFor_TupleLiteral_ArityMismatch()
    {
        const string source = "let t: (string, number) = (\"abc\", 420, true);";
        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TupleArityMismatch,
            "Tuple type '(string, number)' expects 2 element(s), but 3 were provided."
        );
    }

    [Fact]
    public void Checks_FunctionReturnType_TupleLiteral()
    {
        const string source = """
            fn returns_tuple: (string, number) {
                return ("abc", 420);
            }
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
    }

    [Fact]
    public void ThrowsFor_FunctionReturnType_TupleArityMismatch()
    {
        const string source = """
            fn returns_tuple: (string, number) {
                return ("abc", 420, true);
            }
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TupleArityMismatch,
            "Tuple type '(string, number)' expects 2 element(s), but 3 were provided."
        );
    }

    [Fact]
    public void Checks_TuplePattern_BindsElementTypesPositionally()
    {
        const string source = """
            let t: (string, number) = ("abc", 420);
            let (one, two) = t;
            two;
            """;

        var type = Utility.GetLastStatementType(source);
        Assert.Equal(PrimitiveType.Number, type);
    }

    [Fact]
    public void ThrowsFor_TuplePattern_ArityMismatch()
    {
        const string source = """
            let t: (string, number) = ("abc", 420);
            let (one, two, three) = t;
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TupleArityMismatch,
            "Tuple type '(string, number)' expects 2 element(s), but 3 were provided."
        );
    }

    [Fact]
    public void ThrowsFor_TuplePattern_NonTupleSource()
    {
        const string source = """
            let n: number = 1;
            let (one, two) = n;
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidDestructureSource, "Cannot destructure value of type 'number' with a tuple pattern.");
    }

    [Fact]
    public void Checks_TupleConstraint_AcceptsTupleArgument() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                declare fn something<T: Tuple>(..args: T): void;
                something::<(string, number)>("abc", 420);
                """
            )
        );

    [Fact]
    public void ThrowsFor_TupleConstraint_RejectsNonTuple()
    {
        const string source = """
            declare fn something<T: Tuple>(..args: T): void;
            something::<number[]>(1, 2);
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.ConstraintViolation,
            "Type 'number[]' does not satisfy constraint 'Tuple' for type parameter 'T'."
        );
    }

    [Fact]
    public void Checks_TupleRest_ExactArity_Ok() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                declare fn something<T: Tuple>(..args: T): void;
                something::<(string, number, bool)>("abc", 420, true);
                """
            )
        );

    [Fact]
    public void ThrowsFor_TupleRest_WrongArity()
    {
        const string source = """
            declare fn something<T: Tuple>(..args: T): void;
            something::<(string, number)>("abc");
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TupleRestArityMismatch,
            "Tuple rest parameter expects exactly 2 arguments, but 1 were provided."
        );
    }

    [Fact]
    public void ThrowsFor_TupleRest_PositionalTypeMismatch()
    {
        const string source = """
            declare fn something<T: Tuple>(..args: T): void;
            something::<(string, number)>("abc", "def");
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '\"def\"' is not assignable to type 'number'.");
    }

    [Fact]
    public void ThrowsFor_RestParameter_TupleTypeParameter_WithoutTupleConstraint()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("declare fn something<T>(..args: T): void;");
        var diagnostic = diagnostics.Find(d => d.Code == InternalCodes.InvalidRestParameterType);
        Assert.NotNull(diagnostic);
    }

    [Fact]
    public void Checks_MatchTuplePattern_BindsElementTypesPositionally()
    {
        const string source = """
            let t: (string, number) = ("abc", 420);
            match t {
                (a, b) -> b,
                _ -> 0,
            };
            """;

        var type = Utility.GetLastStatementType(source);
        Assert.Equal(PrimitiveType.Number, type);
    }

    [Fact]
    public void ThrowsFor_MatchTuplePattern_ArityMismatch()
    {
        const string source = """
            let t: (string, number) = ("abc", 420);
            match t {
                (a, b, c) -> a,
                _ -> "none",
            };
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TupleArityMismatch,
            "Tuple type '(string, number)' expects 2 element(s), but 3 were provided."
        );
    }

    [Fact]
    public void ThrowsFor_MatchTuplePattern_NonTupleScrutinee()
    {
        const string source = """
            let n: number = 1;
            match n {
                (a, b) -> a,
                _ -> "none",
            };
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Tuple pattern cannot match value of type 'number'.");
    }

    [Fact]
    public void Checks_MatchTuplePattern_NestedTuplePattern()
    {
        const string source = """
            let t: (string, (number, bool)) = ("abc", (420, true));
            match t {
                (a, (b, c)) -> c,
                _ -> false,
            };
            """;

        var type = Utility.GetLastStatementType(source);
        Assert.Equal(PrimitiveType.Bool, type);
    }
}
