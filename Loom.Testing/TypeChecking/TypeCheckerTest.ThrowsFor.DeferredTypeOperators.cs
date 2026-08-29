using Loom.Core.Diagnostics;
using Loom.Core.TypeChecking;
using Loom.Core.TypeChecking.Types;
using Type = Loom.Core.TypeChecking.Types.Type;
using Loom.Core.TypeChecking.Solving;
using Loom.Core.TypeChecking.Intrinsic;


namespace Loom.Testing;

public partial class TypeCheckerTest
{
    /// <remarks>
    ///     'keyof(T)' over a bare parameter is deferred the way 'T[K]' is, not answered at the declaration:
    ///     what T stands for is known only once the generic is instantiated. It used to be 'never', which
    ///     made every utility type written over its own keys silently empty.
    /// </remarks>
    [Theory]
    [InlineData("type Keys<T> = keyof(T);\ndeclare let probe: Keys<User>;", "\"name\" | \"age\"")]
    [InlineData("type ValueOf<T> = T[keyof(T)];\ndeclare let probe: ValueOf<User>;", "string | number")]
    [InlineData("type Lookup<T, K: keyof(T)> = T[K];\ndeclare let probe: Lookup<User, \"name\">;", "string")]
    [InlineData("type Lookup<T, K: keyof(T)> = T[K];\ndeclare let probe: Lookup<User, \"age\">;", "number")]
    public void Checks_DeferredKeyOf_ResolvesOnInstantiation(string source, string expected)
    {
        var type = Utility.GetLastStatementType($"interface User {{ name: string, age: number }}\n{source}\nprobe");
        var expanded = type is InstantiatedType instantiated ? instantiated.Expand() : type;

        Assert.Equal(expected, expanded.ToString());
    }

    [Fact]
    public void Checks_DeferredKeyOf_ConstrainsATypeArgument()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface User { name: string, age: number }
            type Lookup<T, K: keyof(T)> = T[K];
            declare let bad: Lookup<User, "nope">;
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.ConstraintViolation,
            "Type '\"nope\"' does not satisfy constraint '\"name\" | \"age\"' for type parameter 'K'."
        );
    }

    [Fact]
    public void ThrowsFor_KeyOf_OnANonObject_ThroughAnInstantiation()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("type Keys<T> = keyof(T);\ndeclare let probe: Keys<number>;");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidKeyOf, "Cannot access keys of type 'number'.");
    }

    /// <remarks>
    ///     A member belongs to an intersection if any constituent has it. Member access used to reject every
    ///     intersection outright, which made 'A & B' unusable for anything but assignability.
    /// </remarks>
    [Theory]
    [InlineData("direct.name", "string")]
    [InlineData("direct.tag", "string")]
    [InlineData("direct.age", "number")]
    [InlineData("direct[\"name\"]", "string")]
    public void Checks_MemberAccess_OnAnIntersection(string expression, string expected)
    {
        var type = Utility.GetLastStatementType(
            $"interface User {{ name: string, age: number }}\ninterface Tagged {{ tag: string }}\ndeclare let direct: User & Tagged;\n{expression}"
        );

        Assert.Equal(expected, type.ToString());
    }

    [Fact]
    public void Checks_MemberAccess_OnAnIntersectionThroughAGenericAlias() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                interface User { name: string, age: number }
                interface Tagged { tag: string }
                type Merge<A, B> = A & B;
                declare let merged: Merge<User, Tagged>;
                let a: string = merged.name;
                let b: string = merged.tag;
                """
            )
        );

    [Fact]
    public void Checks_MemberAccess_OnAnIntersection_IntersectsWhenSeveralConstituentsHaveIt()
    {
        var type = Utility.GetLastStatementType(
            """
            interface Named { value: string }
            interface Aged { value: string }
            declare let both: Named & Aged;
            both.value
            """
        );

        Assert.Equal("string", type.ToString());
    }

    [Fact]
    public void ThrowsFor_MemberAccess_OnAnIntersection_WhereNoConstituentHasIt()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            "interface User { name: string }\ninterface Tagged { tag: string }\ndeclare let direct: User & Tagged;\ndirect.missing;"
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidAccess, "Cannot access property 'missing' on type 'User & Tagged'.");
    }
}
