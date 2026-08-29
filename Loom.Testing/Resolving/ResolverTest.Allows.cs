using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving.Symbols;

namespace Loom.Testing;

public partial class ResolverTest
{
    [Fact]
    public void Allows_ValidShorthandInitializers() =>
        Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                interface Foo { bar: string, baz: number }

                let bar = "abc";
                let baz = 420;
                let foo = new Foo { bar, baz }
                """
            )
        );

    [Fact]
    public void Allows_ValidTraitImplementation() =>
        Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                trait Iterator {
                    fn next(): number
                }

                interface List { }

                implement Iterator for List {
                    fn next() { return 0; }
                }
                """
            )
        );

    [Fact]
    public void Allows_ForLoopVariable_ShadowingOuterVariable()
    {
        var model = Utility.AssertNoErrors(Utility.GetSemanticModel("let x = 1; for x : 1..10 { x; }"));
        var statements = model.Tree.Statements;
        var forStmt = Assert.IsType<For>(statements[1]);
        var declSymbol = model.GetDeclarationSymbol(forStmt.Names.First());
        Assert.NotNull(declSymbol);

        var outerDecl = Assert.IsType<VariableDeclaration>(statements[0]);
        var outerSymbol = model.GetDeclarationSymbol(outerDecl);
        Assert.NotEqual(declSymbol, outerSymbol);
    }

    [Fact]
    public void ThrowsFor_ForLoop_DuplicateNames_DoesNotLeakScope()
    {
        var diagnostics = Utility.GetSemanticModel(
            """
            for x, x in [1, 2] { }
            export let y = 1;
            """
        ).Diagnostics;

        var diagnostic = Assert.Single(diagnostics.Set);
        Assert.Equal(InternalCodes.DuplicateName, diagnostic.Code);
    }

    [Fact]
    public void ThrowsFor_Implement_OtherMethodCollision_DoesNotLeakScope()
    {
        var diagnostics = Utility.GetSemanticModel(
            """
            trait A { fn shared(): void }
            trait B { fn shared(): void }
            trait C { fn go(): void }
            interface Foo { }
            implement A for Foo { fn shared() { } }
            implement B for Foo { fn shared() { } }
            implement C for Foo { fn go() { } }
            export let y = 1;
            """
        ).Diagnostics;

        var diagnostic = Assert.Single(diagnostics.Set);
        Assert.Equal(InternalCodes.DuplicateName, diagnostic.Code);
    }

    [Fact]
    public void Allows_InvokingIntrinsicInterfaces() => Utility.AssertNoErrors(Utility.GetSemanticModel("new Record::<string, bool> {}"));

    [Fact]
    public void Allows_TernaryOp() => Utility.AssertNoErrors(Utility.GetSemanticModel("true ? 1 : 'abc'"));

    [Theory]
    [InlineData("fn abc { return 69 }")]
    [InlineData("fn abc { if true { return 69 } }")]
    public void Allows_Fn_Return(string source) => Utility.AssertNoErrors(Utility.GetSemanticModel(source));

    [Fact]
    public void Allows_VariableInitializedBeforeAfter_UsedAfter() => Utility.AssertNoErrors(Utility.GetSemanticModel("let x = 1; after 1s { } x;"));

    [Fact]
    public void Allows_AfterInsideIf() => Utility.AssertNoErrors(Utility.GetSemanticModel("if true { after 1s { } }"));

    [Fact]
    public void Allows_AfterInsideWhile() => Utility.AssertNoErrors(Utility.GetSemanticModel("while true { after 1s { } }"));

    [Fact]
    public void Allows_AfterBody_WithShadowedVariable() => Utility.AssertNoErrors(Utility.GetSemanticModel("let x = 1; after 1s { let x = 2; x; } x;"));

    [Fact]
    public void Allows_After_WithEmptyBlock() => Utility.AssertNoErrors(Utility.GetSemanticModel("after 1s { }"));

    [Fact]
    public void Allows_VariableInitializedBeforeEvery_UsedAfter() => Utility.AssertNoErrors(Utility.GetSemanticModel("let x = 1; every 1s { } x;"));

    [Fact]
    public void Allows_EveryInsideIf() => Utility.AssertNoErrors(Utility.GetSemanticModel("if true { every 1s { } }"));

    [Fact]
    public void Allows_EveryInsideWhile() => Utility.AssertNoErrors(Utility.GetSemanticModel("while true { every 1s { } }"));

    [Fact]
    public void Allows_EveryBody_WithShadowedVariable() => Utility.AssertNoErrors(Utility.GetSemanticModel("let x = 1; every 1s { let x = 2; x; } x;"));

    [Fact]
    public void Allows_Every_WithEmptyBlock() => Utility.AssertNoErrors(Utility.GetSemanticModel("every 1s { }"));

    [Fact]
    public void Allows_EveryGuardCondition_ReferencingOuterVariable() =>
        Utility.AssertNoErrors(Utility.GetSemanticModel("let running = true; every 1s while running { }"));

    [Fact]
    public void Allows_NonSealedInterfaceConstraints() => Utility.AssertNoErrors(Utility.GetSemanticModel("interface A; interface B: A;"));

    [Fact]
    public void Allows_BreakInsideFor() => Utility.AssertNoErrors(Utility.GetSemanticModel("for x : 1..10 { break }"));

    [Fact]
    public void Allows_ContinueInsideFor() => Utility.AssertNoErrors(Utility.GetSemanticModel("for x : 1..10 { continue }"));

    [Fact]
    public void Allows_BreakInsideWhile() => Utility.AssertNoErrors(Utility.GetSemanticModel("while true { break }"));

    [Fact]
    public void Allows_ContinueInsideWhile() => Utility.AssertNoErrors(Utility.GetSemanticModel("while true { continue }"));

    [Fact]
    public void Allows_BreakInsideIfInsideWhile() => Utility.AssertNoErrors(Utility.GetSemanticModel("while true { if true { break } }"));

    [Fact]
    public void Allows_ContinueInsideIfInsideWhile() => Utility.AssertNoErrors(Utility.GetSemanticModel("while true { if true { continue } }"));

    [Fact]
    public void VariableInitializedBeforeWhile_IsDefinitelyInitializedAfter()
    {
        var model = Utility.GetSemanticModel("let x = 1; while true { break } x;");
        Utility.AssertNoErrors(model);
    }

    [Fact]
    public void Allows_Interface_WithSingleIndexerAndUniqueProperties() =>
        Utility.AssertNoErrors(Utility.GetSemanticModel("interface I { [number]: string, count: number, name: string }"));

    [Fact]
    public void Allows_SameParameterName_ChainedFnTypes()
    {
        var model = Utility.GetSemanticModel("type X = fn(x: number): void & fn(x: string): bool");
        Utility.AssertNoErrors(model);
    }

    [Fact]
    public void Allows_UsageOfDeclaredVariable()
    {
        var model = Utility.GetSemanticModel("declare let x: number; x;");
        Utility.AssertNoErrors(model);
    }

    [Fact]
    public void Allows_UsageOfDeclaredFunction()
    {
        var model = Utility.GetSemanticModel("declare fn foo(): void; foo;");
        Utility.AssertNoErrors(model);
    }

    [Fact]
    public void Allows_AssignToDeclaredMutableVariable()
    {
        var model = Utility.GetSemanticModel("declare mut counter: number; counter = 1;");
        Utility.AssertNoErrors(model);
    }

    [Fact]
    public void Allows_NestedScopes_WithSameVariableNames() => Utility.AssertNoErrors(Utility.GetSemanticModel("let x = 42; { let x = 69; x; } x;"));

    [Fact]
    public void Allows_ReturnStatementInNestedFunction() => Utility.AssertNoErrors(Utility.GetSemanticModel("fn outer() { fn inner() { return 42; } return 0; }"));

    [Fact]
    public void Allows_ReturnStatementInsideFunction() => Utility.AssertNoErrors(Utility.GetSemanticModel("fn test() { return 42; }"));

    [Fact]
    public void Allows_ReturnStatementInFunctionExpression() =>
        Utility.AssertNoErrors(Utility.GetSemanticModel("let f = fn(): number { return 42; };"));

    [Fact]
    public void Allows_FunctionExpression_CapturesOuterVariable() =>
        Utility.AssertNoErrors(Utility.GetSemanticModel("let x = 42; let f = fn(): number { return x; };"));

    [Fact]
    public void ThrowsFor_FunctionExpression_ParameterLeaksOutsideBody()
    {
        var diagnostics = Utility.GetSemanticModel("let f = fn(x: number): number { return x; }; x;").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find name 'x'.");
    }

    /// <remarks>
    ///     A function type names its parameters for documentation, not for binding - the body of the
    ///     function taking the callback never receives a value under that name, only the callback itself.
    ///     Reported once, from the resolver: the stages after it look the name up and find nothing too.
    /// </remarks>
    [Fact]
    public void ThrowsFor_FunctionTypeParameter_UsedAsNameInBody()
    {
        var diagnostics = Utility.GetAnalysisDiagnostics("fn on(handler: fn(data: number): void): void { print(data); }");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find name 'data'.");
        Utility.AssertReportedOnce(diagnostics, "data");
    }

    [Fact]
    public void Allows_FunctionExpression_ParameterShadowsOuterVariable() =>
        Utility.AssertNoErrors(Utility.GetSemanticModel("let x = 42; let f = fn(x: number): number { return x; };"));

    [Fact]
    public void Allows_MultipleInitializations_AcrossBranches() =>
        Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                            let cond1 = true; 
                            let cond2 = true; 
                            mut x: number;
                            if cond1 {
                                x = 1;
                            } else if cond2 {
                                x = 2;
                            } else {
                                x = 3;
                            }
                            x;
                """
            )
        );

    [Fact]
    public void Allows_VariableInitializedInOuterScope_ToBeUsedInInnerScope() => Utility.AssertNoErrors(Utility.GetSemanticModel("let x = true; if x { x }"));

    [Fact]
    public void Allows_VariableInitializedBeforeIf_ToBeUsedAfterIf() =>
        Utility.AssertNoErrors(Utility.GetSemanticModel("let condition = true; let x = 42; if condition { } x;"));

    [Fact]
    public void Allows_VariableInitializedInBothIfBranches_ToBeUsedAfterIf() =>
        Utility.AssertNoErrors(Utility.GetSemanticModel("let condition = true; mut x: number; if condition { x = 42 } else { x = 0 } x;"));

    [Fact]
    public void Allows_VariableInitializedInThenBranch_UsedInsideThenBranch() =>
        Utility.AssertNoErrors(Utility.GetSemanticModel("let condition = true; mut x: number; if condition { x = 42; x; }"));

    [Fact]
    public void Allows_VariableFromOuterScope_ToBeReassignedInInnerScope() =>
        Utility.AssertNoErrors(Utility.GetSemanticModel("let condition = true; mut x = 1; if condition { x = 2; } x;"));

    [Fact]
    public void Allows_Match_WildcardAndLiteralArms() => Utility.AssertNoErrors(Utility.GetSemanticModel("""match 1 { 0 -> "zero", _ -> "other" }"""));

    [Fact]
    public void Allows_Match_ArrayAndRestBindings() =>
        Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                let xs = 1;
                match xs {
                    [a, b, c] -> a,
                    [head, ..rest] -> head,
                }
                """
            )
        );

    [Fact]
    public void Allows_Match_NestedArrayInsideObjectInsideTypedPattern() =>
        Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                interface Foo { items: number[] }
                match 1 { f when Foo { items: [first, ..rest] } -> first, _ -> 0 }
                """
            )
        );

    [Fact]
    public void Allows_Match_ObjectShorthandBinding() => Utility.AssertNoErrors(Utility.GetSemanticModel("match 1 { { value } -> value }"));

    [Fact]
    public void Allows_Match_ObjectFieldBinding() => Utility.AssertNoErrors(Utility.GetSemanticModel("match 1 { { ok: true, value: v } -> v }"));

    [Fact]
    public void Allows_Match_OrAndRangePatterns() =>
        Utility.AssertNoErrors(Utility.GetSemanticModel("match 1 { 2 | 3 | 4 -> true, 0..5 | 10..15 | 100 -> false, _ -> false }"));

    [Fact]
    public void Allows_Match_OrPattern_AlternativesSharingBindingName() =>
        Utility.AssertNoErrors(Utility.GetSemanticModel("match 1 { let x | let x -> x, _ -> 0 }"));

    [Fact]
    public void Allows_Match_OrPattern_ObjectAlternativesSharingFieldBindingName() =>
        Utility.AssertNoErrors(
            Utility.GetSemanticModel("interface Foo { x: number }; interface Bar { x: number }; match 1 { Foo { x } | Bar { x } -> x, _ -> 0 }")
        );

    [Fact]
    public void Allows_Match_LetPatternBinding() => Utility.AssertNoErrors(Utility.GetSemanticModel("match 1 { let name -> name }"));

    [Fact]
    public void Allows_Match_TypedPattern_WithPrimitive() => Utility.AssertNoErrors(Utility.GetSemanticModel("match 1 { s when string -> s, _ -> \"\" }"));

    [Fact]
    public void Allows_Match_PatternBinding_ShadowsOuterVariable() => Utility.AssertNoErrors(Utility.GetSemanticModel("let x = 1; match 2 { x -> x }"));

    [Fact]
    public void Allows_Match_AsVariableInitializer() =>
        Utility.AssertNoErrors(Utility.GetSemanticModel("""let n = 1; let x = match n { 0 -> "zero", _ -> "other" };"""));
}
