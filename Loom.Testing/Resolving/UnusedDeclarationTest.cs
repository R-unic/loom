using Loom.Core.Diagnostics;
using Loom.Testing;

namespace Loom.Testing.Resolving;

[Collection("Assembly")]
public class UnusedDeclarationTest
{
    [Fact]
    public void WarnsFor_UnusedParameter_AndUnusedLocal_ButNotAUsedOne()
    {
        var diagnostics = Utility.GetSemanticModel("fn compute(x: number, y: number): number { let total = x * 2; return x; }").Diagnostics;

        Utility.AssertDiagnostic(diagnostics, InternalCodes.UnusedParameter, "'y' is never used.");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.UnusedVariable, "'total' is never used.");
        Assert.DoesNotContain(
            diagnostics.Set,
            diagnostic => diagnostic.Code is InternalCodes.UnusedParameter or InternalCodes.UnusedVariable && diagnostic.Message.Contains("'x'")
        );
    }

    [Fact]
    public void DoesNotWarn_ForUnderscorePrefixedParameterOrLocal()
    {
        var diagnostics = Utility.GetSemanticModel("fn compute(_x: number): number { let _unused = 1; return 0; }").Diagnostics;

        Assert.DoesNotContain(diagnostics.Set, diagnostic => diagnostic.Code is InternalCodes.UnusedParameter or InternalCodes.UnusedVariable);
    }

    [Fact]
    public void DoesNotWarn_ForModuleScopeVariable()
    {
        var diagnostics = Utility.GetSemanticModel("let x = 1;").Diagnostics;

        Assert.DoesNotContain(diagnostics.Set, diagnostic => diagnostic.Code == InternalCodes.UnusedVariable);
    }

    [Fact]
    public void DoesNotWarn_ForSignatureOnlyParameter()
    {
        var diagnostics = Utility.GetSemanticModel("declare fn f(value: number): void;").Diagnostics;

        Assert.DoesNotContain(diagnostics.Set, diagnostic => diagnostic.Code == InternalCodes.UnusedParameter);
    }

    [Fact]
    public void WarnsFor_UnusedTypeParameter_ButNotAUsedOne()
    {
        var diagnostics = Utility.GetSemanticModel("fn identity<T, U>(x: T): T { return x; }").Diagnostics;

        Utility.AssertDiagnostic(diagnostics, InternalCodes.UnusedTypeParameter, "'U' is never used.");
        Assert.DoesNotContain(diagnostics.Set, diagnostic => diagnostic.Code == InternalCodes.UnusedTypeParameter && diagnostic.Message.Contains("'T'"));
    }

    [Fact]
    public void DoesNotWarn_ForUnderscorePrefixedTypeParameter()
    {
        var diagnostics = Utility.GetSemanticModel("fn identity<_T>(x: number): number { return x; }").Diagnostics;

        Assert.DoesNotContain(diagnostics.Set, diagnostic => diagnostic.Code == InternalCodes.UnusedTypeParameter);
    }

    [Fact]
    public void WarnsFor_TraitNeverImplemented()
    {
        var diagnostics = Utility.GetSemanticModel("trait Comparable { fn compare(other: Self): number; }").Diagnostics;

        Utility.AssertDiagnostic(diagnostics, InternalCodes.UnusedTrait, "'Comparable' is never implemented.");
        Assert.DoesNotContain(diagnostics.Set, diagnostic => diagnostic.Code == InternalCodes.UnusedParameter);
    }

    [Fact]
    public void DoesNotWarn_ForTraitImplementedInThisFile()
    {
        var diagnostics = Utility.GetSemanticModel(
            """
            trait Foo { fn a(): void }
            interface Bar { }

            implement Foo for Bar {
                fn a() { }
            }
            """
        ).Diagnostics;

        Assert.DoesNotContain(diagnostics.Set, diagnostic => diagnostic.Code == InternalCodes.UnusedTrait);
    }

    /// <summary>
    ///     An exported trait could still be implemented by another file resolved later in the same build, so
    ///     only a private trait is provably dead from this file alone.
    /// </summary>
    [Fact]
    public void DoesNotWarn_ForExportedTraitNeverImplementedInThisFile()
    {
        var diagnostics = Utility.GetSemanticModel("export trait Comparable { fn compare(other: Self): number; }").Diagnostics;

        Assert.DoesNotContain(diagnostics.Set, diagnostic => diagnostic.Code == InternalCodes.UnusedTrait);
    }
}
