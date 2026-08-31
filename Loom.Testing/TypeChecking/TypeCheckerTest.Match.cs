using Loom.Core.Diagnostics;
using Loom.Core.TypeChecking.Types;

namespace Loom.Testing.TypeChecking;

public partial class TypeCheckerTest
{
    [Fact]
    public void Allows_Match_LiteralAndWildcardArms()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("""match 1 { 0 -> "zero", _ -> "other" }""");
        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Allows_Match_IdentifierBinding()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("match 1 { x -> x }");
        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Allows_Match_LetPatternBinding()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("match 1 { let name -> name }");
        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Allows_Match_TypedPattern_NarrowsBinding()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            let value: string | number = "hi";
            match value {
                text when string -> text,
                n when number -> n,
                _ -> 0,
            }
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Allows_Match_TypePattern_BindsObjectFields()
    {
        var type = Utility.GetLastStatementType(
            """
            interface Foo { field: number }
            let x: Foo = new Foo { field: 1 };
            match x {
                Foo { field } -> field,
                _ -> 0,
            }
            """
        );

        Assert.Equal(PrimitiveType.Number, type.Widen());
    }

    [Fact]
    public void ThrowsFor_Match_TypePattern_DoesNotBindOuterName()
    {
        // unlike a typed pattern ('f when Foo'), a bare type pattern captures nothing under a name of
        // its own - the body has no binding to read the matched value back through
        var diagnostics = Utility.GetAnalysisDiagnostics(
            """
            interface Foo { field: number }
            let x: Foo = new Foo { field: 1 };
            match x {
                Foo {} -> f,
                _ -> 0,
            }
            """
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find name 'f'.");
        Utility.AssertReportedOnce(diagnostics, "f");
    }

    [Fact]
    public void Allows_Match_ArrayAndRestBindings()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            let xs = [1, 2, 3];
            match xs {
                [a, b, c] -> a,
                [head, ..rest] -> head,
                _ -> 0,
            }
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Allows_Match_ObjectFieldBindings()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface Box { value: number }
            let box = new Box { value: 1 };
            match box {
                { value } -> value,
                { value: v } -> v,
                _ -> 0,
            }
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Allows_Match_OrAndRangePatterns()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            match 1 {
                2 | 3 | 4 -> true,
                0..5 -> false,
                _ -> false,
            }
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Allows_Match_Guard_IsBool()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            match 5 {
                n when n > 0 -> n,
                _ -> 0,
            }
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Allows_Match_Guard_OnArrayPattern_ReferencesElementBindings() =>
        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics("match [1, 2] { [a, b] when a > b -> a, _ -> 0 }"));

    [Fact]
    public void Allows_Match_Guard_OnObjectPattern_ReferencesFieldBindings()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface Foo { x: number }
            let value = new Foo { x: 5 };
            match value { { x } when x > 0 -> x, _ -> 0 }
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Allows_Match_NestedArrayInsideObjectInsideTypedPattern()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface Foo { items: number[] }
            let value = new Foo { items: [1, 2] };
            match value { f when Foo { items: [first, ..rest] } -> first, _ -> 0 }
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Checks_Match_ResultType_IsUnionOfArms()
    {
        var type = Utility.GetLastStatementType(
            """
            match 1 {
                0 -> "zero",
                _ -> 1,
            }
            """
        );

        var union = Assert.IsType<UnionType>(type);
        Assert.Contains(union.Types, t => t is LiteralType { Value: "zero" });
        Assert.Contains(union.Types, t => t is LiteralType { Value: 1d } or LiteralType { Value: 1L } or LiteralType { Value: 1 });
    }

    [Fact]
    public void Checks_Match_TypedPatternBinding_UsesNarrowedType()
    {
        var type = Utility.GetLastStatementType(
            """
            let value: string | number = "hi";
            match value {
                text when string -> text,
                n when number -> "num",
            }
            """
        );

        Assert.Equal(PrimitiveType.String, type.Widen());
    }

    [Fact]
    public void Checks_Match_ArrayRestBinding_IsArray()
    {
        var type = Utility.GetLastStatementType(
            """
            let xs = [1, 2, 3];
            match xs {
                [head, ..rest] -> rest,
                _ -> xs,
            }
            """
        );

        var array = Assert.IsType<ArrayType>(type);
        Assert.Equal(PrimitiveType.Number, array.ElementType.Widen());
    }

