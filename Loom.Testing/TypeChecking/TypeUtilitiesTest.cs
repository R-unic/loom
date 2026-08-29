using Loom.Core.Diagnostics;

namespace Loom.Testing;

/// <summary>
///     The shipped structural type utilities in loom.loom - <c>Mut</c>, <c>Readonly</c>, <c>Pick</c>,
///     <c>Omit</c>, <c>Partial</c>, <c>Required</c>, <c>ReturnType</c>, <c>ElementType</c>,
///     <c>NonNullable</c>, <c>Exclude</c> and <c>Extract</c>. Every case here uses the ambient name with
///     no local declaration, since the thing under test is that these are actually shipped, not that the
///     underlying language feature works - <see cref="ConditionalTypeTest" /> and
///     <see cref="MappedTypeTest" /> already cover that with type aliases of their own.
/// </summary>
[Collection("Assembly")]
public class TypeUtilitiesTest
{
    private const string Point = "interface Point { x: number; y: number; z: number?; }\n";

    [Fact]
    public void Mut_MakesEveryMemberWritable() =>
        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics($"{Point}declare let point: Mut<Point>;\npoint.x = 1;"));

    [Fact]
    public void Readonly_MakesEveryMemberUnwritable()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics($"{Point}declare let point: Readonly<Point>;\npoint.x = 1;");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.AssignToImmutable, "Cannot assign to immutable property 'x'.");
    }

    [Fact]
    public void Pick_KeepsOnlyTheNamedKeys()
    {
        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics($"{Point}declare let picked: Pick<Point, \"x\" | \"y\">;\nprint(picked.x, picked.y);"));

        var diagnostics = Utility.GetTypeCheckerDiagnostics($"{Point}declare let picked: Pick<Point, \"x\" | \"y\">;\nprint(picked.z);");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidAccess,
            "Expression of type '\"z\"' cannot be used to index type '{ x: number, y: number }'. Property 'z' does not exist on type '{ x: number, y: number }'."
        );
    }

    /// <remarks>'K' is constrained to 'keyof(T)' rather than matched against it, so a key Point does not have is a compile error at the use site rather than a silently empty pick.</remarks>
    [Fact]
    public void Pick_RefusesAKeyTheSourceDoesNotHave() =>
        Utility.AssertDiagnostic(
            Utility.GetTypeCheckerDiagnostics($"{Point}declare let picked: Pick<Point, \"nope\">;"),
            InternalCodes.ConstraintViolation,
            "Type '\"nope\"' does not satisfy constraint '\"x\" | \"y\" | \"z\"' for type parameter 'K'."
        );

    [Fact]
    public void Omit_DropsOnlyTheNamedKeys()
    {
        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics($"{Point}declare let omitted: Omit<Point, \"z\">;\nprint(omitted.x, omitted.y);"));

        var diagnostics = Utility.GetTypeCheckerDiagnostics($"{Point}declare let omitted: Omit<Point, \"z\">;\nprint(omitted.z);");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidAccess,
            "Expression of type '\"z\"' cannot be used to index type '{ x: number, y: number }'. Property 'z' does not exist on type '{ x: number, y: number }'."
        );
    }

    /// <remarks>A mapped type has an index signature rather than fixed properties, so it is read through an indexed-type expression rather than constructed - there is no 'new Partial&lt;T&gt; { … }' to build one from.</remarks>
    [Fact]
    public void Partial_AcceptsNoneForEveryMember() =>
        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics($"{Point}let x: Partial<Point>[\"x\"] = none;"));

    [Fact]
    public void Required_RefusesNoneForAMemberThatWasOptional()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics($"{Point}let z: Required<Point>[\"z\"] = none;");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type 'none' is not assignable to type 'NonNullable<number?>'.\n    Type 'none' is not assignable to type 'number'."
        );
    }

    [Fact]
    public void ReturnType_ReadsTheReturnTypeOfAFunctionType() =>
        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics("let ret: ReturnType<fn(): number> = 3;"));

    [Fact]
    public void ReturnType_OfANonFunctionIsNever() =>
        Utility.AssertDiagnostic(
            Utility.GetTypeCheckerDiagnostics("let ret: ReturnType<number> = 3;"),
            InternalCodes.TypeMismatch,
            "Type '3' is not assignable to type 'ReturnType<number>'.\n    Type '3' is not assignable to type 'never'."
        );

    [Fact]
    public void ElementType_ReadsTheElementTypeOfAnArray() =>
        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics("let elem: ElementType<string[]> = \"a\";"));

    [Fact]
    public void NonNullable_DropsNoneFromAnOptionalType()
    {
        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics("let n: NonNullable<number?> = 3;"));

        var diagnostics = Utility.GetTypeCheckerDiagnostics("let n: NonNullable<number?> = none;");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type 'none' is not assignable to type 'NonNullable<number?>'.\n    Type 'none' is not assignable to type 'number'."
        );
    }

    [Fact]
    public void Exclude_RemovesTheNamedMemberFromAUnion()
    {
        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics("let kept: Exclude<\"x\" | \"y\" | \"z\", \"y\"> = \"x\";"));

        var diagnostics = Utility.GetTypeCheckerDiagnostics("let kept: Exclude<\"x\" | \"y\" | \"z\", \"y\"> = \"y\";");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type '\"y\"' is not assignable to type 'Exclude<\"x\" | \"y\" | \"z\", \"y\">'.\n    Type '\"y\"' is not assignable to type '\"x\" | \"z\"'."
        );
    }

    [Fact]
    public void Extract_KeepsOnlyMembersAssignableToTheSecondArgument()
    {
        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics("let kept: Extract<\"x\" | \"y\" | \"z\", \"y\" | \"z\"> = \"y\";"));

        var diagnostics = Utility.GetTypeCheckerDiagnostics("let kept: Extract<\"x\" | \"y\" | \"z\", \"y\" | \"z\"> = \"x\";");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type '\"x\"' is not assignable to type 'Extract<\"x\" | \"y\" | \"z\", \"y\" | \"z\">'.\n    Type '\"x\"' is not assignable to type '\"y\" | \"z\"'."
        );
    }

    /// <remarks>
    ///     A type declared in runtime.loom marks every file using it as needing the runtime import,
    ///     whether the type itself ever touches the runtime or not - which is why these live in
    ///     loom.loom instead. A regression here would silently add a require() to every file that uses
    ///     any of these, whether or not anything else in it needed the runtime.
    /// </remarks>
    [Theory]
    [InlineData("let ret: ReturnType<fn(): number> = 3;")]
    [InlineData("interface Point { x: number; }\ndeclare let picked: Pick<Point, \"x\">;")]
    [InlineData("interface Point { x: number; }\ndeclare let required: Required<Point>;")]
    public void TypeUtilities_NeedNoRuntimeImport(string source) => Assert.Single(Utility.GetLuauAST(source, typeCheck: true, disableRuntimeLib: false).Statements);
}
