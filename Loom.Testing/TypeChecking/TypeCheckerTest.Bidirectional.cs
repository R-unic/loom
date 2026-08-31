using Loom.Core.Diagnostics;
using Loom.Core.TypeChecking.Types;
using Loom.Core.TypeChecking.Solving;

namespace Loom.Testing.TypeChecking;

public partial class TypeCheckerTest
{
    [Fact]
    public void Allows_EmptyArrayLiteral_AsAnnotatedFunctionArgument()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            declare fn take(xs: number[]): void;
            take([]);
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Allows_EmptyArrayLiteral_AsAnnotatedFunctionReturn()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            fn empty(): number[] {
              return [];
            }
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Allows_GenericCall_ReturnContext_DrivesNestedLiteral()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            declare fn identity<T>(value: T): T;
            let xs: number[] = identity([1, 2]);
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void ThrowsFor_GenericCall_ReturnContext_ArrayElementMismatch()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            declare fn id<T>(value: T): T;
            let xs: number[] = id([1, "no"]);
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type '(number | string)[]' is not assignable to type 'number[]'.\n    Type '\"no\"' is not assignable to type 'number'."
        );
    }

    [Fact]
    public void ThrowsFor_GenericCall_ExplicitTypeArgs_ArrayElementMismatch()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            declare fn id<T>(value: T): T;
            id::<number[]>([1, "no"]);
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type '(number | string)[]' is not assignable to type 'number[]'.\n    Type '\"no\"' is not assignable to type 'number'."
        );
    }

    [Fact]
    public void ThrowsFor_AnnotatedObjectLiteral_PropertyMismatch_ReportsOnBadField()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface Point { x: number, y: number }
            let p: Point = new Point { x: 1, y: "no" }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type '\"no\"' is not assignable to type 'number'."
        );
    }

    [Fact]
    public void ThrowsFor_InterfaceInvocation_NestedArrayProperty_ElementMismatch()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface Box { items: number[] }
            new Box { items: [1, "no"] }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type '(number | string)[]' is not assignable to type 'number[]'.\n    Type '\"no\"' is not assignable to type 'number'."
        );
    }

    [Fact]
    public void ThrowsFor_InterfaceInvocation_NestedArrayIndexer_ElementMismatch()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface Box { [string]: number[] }
            new Box { ["k"]: [1, "no"] }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type '(number | string)[]' is not assignable to type 'number[]'.\n    Type '\"no\"' is not assignable to type 'number'."
        );
    }

    [Fact]
    public void ThrowsFor_AnnotatedArrayLiteral_ElementMismatch_ReportsOnBadElement()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("""let xs: number[] = [1, "no"]""");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type '(number | string)[]' is not assignable to type 'number[]'.\n    Type '\"no\"' is not assignable to type 'number'."
        );
    }

    [Fact]
    public void ThrowsFor_ArrayLiteral_ElementMismatch_AsFunctionArgument()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            declare fn take(xs: number[]): void;
            take([1, "no"]);
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type '(number | string)[]' is not assignable to type 'number[]'.\n    Type '\"no\"' is not assignable to type 'number'."
        );
    }

    [Fact]
    public void ThrowsFor_ArrayLiteral_ElementMismatch_AsFunctionReturn()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            fn bad(): number[] {
              return [1, "no"];
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type '(number | string)[]' is not assignable to type 'number[]'.\n    Type '\"no\"' is not assignable to type 'number'."
        );
    }

    [Fact]
    public void ThrowsFor_ArrayLiteral_ElementMismatch_AsExpressionBodyReturn()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("""fn bad(): number[] -> [1, "no"]""");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type '(number | string)[]' is not assignable to type 'number[]'.\n    Type '\"no\"' is not assignable to type 'number'."
        );
    }

    [Fact]
    public void ThrowsFor_ArrayLiteral_ElementMismatch_OnAssignment()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            mut xs: number[] = [1];
            xs = [1, "no"];
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type '(number | string)[]' is not assignable to type 'number[]'.\n    Type '\"no\"' is not assignable to type 'number'."
        );
    }

    [Fact]
    public void ThrowsFor_NestedArrayLiteral_ElementMismatch_ReportsOnBadElement()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("""let xs: number[][] = [[1, "no"]]""");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type '(number | string)[]' is not assignable to type 'number[]'.\n    Type '\"no\"' is not assignable to type 'number'."
        );
    }

    [Fact]
    public void Allows_Match_EmptyArrayArm_AsAnnotatedVariable()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            let xs: number[] = match 1 {
                _ -> [],
            };
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Allows_Match_EmptyArrayArm_AsFunctionReturn()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            fn empty(flag: bool): number[] {
                return match flag {
                    true -> [1],
                    false -> [],
                };
            }
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void ThrowsFor_Match_ArmBody_Mismatch_AgainstAnnotatedType()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            let x: number = match 1 {
                _ -> "no",
            };
            """
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '\"no\"' is not assignable to type 'number'.");
    }

    [Fact]
    public void ThrowsFor_Match_NestedArrayLiteral_ElementMismatch()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            let xs: number[] = match true {
                true -> [1, "no"],
                false -> [],
            };
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type '(number | string)[]' is not assignable to type 'number[]'.\n    Type '\"no\"' is not assignable to type 'number'."
        );
    }

    [Fact]
    public void Allows_TernaryOperator_EmptyArrayLiteralBranch_AgainstAnnotatedType()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let xs: number[] = true ? [1, 2] : [];");

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Allows_TernaryOperator_BothBranchesEmptyArrayLiteral_AsFunctionArgument()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            declare fn take(xs: number[]): void;
            take(true ? [] : []);
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void ThrowsFor_TernaryOperator_ThenBranch_ArrayElementMismatch()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            let xs: number[] = true ? [1, "no"] : [];
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type '(number | string)[]' is not assignable to type 'number[]'.\n    Type '\"no\"' is not assignable to type 'number'."
        );
    }

    [Fact]
    public void ThrowsFor_TernaryOperator_ElseBranch_ArrayElementMismatch()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            let xs: number[] = true ? [] : [1, "no"];
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type '(number | string)[]' is not assignable to type 'number[]'.\n    Type '\"no\"' is not assignable to type 'number'."
        );
    }

    [Fact]
    public void Allows_ParenthesizedEmptyArrayLiteral_AgainstAnnotatedType()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let xs: number[] = ([]);");

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void ThrowsFor_ParenthesizedTernaryOperator_BranchArrayElementMismatch()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            let xs: number[] = (true ? [1, "no"] : []);
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type '(number | string)[]' is not assignable to type 'number[]'.\n    Type '\"no\"' is not assignable to type 'number'."
        );
    }

    [Fact]
    public void ThrowsFor_AnnotatedArrayLiteral_AllElementsMismatch_TracesOuterArrayType()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let a: string[] = [1, 2, 3]");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type 'number[]' is not assignable to type 'string[]'.\n    Type '1' is not assignable to type 'string'."
        );
    }

    [Fact]
    public void ThrowsFor_UnnestedTypeMismatch_HasNoTrace()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("""let x: number = "no";""");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '\"no\"' is not assignable to type 'number'.");
    }
    [Fact]
    public void Allows_NullCoalesce_EmptyArrayFallback_AgainstAnnotatedType()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            let a: number[]? = [1, 2, 3];
            let xs: number[] = a ?? [];
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void ThrowsFor_NullCoalesce_FallbackMismatch_AgainstAnnotatedType()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            let a: number? = 1;
            let x: number = a ?? "no";
            """
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '\"no\"' is not assignable to type 'number'.");
    }

    [Fact]
    public void ThrowsFor_NullCoalesce_FallbackArrayElementMismatch_AgainstAnnotatedType()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            let a: number[]? = [1, 2, 3];
            let xs: number[] = a ?? [1, "no"];
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type '(number | string)[]' is not assignable to type 'number[]'.\n    Type '\"no\"' is not assignable to type 'number'."
        );
    }

    [Fact]
    public void Allows_InterfaceInvocation_EmptyArrayProperty_AgainstAnnotatedGenericType()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface Box<T> { value: T[] }
            let b: Box<number> = new Box { value: [] };
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void ThrowsFor_InterfaceInvocation_PropertyMismatch_AgainstAnnotatedGenericType()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface Box<T> { value: T }
            let b: Box<number> = new Box { value: "no" };
            """
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '\"no\"' is not assignable to type 'number'.");
    }

    [Fact]
    public void Infers_InterfaceInvocation_GenericTypeParameter_FromAnnotatedContext()
    {
        var type = Utility.GetLastStatementType(
            """
            interface Box<T> { value: T[] }
            let b: Box<number> = new Box { value: [] };
            b
            """
        );

        Assert.Equal("Box<number>", type.ToString());
        var interfaceType = Assert.IsType<InterfaceType>(TypeSimplifier.Expanded(type));
        var property = interfaceType.GetProperty("value");
        Assert.NotNull(property);
        var array = Assert.IsType<ArrayType>(property.ValueType);
        Assert.Equal(PrimitiveType.Number, array.ElementType);
    }

    /// <summary>A named argument's value is checked contextually the same as a positional one - the array literal takes its element type from the declared parameter type, not from the argument's own inference.</summary>
    [Fact]
    public void Allows_NamedArgument_ArrayLiteral_ChecksContextually() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                fn takes(xs: number[]): void {}
                takes(xs: [1, 2, 3]);
                """
            )
        );

    /// <summary>A function-expression parameter that carries both an explicit colon type and a default value is checked against the expected function type from both sides, not just one.</summary>
    [Fact]
    public void Allows_FunctionExpression_ParameterWithDeclaredTypeAndDefault_AgainstExpectedFunctionType() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics("let cb: fn(x: number): void = fn(x: number = 5) { };")
        );

    /// <summary>An empty match expression checked contextually (rather than merely visited) binds to 'never' - which is assignable to the expected type - instead of crashing on an empty arm list.</summary>
    [Fact]
    public void Allows_EmptyMatchExpression_ChecksAgainstContextualType() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                let n = 1;
                let y: number = match n { };
                """
            )
        );

    /// <summary>The redundant null-coalescing warning fires from the contextual Check path too ('let x: number = 5 ?? 10'), not only from a bare Visit of the same operator.</summary>
    [Fact]
    public void WarnsFor_NullCoalesce_ChecksAgainstContextualType_WhenLeftNotOptional()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let x: number = 5 ?? 10;");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.RedundantCode, "Null coalescing has no effect since '5' is not optional.");
    }
}