    [Fact]
    public void ThrowsFor_Match_LiteralPattern_TypeMismatch()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("""match "hi" { 1 -> true, _ -> false }""");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Pattern of type '1' cannot match value of type '\"hi\"'."
        );
    }

    [Fact]
    public void ThrowsFor_Match_ArrayPattern_OnNonArray()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("match 1 { [a] -> a, _ -> 0 }");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Array pattern cannot match value of type '1'."
        );
    }

    [Fact]
    public void Allows_Match_ArrayPattern_OnUnionOfArrays_NarrowsElementType()
    {
        const string source = """
            let value: number[] | string[] = [1];
            match value {
                [n] -> n,
                _ -> 0,
            }
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
    }

    [Fact]
    public void ThrowsFor_Match_ArrayPattern_OnUnionWithNonArrayMember()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            let value: number[] | string = [1];
            match value {
                [n] -> n,
                _ -> 0,
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Array pattern cannot match value of type 'number[] | string'."
        );
    }

    [Fact]
    public void Allows_Match_TuplePattern_OnUnionOfSameArityTuples_NarrowsElementTypesPositionally()
    {
        const string source = """
            let value: (string, number) | (bool, number) = ("abc", 1);
            match value {
                (a, b) -> b,
                _ -> 0,
            }
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
    }

    [Fact]
    public void ThrowsFor_Match_ObjectField_MissingProperty()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface Box { value: number }
            let box = new Box { value: 1 };
            match box { { missing } -> missing }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidAccess,
            "Property 'missing' does not exist on type 'Box'."
        );
    }

    [Fact]
    public void ThrowsFor_Match_Guard_NotBool()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            match 1 {
                n when 1 + 1 -> n,
                _ -> 0,
            }
            """
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type 'number' is not assignable to type 'bool'.");
    }

    [Fact]
    public void ThrowsFor_Match_RangePattern_OnNonNumber()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("""match "hi" { 0..5 -> true, _ -> false }""");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Range pattern can only match values of type 'number', not '\"hi\"'."
        );
    }

    [Fact]
    public void ThrowsFor_Match_NonExhaustive_NoIrrefutableArm()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("""match 1 { 0 -> "zero", 1 -> "one" }""");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.NonExhaustiveMatch,
            "Match expression is not exhaustive.",
            "add a wildcard arm ('_ -> ...') or a binding arm to cover the remaining cases."
        );
    }

    [Fact]
    public void ThrowsFor_Match_NonExhaustive_IrrefutablePatternGuarded()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("match 1 { x when x > 0 -> x }");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.NonExhaustiveMatch,
            "Match expression is not exhaustive.",
            "add a wildcard arm ('_ -> ...') or a binding arm to cover the remaining cases."
        );
    }

    [Fact]
    public void Allows_Match_Exhaustive_ViaOrPatternWildcard()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("""match 1 { 0 -> "zero", 1 | _ -> "other" }""");
        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void ThrowsFor_Match_TypedPattern_ImpossibleOverlap()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("match 123 { s when string -> 123, _ -> 0 }");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Pattern of type 'string' cannot match value of type '123'."
        );
    }

    [Fact]
    public void ThrowsFor_Match_TypePattern_ImpossibleOverlap()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface Foo { x: number }
            match 123 { Foo {} -> 0, _ -> 0 }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Pattern of type 'Foo' cannot match value of type '123'."
        );
    }

    [Fact]
    public void Allows_Match_TypedPattern_PossibleOverlap_OnWidenedType() =>
        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics("let value: number = 1; match value { n when number -> n, _ -> 0 }"));

    [Fact]
    public void Allows_Match_Exhaustive_UnionCoveredByTypedPatterns()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            let value: string | number = "hi";
            match value {
                text when string -> text,
                n when number -> "num",
            }
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void ThrowsFor_Match_NonExhaustive_UnionPartiallyCovered()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            let value: string | number | bool = "hi";
            match value {
                text when string -> text,
                n when number -> "num",
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.NonExhaustiveMatch,
            "Match expression is not exhaustive.",
            "add a wildcard arm ('_ -> ...') or a binding arm to cover the remaining cases."
        );
    }

    [Fact]
    public void Allows_Match_Exhaustive_UnionCoveredByInterfaceTypedPatterns()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface A { tag: string }
            interface B { count: number }
            let value: A | B = new A { tag: "a" };
            match value {
                a when A -> a.tag,
                b when B -> "count",
            }
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Allows_Match_Exhaustive_UnionCoveredByEmptyObjectTypePatterns()
    {
        // an empty object sub-pattern imposes no constraint beyond the type check itself, so it should
        // cover exactly as much as a bare type pattern with no object sub-pattern at all
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface A { tag: string }
            interface B { count: number }
            let value: A | B = new A { tag: "a" };
            match value {
                A {} -> "a",
                B {} -> "b",
            }
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Allows_Match_Exhaustive_UnionCoveredByEmptyObjectTypedPatterns()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface A { tag: string }
            interface B { count: number }
            let value: A | B = new A { tag: "a" };
            match value {
                a when A {} -> "a",
                b when B {} -> "b",
            }
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void ThrowsFor_Match_NonExhaustive_UnionPartiallyCoveredByNonEmptyObjectTypePattern()
    {
        // unlike an empty object sub-pattern, a field-constrained one only matches a subset of the type,
        // so it must not be credited as full coverage of that union member
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface A { tag: string }
            interface B { count: number }
            let value: A | B = new A { tag: "a" };
            match value {
                A { tag: "a" } -> "a",
                B {} -> "b",
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.NonExhaustiveMatch,
            "Match expression is not exhaustive.",
            "add a wildcard arm ('_ -> ...') or a binding arm to cover the remaining cases."
        );
    }

    [Fact]
    public void ThrowsFor_Match_NonExhaustive_UnionCoveredOnlyByLiterals()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            let value: string | number = "hi";
            match value {
                "hi" -> "greeting",
                1 -> "one",
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.NonExhaustiveMatch,
            "Match expression is not exhaustive.",
            "add a wildcard arm ('_ -> ...') or a binding arm to cover the remaining cases."
        );
    }

    [Fact]
    public void Allows_Match_QualifiedNamePattern_OnAnEnumMember()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            enum Direction { North, South, East, West }
            let d: Direction = Direction::North;
            match d {
                Direction::North -> "n",
                _ -> "other",
            }
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Checks_Match_QualifiedNamePattern_IsExhaustiveOverEveryMember()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            enum Direction { North, South, East, West }
            let d: Direction = Direction::North;
            match d {
                Direction::North -> 1,
                Direction::South -> 2,
                Direction::East -> 3,
                Direction::West -> 4,
            }
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void ThrowsFor_Match_NonExhaustive_QualifiedNamePatternMissingAMember()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            enum Direction { North, South, East, West }
            let d: Direction = Direction::North;
            match d {
                Direction::North -> 1,
                Direction::South -> 2,
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.NonExhaustiveMatch,
            "Match expression is not exhaustive.",
            "add a wildcard arm ('_ -> ...') or a binding arm to cover the remaining cases."
        );
    }

    [Fact]
    public void ThrowsFor_Match_QualifiedNamePattern_UnknownMember()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            enum Direction { North, South }
            let d: Direction = Direction::North;
            match d {
                Direction::NotAMember -> 1,
                _ -> 0,
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidAccess,
            "Expression of type '\"NotAMember\"' cannot be used to index type '{ North: 0, South: 1 }'. Property 'NotAMember' does not exist on type '{ North: 0, South: 1 }'."
        );
    }

    [Fact]
    public void ThrowsFor_Match_QualifiedNamePattern_UnknownEnum()
    {
        var diagnostics = Utility.GetAnalysisDiagnostics(
            """
            match 1 {
                NotAnEnum.Member -> 1,
                _ -> 0,
            }
            """
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find name 'NotAnEnum'.");
    }

    [Fact]
    public void ThrowsFor_Match_QualifiedNamePattern_NotACompileTimeConstant()
    {
        // Full analysis (not just type checking) so this also proves codegen doesn't crash reaching a
        // pattern the type checker already rejected and bound 'never' instead of a literal.
        var diagnostics = Utility.GetAnalysisDiagnostics(
            """
            interface Box { value: number }
            declare let box: Box;
            match 1 {
                box.value -> 1,
                _ -> 0,
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "'box.value' cannot be used as a pattern because its value is not a compile-time constant."
        );
    }

    [Fact]
    public void ThrowsFor_Match_QualifiedNamePattern_IncompatibleWithScrutinee()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            enum Direction { North, South }
            match "hello" {
                Direction::North -> "n",
                _ -> "other",
            }
            """
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Pattern of type '0' cannot match value of type '\"hello\"'.");
    }

    [Fact]
    public void Allows_Match_AndPattern_TypedPatternWithGuard()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            let value: number | string = 5;
            match value {
                n when number & n > 0 -> "positive",
                _ -> "other",
            }
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void ThrowsFor_Match_AndPattern_GuardNotBool()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            match 5 {
                n when number & 1 + 1 -> "a",
                _ -> "b",
            }
            """
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type 'number' is not assignable to type 'bool'.");
    }

    [Fact]
    public void ThrowsFor_Match_NonExhaustive_AndPatternDoesNotCountAsCoverage()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            let value: number | string = 5;
            match value {
                n when number & n > 0 -> "positive",
                s when string -> s,
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.NonExhaustiveMatch,
            "Match expression is not exhaustive.",
            "add a wildcard arm ('_ -> ...') or a binding arm to cover the remaining cases."
        );
    }

    [Fact]
    public void ThrowsFor_Match_NonExhaustive_ArrayPatternDoesNotCountAsCoverage()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            let value: number[] | string[] = [1];
            match value {
                [n] -> "n",
                [s] -> "s",
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.NonExhaustiveMatch,
            "Match expression is not exhaustive.",
            "add a wildcard arm ('_ -> ...') or a binding arm to cover the remaining cases."
        );
    }

    [Fact]
    public void ThrowsFor_Match_NonExhaustive_TuplePatternDoesNotCountAsCoverage()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            let value: (string, number) | (bool, number) = ("abc", 1);
            match value {
                (a, b) -> "n",
                (c, d) -> "s",
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.NonExhaustiveMatch,
            "Match expression is not exhaustive.",
            "add a wildcard arm ('_ -> ...') or a binding arm to cover the remaining cases."
        );
    }

    [Fact]
    public void ThrowsFor_Match_NonExhaustive_RangePatternDoesNotCountAsCoverage()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            let value: number | string = 5;
            match value {
                0..10 -> "n",
                s when string -> s,
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.NonExhaustiveMatch,
            "Match expression is not exhaustive.",
            "add a wildcard arm ('_ -> ...') or a binding arm to cover the remaining cases."
        );
    }
}
