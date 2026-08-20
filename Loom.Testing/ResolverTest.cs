using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving.Symbols;

namespace Loom.Testing;

[Collection("Assembly")]
public class ResolverTest
{
    [Fact]
    public void WarnsFor_UseExpressionBody()
    {
        var diagnostics = Utility.GetSemanticModel("fn abc() { return 1; }").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.RedundantCode, "Use expression body.");
    }

    [Fact]
    public void TracksInitialization_ThroughNestedBlocks() => Utility.AssertNoErrors(Utility.GetSemanticModel("mut x: number; { x = 42; } x;"));

    /// <summary>
    ///     A hoisted declaration is visited after the hoisting pass already declared it, and declaring it
    ///     again would leave two symbols standing for one node - only the first reachable through a lookup,
    ///     so anything written to the other is lost, and symbol identity no longer answers "same declaration".
    /// </summary>
    [Theory]
    [InlineData("enum Color { Red, Green }", SymbolKind.Variable, SymbolKind.EnumType)]
    [InlineData("interface Foo { bar: number }", SymbolKind.Variable, SymbolKind.Interface)]
    [InlineData("type Foo = number;", SymbolKind.Type)]
    [InlineData("event ping(n: number);", SymbolKind.Event)]
    [InlineData("fn f(): number -> 1;", SymbolKind.Function)]
    [InlineData("let x = 1;", SymbolKind.Variable)]
    public void Declares_OneSymbolPerDeclaration(string source, params SymbolKind[] expectedKinds)
    {
        var model = Utility.GetSemanticModel(source);
        var symbols = model.GetDeclarationSymbols(model.Tree.Statements[0]);

        Assert.Equal(expectedKinds, symbols.Select(symbol => symbol.Kind));
        Assert.Equal(symbols.Count, symbols.Distinct().Count());
    }

    #region ThrowsFor
    [Fact]
    public void ThrowsFor_UninitializedConst()
    {
        var diagnostics = Utility.GetSemanticModel("let x;").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.MustHaveInitializer, "Immutable declarations must be initialized.");
    }

    [Fact]
    public void ThrowsFor_DuplicateVariable()
    {
        var diagnostics = Utility.GetSemanticModel("let x = 1; let x = 2;").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.DuplicateName, "Variable 'x' is already declared in this scope.");
    }

    [Theory]
    [InlineData("let Vector3 = 1;\nprint(Vector3);")]
    [InlineData("type Vector3 = number;\nlet v: Vector3 = 1;\nprint(v);")]
    [InlineData("fn Vector3(): number -> 1;\nprint(Vector3());")]
    public void Allows_ADeclaration_ToShadowAnIntrinsic(string source) => Utility.AssertNoErrors(Utility.GetSemanticModel(source).Diagnostics);

    /// <remarks>
    ///     A spread has no name of its own, so what has to hold is that the resolver reaches through it to
    ///     the operand - a spread of an undeclared name is an unresolved name, not a silently skipped node.
    /// </remarks>
    [Theory]
    [InlineData("let xs = [1]; let ys = [..xs];")]
    [InlineData("let xs = [1]; let ys = [0, ..xs];")]
    [InlineData("fn take(..ns: number[]): void { } let xs = [1]; take(..xs);")]
    public void Resolves_TheOperandOfASpread(string source) => Utility.AssertNoErrors(Utility.GetSemanticModel(source).Diagnostics);

    [Theory]
    [InlineData("let ys = [..missing];")]
    [InlineData("fn take(..ns: number[]): void { } take(..missing);")]
    public void ThrowsFor_SpreadOfAnUndeclaredName(string source) =>
        Utility.AssertDiagnostic(Utility.GetSemanticModel(source).Diagnostics, InternalCodes.CannotFindName, "Cannot find name 'missing'.");

    [Fact]
    public void Resolves_AnIntrinsic_ThatNothingShadows() =>
        Utility.AssertNoErrors(Utility.GetSemanticModel("let v: Vector3 = Vector3.zero;\nprint(v.x);").Diagnostics);

    [Fact]
    public void Allows_AModuleDeclaration_ToShadowAnAmbientGlobal() =>
        Utility.WithTempProject(
            [("types.d.loom", "declare let thing: number;"), ("main.loom", "let thing = 1;\nprint(thing);")],
            (_, result) => Utility.AssertNoErrors(result)
        );

    [Fact]
    public void Exports_ADeclaration_NamedLikeAnAmbientGlobal() =>
        Utility.WithTempProject(
            [
                ("types.d.loom", "declare let thing: number;"), ("m.loom", "export let thing = 1;"),
                ("main.loom", "import { thing } from \"./m\"\nprint(thing);")
            ],
            (_, result) => Utility.AssertNoErrors(result)
        );

    [Fact]
    public void Resolves_AnAmbientGlobal_ThatNothingShadows() =>
        Utility.WithTempProject(
            [("types.d.loom", "declare let thing: number;"), ("main.loom", "print(thing);")],
            (_, result) => Utility.AssertNoErrors(result)
        );

    [Fact]
    public void ThrowsFor_DuplicateFunction()
    {
        var diagnostics = Utility.GetSemanticModel("fn foo() {} fn foo() {}").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.DuplicateName, "Variable 'foo' is already declared in this scope.");
    }

    [Fact]
    public void ThrowsFor_DuplicateEnum()
    {
        var diagnostics = Utility.GetSemanticModel("enum Abc {} enum Abc{}").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.DuplicateName, "Variable 'Abc' is already declared in this scope.");
    }

    [Fact]
    public void ThrowsFor_DuplicateEnumTypeName()
    {
        var diagnostics = Utility.GetSemanticModel("type Abc = number[]; enum Abc {}").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.DuplicateName, "Type 'Abc' is already declared in this scope.");
    }

    [Fact]
    public void ThrowsFor_DuplicateInterface()
    {
        var diagnostics = Utility.GetSemanticModel("interface Abc; interface Abc;").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.DuplicateName, "Variable 'Abc' is already declared in this scope.");
    }

    [Fact]
    public void ThrowsFor_DuplicateInterfaceTypeName()
    {
        var diagnostics = Utility.GetSemanticModel("type Abc = number; interface Abc;").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.DuplicateName, "Type 'Abc' is already declared in this scope.");
    }

    [Fact]
    public void ThrowsFor_DuplicateInterfaceVariableName()
    {
        var diagnostics = Utility.GetSemanticModel("let Abc = 69; interface Abc;").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.DuplicateName, "Variable 'Abc' is already declared in this scope.");
    }

    [Fact]
    public void ThrowsFor_DuplicateTrait()
    {
        var diagnostics = Utility.GetSemanticModel("trait Abc {} trait Abc {}").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.DuplicateName, "Trait 'Abc' is already declared in this scope.");
    }

    [Fact]
    public void ThrowsFor_DuplicateTraitTypeName()
    {
        var diagnostics = Utility.GetSemanticModel("type Abc = number; trait Abc {}").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.DuplicateName, "Type 'Abc' is already declared in this scope.");
    }

    [Fact]
    public void ThrowsFor_DuplicateTraitInterface()
    {
        var diagnostics = Utility.GetSemanticModel("trait Abc {} interface Abc {}").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.DuplicateName, "Type 'Abc' is already declared in this scope.");
    }

    [Fact]
    public void ThrowsFor_DuplicateParameter()
    {
        var diagnostics = Utility.GetSemanticModel("fn foo(x: number, x: string) {}").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.DuplicateName, "Parameter 'x' is already declared for this function.");
    }

    [Fact]
    public void ThrowsFor_DuplicateDeclareVariable()
    {
        var diagnostics = Utility.GetSemanticModel("declare let x: number; declare let x: number;").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.DuplicateName, "Variable 'x' is already declared in this scope.");
    }

    [Fact]
    public void ThrowsFor_DuplicateDeclareFunction()
    {
        var diagnostics = Utility.GetSemanticModel("declare fn f(): void; declare fn f(): void;").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.DuplicateName, "Variable 'f' is already declared in this scope.");
    }

    [Fact]
    public void ThrowsFor_DuplicateDeclareFunctionParameter()
    {
        var diagnostics = Utility.GetSemanticModel("declare fn f(x: number, x: string): void;").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.DuplicateName, "Parameter 'x' is already declared for this function.");
    }

    [Fact]
    public void ThrowsFor_DuplicateDeclareInterface()
    {
        var diagnostics = Utility.GetSemanticModel("declare interface Abc; declare interface Abc;").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.DuplicateName, "Interface 'Abc' is already declared in this scope.");
    }

    [Fact]
    public void ThrowsFor_DuplicateDeclareInterfaceTypeName()
    {
        var diagnostics = Utility.GetSemanticModel("type Abc = number; declare interface Abc;").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.DuplicateName, "Type 'Abc' is already declared in this scope.");
    }

    [Fact]
    public void ThrowsFor_UndefinedVariable()
    {
        var diagnostics = Utility.GetSemanticModel("x;").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find name 'x'.");
    }

    [Fact]
    public void ThrowsFor_UndefinedVariable_InTypeOf()
    {
        var diagnostics = Utility.GetSemanticModel("type X = typeof(x);").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find name 'x'.");
    }

    [Fact]
    public void ThrowsFor_UndefinedVariable_InInterpolationHole()
    {
        var diagnostics = Utility.GetSemanticModel("""$"{x}";""").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find name 'x'.");
    }

    [Fact]
    public void Resolves_Variable_InInterpolationHole()
    {
        var model = Utility.GetSemanticModel("""let x = 1; $"{x}";""");
        Utility.AssertNoErrors(model.Diagnostics);

        var interpolated = Assert.IsType<InterpolatedStringLiteral>(Assert.IsType<ExpressionStatement>(model.Tree.Statements[1]).Expression);
        var identifier = Assert.IsType<Identifier>(Assert.IsType<InterpolationHolePart>(interpolated.Parts[0]).Expression);
        Assert.NotNull(model.GetSymbol(identifier));
    }

    [Fact]
    public void ThrowsFor_UndefinedType()
    {
        var diagnostics = Utility.GetSemanticModel("let x: Abc = 1").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find type 'Abc'.");
    }

    [Fact]
    public void ThrowsFor_DynamicEnumAccess()
    {
        var diagnostics = Utility.GetSemanticModel("enum Abc { A, B, C }; Abc").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.DynamicEnumAccess, "Cannot use enums dynamically because they are compile-time constants.");
    }

    [Fact]
    public void ThrowsFor_VariableInitializedInNestedBlock_UsedOutside()
    {
        var diagnostics = Utility.GetSemanticModel("{ let x = 42; } x;").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find name 'x'.");
    }

    [Fact]
    public void ThrowsFor_ReturnStatementOutsideFunction()
    {
        var diagnostics = Utility.GetSemanticModel("return 42;").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.ReturnOutsideFunction, "Return statements can only be used inside of functions.");
    }

    [Fact]
    public void ThrowsFor_DeclareVariableConflictsWithFunction()
    {
        var diagnostics = Utility.GetSemanticModel("fn foo() {} declare let foo: number;").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.DuplicateName, "Variable 'foo' is already declared in this scope.");
    }

    [Fact]
    public void ThrowsFor_DeclareFunctionConflictsWithVariable()
    {
        var diagnostics = Utility.GetSemanticModel("let x = 1; declare fn x(): void;").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.DuplicateName, "Variable 'x' is already declared in this scope.");
    }

    [Fact]
    public void ThrowsFor_DeclaredVariableNotVisibleOutsideBlock()
    {
        var diagnostics = Utility.GetSemanticModel("{ declare let x: number; } x;").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find name 'x'.");
    }

    [Fact]
    public void ThrowsFor_Interface_DuplicateIndexer()
    {
        var diagnostics = Utility.GetSemanticModel("interface I { [number]: string, [string]: bool }").Diagnostics;
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.DuplicateIndexer,
            "Type 'I' may only have one indexer."
        );
    }

    [Fact]
    public void ThrowsFor_Interface_DuplicateProperty()
    {
        var diagnostics = Utility.GetSemanticModel("interface I { x: number, x: string }").Diagnostics;
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.DuplicateName,
            "Property 'x' already exists on type 'I'"
        );
    }

    [Fact]
    public void Resolves_Interface_DuplicateFunctionProperty_AsOverloadSet()
    {
        var diagnostics = Utility.GetSemanticModel("interface I { create: fn(): number, create: fn(x: number): number }").Diagnostics;
        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void ThrowsFor_Parameter_MissingTypeAndDefault()
    {
        var diagnostics = Utility.GetSemanticModel("fn foo(x) {}").Diagnostics;
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.MustHaveDefaultOrType,
            "Parameter must have a declared type or default value to infer from."
        );
    }

    [Fact]
    public void Allows_UntypedParameter_OnFunctionExpressionConnectedToEvent() =>
        Utility.AssertNoErrors(Utility.GetSemanticModel("event abc(x: number); abc += fn(x) { };"));

    [Fact]
    public void Allows_UntypedParameter_OnFunctionExpressionDisconnectedFromEvent() =>
        Utility.AssertNoErrors(Utility.GetSemanticModel("event abc(x: number); abc -= fn(x) { };"));

    [Fact]
    public void Allows_UntypedParameter_OnFunctionExpressionConnectedOnceToEvent() =>
        Utility.AssertNoErrors(Utility.GetSemanticModel("event abc(x: number); abc ^= fn(x) { };"));

    [Fact]
    public void ThrowsFor_UntypedParameter_OnFunctionExpression_NotConnectedToAnything()
    {
        var diagnostics = Utility.GetSemanticModel("let f = fn(x) { };").Diagnostics;
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.MustHaveDefaultOrType,
            "Parameter must have a declared type or default value to infer from."
        );
    }

    [Fact]
    public void ThrowsFor_UntypedParameter_OnFunctionExpressionAssignedWithEquals()
    {
        var diagnostics = Utility.GetSemanticModel("mut f = fn() {}; f = fn(x) { };").Diagnostics;
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.MustHaveDefaultOrType,
            "Parameter must have a declared type or default value to infer from."
        );
    }

    [Fact]
    public void ThrowsFor_BreakOutsideLoop()
    {
        var diagnostics = Utility.GetSemanticModel("break").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.BreakOutsideLoop, "Break statements can only be used inside of loops.");
    }

    [Fact]
    public void ThrowsFor_ContinueOutsideLoop()
    {
        var diagnostics = Utility.GetSemanticModel("continue").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.ContinueOutsideLoop, "Continue statements can only be used inside of loops.");
    }

    [Fact]
    public void ThrowsFor_BreakInsideFunctionInsideLoop()
    {
        var diagnostics = Utility.GetSemanticModel("while true { fn inner() { break } }").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.BreakOutsideLoop, "Break statements can only be used inside of loops.");
    }

    [Fact]
    public void ThrowsFor_ContinueInsideFunctionInsideLoop()
    {
        var diagnostics = Utility.GetSemanticModel("while true { fn inner() { continue } }").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.ContinueOutsideLoop, "Continue statements can only be used inside of loops.");
    }

    [Fact]
    public void ThrowsFor_Sealed_Inheritance()
    {
        var diagnostics = Utility.GetSemanticModel("sealed interface A; interface B: A;").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InheritFromSealed, "Cannot constrain interface 'B' with sealed interface 'A'.");
    }

    [Theory]
    [InlineData("interface I : number;")]
    [InlineData("interface I : 69;")]
    [InlineData("type A = number; interface I : A;")]
    public void ThrowsFor_Interface_ConstraintNotInterface(string source)
    {
        var diagnostics = Utility.GetSemanticModel(source).Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.NonInterfaceConstraint, "Interfaces may only be constrained by other interfaces.");
    }

    [Fact]
    public void ThrowsFor_DeclaredInterface_Invocation()
    {
        var diagnostics = Utility.GetSemanticModel("declare interface A; let a = new A {}").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvokeDeclaredInterface, "Cannot invoke interface 'A' because it was declared as a type.");
    }

    [Fact]
    public void ThrowsFor_ReturnInsideAfterBody_OutsideFunction()
    {
        var diagnostics = Utility.GetSemanticModel("after 1s { return 42; }").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.ReturnOutsideFunction, "Return statements can only be used inside of functions.");
    }

    [Fact]
    public void ThrowsFor_AfterCondition_UsesUndefinedVariable()
    {
        var diagnostics = Utility.GetSemanticModel("after unknown { }").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find name 'unknown'.");
    }

    [Fact]
    public void ThrowsFor_VariableDeclaredInAfterBody_UsedOutside()
    {
        var diagnostics = Utility.GetSemanticModel("after 1s { let x = 42; } x;").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find name 'x'.");
    }

    [Fact]
    public void ThrowsFor_ContinueInsideAfter()
    {
        var diagnostics = Utility.GetSemanticModel("after 1s { continue }").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.ContinueOutsideLoop, "Continue statements can only be used inside of loops.");
    }

    [Fact]
    public void ThrowsFor_BreakInsideAfter()
    {
        var diagnostics = Utility.GetSemanticModel("after 1s { break }").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.BreakOutsideLoop, "Break statements can only be used inside of loops.");
    }

    [Fact]
    public void ThrowsFor_BreakInsideAfter_NestedInLoop()
    {
        var diagnostics = Utility.GetSemanticModel("while true { after 1s { break } }").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.BreakOutsideLoop, "Break statements can only be used inside of loops.");
    }

    [Fact]
    public void ThrowsFor_ReturnInsideAfter()
    {
        var diagnostics = Utility.GetSemanticModel("fn abc { after 1s { return 69 } }").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.ReturnInAfter, "Cannot return a value from an 'after' statement body.");
    }

    [Fact]
    public void ThrowsFor_ReturnInsideEveryBody_OutsideFunction()
    {
        var diagnostics = Utility.GetSemanticModel("every 1s { return 42; }").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.ReturnOutsideFunction, "Return statements can only be used inside of functions.");
    }

    [Fact]
    public void ThrowsFor_EveryDuration_UsesUndefinedVariable()
    {
        var diagnostics = Utility.GetSemanticModel("every unknown { }").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find name 'unknown'.");
    }

    [Fact]
    public void ThrowsFor_EveryCondition_UsesUndefinedVariable()
    {
        var diagnostics = Utility.GetSemanticModel("every 1s while unknown { }").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find name 'unknown'.");
    }

    [Fact]
    public void ThrowsFor_VariableDeclaredInEveryBody_UsedOutside()
    {
        var diagnostics = Utility.GetSemanticModel("every 1s { let x = 42; } x;").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find name 'x'.");
    }

    [Fact]
    public void ThrowsFor_ContinueInsideEvery()
    {
        var diagnostics = Utility.GetSemanticModel("every 1s { continue }").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.ContinueOutsideLoop, "Continue statements can only be used inside of loops.");
    }

    [Fact]
    public void ThrowsFor_BreakInsideEvery()
    {
        var diagnostics = Utility.GetSemanticModel("every 1s { break }").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.BreakOutsideLoop, "Break statements can only be used inside of loops.");
    }

    [Fact]
    public void ThrowsFor_BreakInsideEvery_NestedInLoop()
    {
        var diagnostics = Utility.GetSemanticModel("while true { every 1s { break } }").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.BreakOutsideLoop, "Break statements can only be used inside of loops.");
    }

    [Fact]
    public void ThrowsFor_ReturnInsideEvery()
    {
        var diagnostics = Utility.GetSemanticModel("fn abc { every 1s { return 69 } }").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.ReturnInAfter, "Cannot return a value from an 'every' statement body.");
    }

    [Fact]
    public void ThrowsFor_ReturnInsideAfter_NestedInsideEvery()
    {
        var diagnostics = Utility.GetSemanticModel("fn abc { every 1s { after 1s { return 69 } } }").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.ReturnInAfter, "Cannot return a value from an 'after' statement body.");
    }

    [Fact]
    public void ThrowsFor_ReturnInsideEvery_NestedInsideAfter()
    {
        var diagnostics = Utility.GetSemanticModel("fn abc { after 1s { every 1s { return 69 } } }").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.ReturnInAfter, "Cannot return a value from an 'every' statement body.");
    }

    [Fact]
    public void Allows_ReturnInsideFunctionExpression_NestedInsideEvery() =>
        Utility.AssertNoErrors(Utility.GetSemanticModel("every 1s { let f = fn(): number { return 69; }; }"));

    [Fact]
    public void ThrowsFor_ErrorPropagationOutsideFunction()
    {
        var diagnostics = Utility.GetSemanticModel("fn get() {} get()?;").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.ErrorPropagationOutsideFunction, "'?' can only be used inside of functions.");
    }

    [Fact]
    public void ThrowsFor_ErrorPropagationInsideAfter()
    {
        var diagnostics = Utility.GetSemanticModel("fn get() {} fn abc { after 1s { get()?; } }").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.ErrorPropagationInAfter, "Cannot use '?' inside an 'after' statement body.");
    }

    [Fact]
    public void ThrowsFor_ErrorPropagationInsideEvery()
    {
        var diagnostics = Utility.GetSemanticModel("fn get() {} fn abc { every 1s { get()?; } }").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.ErrorPropagationInAfter, "Cannot use '?' inside an 'every' statement body.");
    }

    [Fact]
    public void Allows_ErrorPropagationInsideFunctionExpression_NestedInsideEvery() =>
        Utility.AssertNoErrors(Utility.GetSemanticModel("fn get() {} every 1s { let f = fn() { get()?; }; }"));

    [Fact]
    public void ThrowsFor_DeclareVariable_MissingType()
    {
        var diagnostics = Utility.GetSemanticModel("declare let x").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.MissingDeclareVariableType, "Declared variable signatures must have a type.");
    }

    [Fact]
    public void ThrowsFor_ForLoop_NonObjectCollection_Number()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("for x : 42 { }");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '42' is not assignable to type 'object'.");
    }

    [Fact]
    public void ThrowsFor_ForLoop_NonObjectCollection_Bool()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("for x : true { }");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type 'true' is not assignable to type 'object'.");
    }

    [Fact]
    public void ThrowsFor_ForLoop_NonObjectCollection_String()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("for x : \"abc\" { }");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '\"abc\"' is not assignable to type 'object'.");
    }

    [Fact]
    public void ThrowsFor_ForLoop_NonObjectCollection_Optional()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("interface Foo {}; let a: Foo? = new Foo {}; for x : a { }");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type 'Foo?' is not assignable to type 'object'.");
    }

    [Fact]
    public void ThrowsFor_RuntimeStatement_InDeclarationFile()
    {
        var diagnostics = Utility.GetSemanticModel("let x = 1;", true).Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.RuntimeInDeclarationFile, "Only type-level declarations are allowed in declaration files.");
    }

    [Fact]
    public void ThrowsFor_Trait_DuplicateMethod()
    {
        var diagnostics = Utility.GetSemanticModel(
                """
                trait Iterator {
                    fn next(): number
                    fn next(): string
                }
                """
            )
            .Diagnostics;

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.DuplicateName,
            "Method 'next' already exists on trait 'Iterator'"
        );
    }

    [Fact]
    public void ThrowsFor_Implement_NonTrait()
    {
        var diagnostics = Utility.GetSemanticModel(
                """
                interface Foo { }
                interface Bar { }

                implement Foo for Bar { }
                """
            )
            .Diagnostics;

        Utility.AssertDiagnostic(diagnostics, InternalCodes.NonInterfaceImplementation, "Interfaces may only implement traits.");
    }

    [Fact]
    public void ThrowsFor_Implement_NonInterface()
    {
        var diagnostics = Utility.GetSemanticModel(
                """
                trait Foo { }

                type Bar = number

                implement Foo for Bar { }
                """
            )
            .Diagnostics;

        Utility.AssertDiagnostic(diagnostics, InternalCodes.NonInterfaceImplementation, "Traits may only be implemented by interfaces.");
    }

    [Fact]
    public void ThrowsFor_DuplicateTraitImplementation()
    {
        var diagnostics = Utility.GetSemanticModel(
                """
                trait Foo { }

                interface Bar { }

                implement Foo for Bar { }
                implement Foo for Bar { }
                """
            )
            .Diagnostics;

        Utility.AssertDiagnostic(diagnostics, InternalCodes.DuplicateImplementation, "Interface 'Bar' already has an implementation for trait 'Foo'");
    }

    [Fact]
    public void ThrowsFor_InvalidImplementationMethod()
    {
        var diagnostics = Utility.GetSemanticModel(
                """
                trait Foo {
                    fn a(): void
                }

                interface Bar { }

                implement Foo for Bar {
                    fn b() { }
                }
                """
            )
            .Diagnostics;

        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidImplementation, "Trait 'Foo' does not contain a signature for method 'b'");
    }

    [Fact]
    public void ThrowsFor_MissingImplementation()
    {
        var diagnostics = Utility.GetSemanticModel(
                """
                trait Foo {
                    fn a(): void
                    fn b(): void
                }

                interface Bar { }

                implement Foo for Bar {
                    fn a() { }
                }
                """
            )
            .Diagnostics;

        Utility.AssertDiagnostic(diagnostics, InternalCodes.MissingImplementation, "Implementation of trait 'Foo' on interface 'Bar' is missing method 'b'");
    }

    [Fact]
    public void Allows_Implement_OmittingMethodWithDefault() =>
        Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                trait Foo {
                    fn a(): void
                    fn b(): void -> print("default");
                }

                interface Bar { }

                implement Foo for Bar {
                    fn a() { }
                }
                """
            )
        );

    [Theory]
    [InlineData("deep_equal")]
    [InlineData("deep_hash")]
    [InlineData("deep_display")]
    public void ThrowsFor_InternalRuntimeHelper_NotReachableAsLoomName(string name)
    {
        var diagnostics = Utility.GetSemanticModel($"{name}(1, 2)").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, $"Cannot find name '{name}'.");
    }

    [Fact]
    public void Allows_SelfExpression_InsideDefaultTraitMethodBody() =>
        Utility.AssertNoErrors(
            Utility.GetSemanticModel("trait Foo { fn a(): unknown -> @; }")
        );

    [Fact]
    public void Allows_SelfExpression_InsideClosureNestedInDefaultTraitMethodBody() =>
        Utility.AssertNoErrors(
            Utility.GetSemanticModel("trait Foo { fn a(): void -> (fn() { print(@); })(); }")
        );

    [Fact]
    public void ThrowsFor_TwoTraits_BothDefaultingSameMethodName_NeitherOverridden()
    {
        var diagnostics = Utility.GetSemanticModel(
            """
            trait A { fn describe(): string -> "a"; }
            trait B { fn describe(): string -> "b"; }
            interface X { }
            implement A for X { }
            implement B for X { }
            """
        ).Diagnostics;

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.AmbiguousTraitDefault,
            "Traits 'A' and 'B' both default method 'describe' on interface 'X' - override 'describe' explicitly to resolve the ambiguity."
        );
    }

    [Fact]
    public void Allows_TwoTraits_BothDefaultingSameMethodName_WhenOneOverrides() =>
        Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                trait A { fn describe(): string -> "a"; }
                trait B { fn describe(): string -> "b"; }
                interface X { }
                implement A for X { }
                implement B for X { fn describe(): string -> "explicit"; }
                """
            )
        );

    [Fact]
    public void ThrowsFor_IntrinsicImplementation()
    {
        var diagnostics = Utility.GetSemanticModel(
                """
                trait Foo {
                    fn a(): void
                }

                implement Foo for Range {
                    fn b() { }
                }
                """
            )
            .Diagnostics;

        Utility.AssertDiagnostic(diagnostics, InternalCodes.IntrinsicImplementation, "Trait 'Foo' may not be implemented on intrinsic interface 'Range'.");
    }

    [Fact]
    public void ThrowsFor_MatchBinding_UsedOutsideArm()
    {
        var diagnostics = Utility.GetSemanticModel("match 1 { x -> x }; x;").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find name 'x'.");
    }

    [Fact]
    public void ThrowsFor_MatchBinding_NotVisibleInOtherArm()
    {
        var diagnostics = Utility.GetSemanticModel("match 1 { a -> a, _ -> a }").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find name 'a'.");
    }

    [Fact]
    public void ThrowsFor_Match_UnknownNameInBody()
    {
        var diagnostics = Utility.GetSemanticModel("match 1 { _ -> y }").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find name 'y'.");
    }

    [Fact]
    public void ThrowsFor_Match_DuplicatePatternBinding()
    {
        var diagnostics = Utility.GetSemanticModel("match 1 { [a, a] -> a }").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.DuplicateName, "Variable 'a' is already declared in this scope.");
    }

    [Fact]
    public void ThrowsFor_Match_UnknownTypeInTypedPattern()
    {
        var diagnostics = Utility.GetSemanticModel("match 1 { s when Foo -> s }").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find type 'Foo'.");
    }

    [Fact]
    public void ThrowsFor_Match_UnknownScrutinee()
    {
        var diagnostics = Utility.GetSemanticModel("match missing { _ -> 0 }").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find name 'missing'.");
    }

    [Fact]
    public void ThrowsFor_MatchTypePattern_DoesNotBindOuterName()
    {
        // a bare type pattern ('Foo { ... }') captures nothing under a name of its own, unlike a typed
        // pattern ('f when Foo') - only its object sub-pattern's own fields get bound
        var diagnostics = Utility.GetSemanticModel("interface Foo {}; match 1 { Foo {} -> f }").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find name 'f'.");
    }

    [Fact]
    public void ThrowsFor_IsExpression_DoesNotBindOuterName()
    {
        var diagnostics = Utility.GetSemanticModel("interface Foo {}; let value = none as never as Foo; if value is Foo { print(value) }; f;").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find name 'f'.");
    }

    [Fact]
    public void Allows_IsExpression_ObjectPatternBinding_InsideThenBranch() =>
        Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                interface Foo { some_field: number }
                let value = none as never as Foo;
                if value is Foo { some_field: x } {
                    print(x)
                }
                """
            )
        );

    [Fact]
    public void ThrowsFor_IsExpression_ObjectPatternBinding_LeaksOutsideThenBranch()
    {
        var diagnostics = Utility.GetSemanticModel(
            """
            interface Foo { some_field: number }
            let value = none as never as Foo;
            if value is Foo { some_field: x } {
                print(x)
            }
            print(x)
            """
        ).Diagnostics;

        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find name 'x'.");
    }

    [Fact]
    public void Allows_IsExpression_ObjectPatternBinding_UsedInAndChainedCondition() =>
        Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                interface Foo { some_field: number }
                let value = none as never as Foo;
                if value is Foo { some_field: n } && n > 0 {
                    print(n)
                }
                """
            )
        );

    [Fact]
    public void ThrowsFor_ExportMutable()
    {
        var diagnostics = Utility.GetSemanticModel("export mut x = 1;").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotExportMutable, "Mutable variables cannot be exported.", "use 'let' instead of 'mut'");
    }

    [Fact]
    public void ThrowsFor_ExportOutsideModuleScope()
    {
        var diagnostics = Utility.GetSemanticModel("fn f() { export let x = 1; }").Diagnostics;
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.ExportOutsideModuleScope,
            "Declarations can only be exported at the top level of a module.",
            "move the 'export' declaration out of the enclosing block"
        );
    }
    #endregion ThrowsFor

    #region Resolves
    [Fact]
    public void Resolves_ExportedDeclarations()
    {
        var model = Utility.AssertNoErrors(Utility.GetSemanticModel("export let constant = 69; export fn do_something() { }"));

        Assert.Equal(2, model.Exports.Count);
        Assert.Equal(["constant", "do_something"], model.Exports.Select(s => s.Name));

        var variable = Assert.IsType<ExportDeclaration>(model.Tree.Statements[0]).Declaration;
        Assert.Same(model.GetDeclarationSymbol(variable), model.Exports[0].Symbol);
    }

    [Fact]
    public void Resolves_ExportedTypeDeclarations()
    {
        var model = Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                export type Alias = number;
                export interface Point { x: number y: number }
                export sealed interface Handle;
                export enum Direction { Up, Down }
                export trait Drawable { fn draw: void; }
                """
            )
        );

        // interfaces and enums declare a value symbol as well as a type symbol, and both are exported so
        // that an importer can use the name in either namespace
        Assert.Equal(
            [
                "Alias",
                "Point",
                "Point",
                "Handle",
                "Handle",
                "Direction",
                "Direction",
                "Drawable"
            ],
            model.Exports.Select(s => s.Name)
        );

        Assert.Equal(
            [
                SymbolKind.Type,
                SymbolKind.Variable,
                SymbolKind.Interface,
                SymbolKind.Variable,
                SymbolKind.Interface,
                SymbolKind.Variable,
                SymbolKind.EnumType,
                SymbolKind.Trait
            ],
            model.Exports.Select(s => s.Symbol.Kind)
        );

        // ...which is what FindExports hands an importing module
        Assert.Equal([SymbolKind.Variable, SymbolKind.Interface], model.FindExports("Point").Select(s => s.Symbol.Kind));
        Assert.Equal([SymbolKind.Type], model.FindExports("Alias").Select(s => s.Symbol.Kind));
        Assert.Empty(model.FindExports("Nope"));

        // none of these emit a runtime local, so none reach the module's return table
        Assert.DoesNotContain(model.Exports, s => s.EmitsRuntimeBinding);

        var trait = Assert.IsType<ExportDeclaration>(model.Tree.Statements[4]).Declaration;
        Assert.Same(model.GetDeclarationSymbol(trait), model.Exports[7].Symbol);
    }

    [Fact]
    public void Resolves_InterfaceAndTraitRelationship()
    {
        var model = Utility.AssertNoErrors(
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

        var trait = Assert.IsType<TraitDeclaration>(model.Tree.Statements[0]);
        var iface = Assert.IsType<InterfaceDeclaration>(model.Tree.Statements[1]);

        var traitSymbol = Assert.IsType<TraitSymbol>(model.GetDeclarationSymbol(trait, SymbolKind.Trait));

        var interfaceSymbol = Assert.IsType<InterfaceSymbol>(model.GetDeclarationSymbol(iface, SymbolKind.Interface));

        Assert.Single(interfaceSymbol.Implements);
        Assert.Same(traitSymbol, interfaceSymbol.Implements[0]);

        Assert.Single(traitSymbol.ImplementedBy);
        Assert.Same(interfaceSymbol, traitSymbol.ImplementedBy[0]);
    }

    [Fact]
    public void Resolves_InterfaceImplementationDeclaration()
    {
        var model = Utility.AssertNoErrors(
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

        var implement = Assert.IsType<Implement>(model.Tree.Statements[2]);

        var iface = Assert.IsType<InterfaceDeclaration>(model.Tree.Statements[1]);
        var symbol = Assert.IsType<InterfaceSymbol>(model.GetDeclarationSymbol(iface, SymbolKind.Interface));

        var implementation = Assert.Single(symbol.Implementations);

        Assert.Same(implement, implementation);
    }

    [Fact]
    public void Resolves_SelfExpression_ToImplementedInterface()
    {
        var model = Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                interface WithIndexer { [string]: number }

                trait GetValue<K, V> {
                    fn get_value(key: K): V
                }

                implement GetValue<string, number> for WithIndexer {
                    fn get_value(key) -> @[key];
                }
                """
            )
        );

        var iface = Assert.IsType<InterfaceDeclaration>(model.Tree.Statements[0]);
        var interfaceSymbol = Assert.IsType<InterfaceSymbol>(model.GetDeclarationSymbol(iface, SymbolKind.Interface));

        var implement = Assert.IsType<Implement>(model.Tree.Statements[2]);
        var method = Assert.Single(implement.Body.Implementations);
        var body = Assert.IsType<ExpressionBody>(method.Body);
        var elementAccess = Assert.IsType<ElementAccess>(body.Expression);
        var selfExpression = Assert.IsType<SelfExpression>(elementAccess.Expression);

        Assert.Same(interfaceSymbol, model.GetSymbol(selfExpression));
    }

    [Fact]
    public void Resolves_TraitTypeArgument_ReferencingUserDefinedInterface()
    {
        var model = Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                interface Bar { name: string }

                trait Serialize<T> {
                    fn serialize: T;
                }

                interface Foo { name: string }

                implement Serialize<Bar> for Foo {
                    fn serialize -> new Bar { name: "hi" }
                }
                """
            )
        );

        var bar = Assert.IsType<InterfaceDeclaration>(model.Tree.Statements[0]);
        var barSymbol = Assert.IsType<InterfaceSymbol>(model.GetDeclarationSymbol(bar, SymbolKind.Interface));

        var implement = Assert.IsType<Implement>(model.Tree.Statements[3]);
        var typeArgument = Assert.Single(implement.TraitName.TypeArguments!.ArgumentsList);
        Assert.Same(barSymbol, model.GetSymbol(typeArgument));
    }

    [Fact]
    public void ThrowsFor_SelfExpression_OutsideImplementation()
    {
        var diagnostics = Utility.GetSemanticModel("let x = @;").Diagnostics;
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.SelfOutsideImplementation,
            "'@' can only be used inside an implemented trait method or as a type predicate subject on an interface or trait member."
        );
    }

    [Fact]
    public void Resolves_SelfExpression_AsInterfaceTypePredicateSubject()
    {
        var diagnostics = Utility.GetSemanticModel("interface Container { is_kind: fn<T>(): @ is T }").Diagnostics;
        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Resolves_SelfExpression_AsTraitTypePredicateSubject()
    {
        var diagnostics = Utility.GetSemanticModel("trait HasKind<T> { fn is_kind(): @ is T; }").Diagnostics;
        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Resolves_AttributedDeclareFunctionSignature_AsFunctionSymbol_WithLuauNameAttribute()
    {
        var model = Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                [luau_name("typeof")]
                declare fn type_of(value: unknown): string;
                """
            )
        );

        var declare = Assert.IsType<Declare>(model.Tree.Statements.Single());
        var signature = Assert.IsType<DeclareFunctionSignature>(declare.Signature);
        var symbol = Assert.IsType<FunctionSymbol>(model.GetDeclarationSymbol(signature, SymbolKind.Function));

        Assert.True(symbol.TryGetIntrinsicAttribute("luau_name", out var attribute));
        Assert.Equal("luau_name", attribute.Name);
    }

    /// <summary>
    ///     Carrying attributes is not what decides a symbol's class: an unattributed signature is the same
    ///     <see cref="FunctionSymbol" /> as an attributed one, with nothing on it.
    /// </summary>
    [Fact]
    public void Resolves_NonAttributedDeclareFunctionSignature_AsFunctionSymbol_WithNoAttributes()
    {
        var model = Utility.AssertNoErrors(Utility.GetSemanticModel("declare fn print(..data: unknown[]): void;"));

        var declare = Assert.IsType<Declare>(model.Tree.Statements.Single());
        var signature = Assert.IsType<DeclareFunctionSignature>(declare.Signature);
        var symbol = Assert.IsType<FunctionSymbol>(model.GetDeclarationSymbol(signature, SymbolKind.Function));

        Assert.Empty(symbol.Attributes);
    }

    [Fact]
    public void Resolves_BareCall_ToMethodFromOtherImplementedTrait()
    {
        var model = Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                interface Container { value: number }
                trait Display { fn display: void }
                trait Balls { fn balls: void }

                implement Balls for Container {
                    fn balls -> print(@.value);
                }

                implement Display for Container {
                    fn display -> print(balls());
                }
                """
            )
        );

        var ballsImplement = Assert.IsType<Implement>(model.Tree.Statements[3]);
        var ballsMethod = Assert.Single(ballsImplement.Body.Implementations);

        var displayImplement = Assert.IsType<Implement>(model.Tree.Statements[4]);
        var displayMethod = Assert.Single(displayImplement.Body.Implementations);
        var body = Assert.IsType<ExpressionBody>(displayMethod.Body);
        var printCall = Assert.IsType<Invocation>(body.Expression);
        var ballsCall = Assert.IsType<Invocation>(Assert.Single(printCall.Arguments.ArgumentList));
        var callee = Assert.IsType<Identifier>(ballsCall.Expression);

        var symbol = model.GetSymbol(callee);
        Assert.Equal(SymbolKind.Function, symbol?.Kind);
        Assert.Same(ballsMethod, symbol?.Declaration);
    }

    [Fact]
    public void ThrowsFor_BareCall_ToMethodFromTraitImplementedLaterInFile()
    {
        var diagnostics = Utility.GetSemanticModel(
            """
            interface Container { value: number }
            trait Display { fn display: void }
            trait Balls { fn balls: void }

            implement Display for Container {
                fn display -> print(balls());
            }

            implement Balls for Container {
                fn balls -> print(@.value);
            }
            """
        ).Diagnostics;

        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find name 'balls'.");
    }

    [Fact]
    public void Resolves_PropertyPointsToInterface()
    {
        var model = Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                interface Address { }

                interface Person {
                    address: Address
                }
                """
            )
        );

        var person = Assert.IsType<InterfaceDeclaration>(model.Tree.Statements[1]);

        var symbol = Assert.IsType<InterfaceSymbol>(model.GetDeclarationSymbol(person, SymbolKind.Interface));

        var property = Assert.Single(symbol.Properties);

        Assert.NotNull(property.PointsTo);
        Assert.Equal("Address", property.PointsTo!.Name);
    }

    [Fact]
    public void Resolves_PropertyPath()
    {
        var model = Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                interface City {
                    name: string
                }

                interface Address {
                    city: City
                }

                interface Person {
                    address: Address
                }
                """
            )
        );

        var person = Assert.IsType<InterfaceDeclaration>(model.Tree.Statements[2]);

        var symbol = Assert.IsType<InterfaceSymbol>(model.GetDeclarationSymbol(person, SymbolKind.Interface));

        var property = symbol.GetPropertyAtPath(["address", "city", "name"]);

        Assert.NotNull(property);
        Assert.Equal("name", property.Name);
    }

    [Fact]
    public void Resolves_PropertyAttributes()
    {
        var model = Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                declare fn some_attribute: fn: void;
                interface Person {
                    [some_attribute]
                    name: string
                }
                """
            )
        );

        Assert.Equal(2, model.Tree.Statements.Count);
        var person = Assert.IsType<InterfaceDeclaration>(model.Tree.Statements.Last());
        var symbol = Assert.IsType<InterfaceSymbol>(model.GetDeclarationSymbol(person, SymbolKind.Interface));
        var property = Assert.Single(symbol.Properties);
        var attribute = Assert.Single(property.Attributes);
        Assert.Equal("some_attribute", attribute.Name);
    }

    [Fact]
    public void Resolves_MultipleImplementedTraits()
    {
        var model = Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                trait A { fn a(): void }
                trait B { fn b(): void }

                interface Foo { }

                implement A for Foo {
                    fn a() { }
                }

                implement B for Foo {
                    fn b() { }
                }
                """
            )
        );

        var iface = Assert.IsType<InterfaceDeclaration>(model.Tree.Statements[2]);

        var symbol = Assert.IsType<InterfaceSymbol>(model.GetDeclarationSymbol(iface, SymbolKind.Interface));

        Assert.Equal(2, symbol.Implements.Count);
        Assert.Equal(2, symbol.Implementations.Count);
    }

    [Fact]
    public void Resolves_ImplementTraitReference()
    {
        var model = Utility.AssertNoErrors(
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

        var implement = Assert.IsType<Implement>(model.Tree.Statements.Last());
        var symbol = model.GetSymbol(implement.TraitName);
        Assert.NotNull(symbol);
        Assert.Equal(SymbolKind.Trait, symbol.Kind);
        Assert.Equal("Iterator", symbol.Name);
    }

    [Fact]
    public void Resolves_ImplementInterfaceReference()
    {
        var model = Utility.AssertNoErrors(
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

        var implement = Assert.IsType<Implement>(model.Tree.Statements.Last());
        var symbol = model.GetSymbol(implement.InterfaceName);
        Assert.NotNull(symbol);
        Assert.Equal(SymbolKind.Interface, symbol.Kind);
        Assert.Equal("List", symbol.Name);
    }

    [Fact]
    public void Resolves_TraitTypeParameter()
    {
        var model = Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                trait Iterator<T> {
                    fn next(): T
                }
                """
            )
        );

        var trait = Assert.IsType<TraitDeclaration>(model.Tree.Statements.Single());
        var member = Assert.Single(trait.Body.Members);
        var returnType = Assert.IsType<TypeName>(member.ReturnType.Type);
        var symbol = model.GetSymbol(returnType);

        Assert.NotNull(symbol);
        Assert.Equal(SymbolKind.Type, symbol.Kind);
        Assert.Equal("T", symbol.Name);
    }

    [Fact]
    public void Resolves_TraitTypeReference()
    {
        var model = Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                trait Iterator {
                    fn next(): number
                }

                mut x: Iterator
                """
            )
        );

        var declaration = Assert.IsType<VariableDeclaration>(model.Tree.Statements.Last());
        var typeName = Assert.IsType<TypeName>(declaration.ColonTypeClause!.Type);
        var symbol = model.GetSymbol(typeName);
        Assert.NotNull(symbol);
        Assert.Equal("Iterator", symbol.Name);
        Assert.Equal(SymbolKind.Trait, symbol.Kind);
    }

    [Fact]
    public void Resolves_TypeParameter_InFunctionSignature()
    {
        var model = Utility.AssertNoErrors(Utility.GetSemanticModel("fn identity<T>(x: T): T { return x; }"));
        var fn = Assert.IsType<FunctionDeclaration>(model.Tree.Statements.Single());
        var returnType = fn.ReturnType!.Type as TypeName;
        Assert.NotNull(returnType);
        Assert.Equal("T", returnType.Name.Text);

        var symbol = model.GetSymbol(returnType);
        Assert.NotNull(symbol);
        Assert.Equal(SymbolKind.Type, symbol.Kind);
        Assert.Equal("T", symbol.Name);
    }

    [Fact]
    public void Resolves_TypeParameter_InTypeAlias()
    {
        var model = Utility.AssertNoErrors(Utility.GetSemanticModel("type Container<T> = T"));
        var alias = Assert.IsType<TypeAlias>(model.Tree.Statements.Single());
        var typeName = Assert.IsType<TypeName>(alias.EqualsTypeClause.Type);
        var symbol = model.GetSymbol(typeName);
        Assert.NotNull(symbol);
        Assert.Equal("T", symbol.Name);
        Assert.Equal(SymbolKind.Type, symbol.Kind);
    }

    [Fact]
    public void Resolves_TypeParameter_Shadowing()
    {
        var model = Utility.AssertNoErrors(Utility.GetSemanticModel("type Outer<T> = fn<T>(x: T): T"));
        var alias = Assert.IsType<TypeAlias>(model.Tree.Statements.Single());
        var fnType = Assert.IsType<FunctionType>(alias.EqualsTypeClause.Type);
        var paramType = fnType.Parameters!.ParameterList[0].ColonTypeClause!.Type as TypeName;
        Assert.NotNull(paramType);

        var symbol = model.GetSymbol(paramType);
        Assert.NotNull(symbol);
        Assert.Equal("T", symbol.Name);
        Assert.Equal(fnType.TypeParameters!.ParameterList[0], symbol.Declaration);
    }

    [Fact]
    public void Resolves_Interface_WithGenericConstraint()
    {
        var model = Utility.AssertNoErrors(Utility.GetSemanticModel("type Foo = number; interface I<T: Foo> { }"));
        Assert.Equal(2, model.Tree.Statements.Count);

        var iface = Assert.IsType<InterfaceDeclaration>(model.Tree.Statements.Last());
        Assert.NotNull(iface.TypeParameters);

        var tp = iface.TypeParameters.ParameterList[0];
        Assert.NotNull(tp.ColonTypeClause);

        var constraint = tp.ColonTypeClause.Type;
        var symbol = model.GetSymbol(constraint);
        Assert.NotNull(symbol);
        Assert.Equal("Foo", symbol.Name);
    }

    [Fact]
    public void Resolves_TypeParameter_InTypeArgument()
    {
        var model = Utility.AssertNoErrors(Utility.GetSemanticModel("type Foo<T> = T; fn foo<T>(x: Foo<T>) { }"));
        Assert.Equal(2, model.Tree.Statements.Count);

        var fn = Assert.IsType<FunctionDeclaration>(model.Tree.Statements.Last());
        Assert.NotNull(fn.Parameters);

        var param = fn.Parameters.ParameterList[0];
        var typeName = Assert.IsType<TypeName>(param.ColonTypeClause!.Type);
        Assert.NotNull(typeName.TypeArguments);

        var arg = typeName.TypeArguments.ArgumentsList[0] as TypeName;
        Assert.NotNull(arg);

        var symbol = model.GetSymbol(arg);
        Assert.NotNull(symbol);
        Assert.Equal("T", symbol.Name);
    }

    [Fact]
    public void Resolves_IntrinsicTypeSymbols()
    {
        var model = Utility.AssertNoErrors(Utility.GetSemanticModel("mut x: Range;"));
        Assert.Single(model.Tree.Statements);

        var declaration = Assert.IsType<VariableDeclaration>(model.Tree.Statements.First());
        Assert.NotNull(declaration.ColonTypeClause);

        var symbol = model.GetSymbol(declaration.ColonTypeClause.Type);
        Assert.NotNull(symbol);
        Assert.True(symbol.IsGlobal);
        Assert.True(symbol.IsIntrinsic);
    }

    [Fact]
    public void Resolves_GlobalSymbols_FromCompilationUnit()
    {
        var model = Utility.AssertNoErrors(Utility.GetSemanticModel("print(42);"));
        Assert.Single(model.Tree.Statements);

        var stmt = Assert.IsType<ExpressionStatement>(model.Tree.Statements.First());
        var invoc = Assert.IsType<Invocation>(stmt.Expression);
        var ident = Assert.IsType<Identifier>(invoc.Expression);
        var symbol = model.GetSymbol(ident);
        Assert.NotNull(symbol);
        Assert.Equal("print", symbol.Name);
        Assert.True(symbol.IsGlobal);
        Assert.True(symbol.IsIntrinsic);
    }

    [Fact]
    public void Resolves_Declare_InsideBlock()
    {
        var model = Utility.AssertNoErrors(Utility.GetSemanticModel("if true { declare let x: number; }"));
        var ifStmt = Assert.IsType<If>(model.Tree.Statements.Single());
        var block = Assert.IsType<Block>(ifStmt.ThenBranch);
        var declare = Assert.IsType<Declare>(block.Statements.Single());
        var sig = Assert.IsType<DeclareVariableSignature>(declare.Signature);
        var symbol = model.GetDeclarationSymbol(sig);
        Assert.NotNull(symbol);
        Assert.Equal("x", symbol.Name);
    }

    [Fact]
    public void Resolves_FunctionName_InsideOwnBody()
    {
        var model = Utility.AssertNoErrors(
            Utility.GetSemanticModel("fn factorial(n: number): number { if n <= 1 { return 1 } else { return n * factorial(n - 1) } }")
        );

        var fn = Assert.IsType<FunctionDeclaration>(model.Tree.Statements.Single());
        var block = Assert.IsType<Block>(fn.Body);
        var ifStmt = Assert.IsType<If>(block.Statements.First());
        Assert.NotNull(ifStmt.ElseBranch);

        var elseBlock = Assert.IsType<Block>(ifStmt.ElseBranch!.Branch);
        var ret = Assert.IsType<Return>(elseBlock.Statements.First());
        var binary = Assert.IsType<BinaryOperator>(ret.Expression!);
        var invocation = Assert.IsType<Invocation>(binary.Right);
        var ident = Assert.IsType<Identifier>(invocation.Expression);
        var symbol = model.GetSymbol(ident);
        Assert.NotNull(symbol);
        Assert.Equal("factorial", symbol.Name);
    }

    [Fact]
    public void Resolves_MatchPatternBinding_InBody()
    {
        var model = Utility.AssertNoErrors(Utility.GetSemanticModel("match 1 { x -> x }"));
        var match = Assert.IsType<MatchExpression>(Assert.IsType<ExpressionStatement>(model.Tree.Statements.Single()).Expression);
        var arm = Assert.Single(match.Arms);
        var pattern = Assert.IsType<IdentifierPattern>(arm.Pattern);
        var declaration = model.GetDeclarationSymbol(pattern);
        Assert.NotNull(declaration);
        Assert.Equal("x", declaration.Name);
        Assert.Equal(SymbolKind.Variable, declaration.Kind);

        var body = Assert.IsType<Identifier>(arm.Body);
        Assert.Equal(declaration, model.GetSymbol(body));
    }

    [Fact]
    public void Resolves_MatchPatternBinding_InGuard()
    {
        var model = Utility.AssertNoErrors(Utility.GetSemanticModel("match 1 { n when n > 0 -> n }"));
        var match = Assert.IsType<MatchExpression>(Assert.IsType<ExpressionStatement>(model.Tree.Statements.Single()).Expression);
        var arm = Assert.Single(match.Arms);
        var pattern = Assert.IsType<IdentifierPattern>(arm.Pattern);
        var declaration = model.GetDeclarationSymbol(pattern);
        Assert.NotNull(declaration);

        var guard = Assert.IsType<BinaryOperator>(arm.Guard);
        var guardIdent = Assert.IsType<Identifier>(guard.Left);
        Assert.Equal(declaration, model.GetSymbol(guardIdent));
    }

    [Fact]
    public void Resolves_MatchTypedPattern_TypeName()
    {
        var model = Utility.AssertNoErrors(Utility.GetSemanticModel("interface Foo {}; match 1 { s when Foo -> s }"));
        var match = Assert.IsType<MatchExpression>(Assert.IsType<ExpressionStatement>(model.Tree.Statements[1]).Expression);
        var arm = Assert.Single(match.Arms);
        var typed = Assert.IsType<TypedPattern>(arm.Pattern);
        var typeName = Assert.IsType<TypeName>(typed.Type);
        var symbol = model.GetSymbol(typeName);
        Assert.NotNull(symbol);
        Assert.Equal("Foo", symbol.Name);
    }

    [Fact]
    public void Resolves_MatchTypePattern_ObjectFieldBinding()
    {
        var model = Utility.AssertNoErrors(Utility.GetSemanticModel("interface Foo { field: number }; match 1 { Foo { field } -> field }"));
        var match = Assert.IsType<MatchExpression>(Assert.IsType<ExpressionStatement>(model.Tree.Statements[1]).Expression);
        var arm = Assert.Single(match.Arms);
        var typePattern = Assert.IsType<TypePattern>(arm.Pattern);
        var field = Assert.Single(typePattern.ObjectPattern!.Fields);
        var identifierPattern = Assert.IsType<IdentifierPattern>(field.Pattern);
        var declaration = model.GetDeclarationSymbol(identifierPattern);
        Assert.NotNull(declaration);
        Assert.Equal("field", declaration.Name);
        Assert.Equal(declaration, model.GetSymbol(Assert.IsType<Identifier>(arm.Body)));
    }

    [Fact]
    public void Resolves_MatchArrayRestBinding()
    {
        var model = Utility.AssertNoErrors(Utility.GetSemanticModel("match 1 { [head, ..rest] -> rest }"));
        var match = Assert.IsType<MatchExpression>(Assert.IsType<ExpressionStatement>(model.Tree.Statements.Single()).Expression);
        var arm = Assert.Single(match.Arms);
        var array = Assert.IsType<ArrayPattern>(arm.Pattern);
        Assert.NotNull(array.Rest);
        var restPattern = Assert.IsType<IdentifierPattern>(array.Rest.Pattern);
        var declaration = model.GetDeclarationSymbol(restPattern);
        Assert.NotNull(declaration);
        Assert.Equal("rest", declaration.Name);
        Assert.Equal(declaration, model.GetSymbol(Assert.IsType<Identifier>(arm.Body)));
    }
    #endregion

    #region Allows
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
    #endregion Allows

    #region Declares
    [Theory]
    [InlineData("Range")]
    [InlineData("Record<string, bool>")]
    [InlineData("MutRecord<string, bool>")]
    [InlineData("Event<number, string>")]
    [InlineData("ConsumerEvent<number, string>")]
    public void Declares_IntrinsicType_Symbols(string name) => Utility.AssertNoErrors(Utility.GetSemanticModel($"mut x: {name}"));

    [Fact]
    public void Declares_EventPropertySymbol()
    {
        var model = Utility.AssertNoErrors(Utility.GetSemanticModel("interface Foo { event abc; }"));
        var interfaceDeclaration = Assert.IsType<InterfaceDeclaration>(model.Tree.Statements.Single());
        Assert.NotNull(interfaceDeclaration.Body);

        var eventDeclaration = Assert.IsType<EventDeclaration>(Assert.Single(interfaceDeclaration.Body.Members));
        var symbol = model.GetDeclarationSymbol(interfaceDeclaration, SymbolKind.Interface);
        Assert.NotNull(symbol);

        var interfaceSymbol = Assert.IsType<InterfaceSymbol>(symbol);
        Assert.Equal("Foo", interfaceSymbol.Name);
        Assert.Single(interfaceSymbol.Properties);

        var property = interfaceSymbol.GetPropertyAtPath(["abc"]);
        Assert.NotNull(property);
        Assert.Equal(SymbolKind.Event, property.Kind);
        Assert.Equal(eventDeclaration, property.Declaration);
        Assert.False(property.IsIntrinsic);
        Assert.False(property.IsMutable);
    }

    [Fact]
    public void Declares_EventSymbol()
    {
        var model = Utility.AssertNoErrors(Utility.GetSemanticModel("event abc;"));
        var eventDeclaration = Assert.IsType<EventDeclaration>(model.Tree.Statements.Single());

        var symbol = model.GetDeclarationSymbol(eventDeclaration, SymbolKind.Event);
        Assert.NotNull(symbol);
        Assert.Equal("abc", symbol.Name);
        Assert.Equal(SymbolKind.Event, symbol.Kind);
        Assert.Equal(eventDeclaration, symbol.Declaration);
        Assert.False(symbol.IsAmbient);
        Assert.False(symbol.IsIntrinsic);
        Assert.False(symbol.IsMutable);
    }

    [Fact]
    public void Declares_EventPropertySymbol_WithAttribute()
    {
        var model = Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                interface Foo {
                    [luau_name("OnConsume")]
                    event abc(x: number);
                }
                """
            )
        );

        var interfaceDeclaration = Assert.IsType<InterfaceDeclaration>(model.Tree.Statements.Last());
        Assert.NotNull(interfaceDeclaration.Body);

        var symbol = model.GetDeclarationSymbol(interfaceDeclaration, SymbolKind.Interface);
        var interfaceSymbol = Assert.IsType<InterfaceSymbol>(symbol);

        var property = Assert.Single(interfaceSymbol.Properties);
        Assert.Equal(SymbolKind.Event, property.Kind);
        var attribute = Assert.Single(property.Attributes);
        Assert.Equal("luau_name", attribute.Name);
    }

    [Fact]
    public void Declares_EventSymbol_WithAttribute_HasNoEffect()
    {
        var model = Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                [luau_name("OnConsume")]
                event abc(x: number);
                """
            )
        );

        var eventDeclaration = Assert.IsType<EventDeclaration>(model.Tree.Statements.Last());
        var symbol = model.GetDeclarationSymbol(eventDeclaration, SymbolKind.Event);
        Assert.NotNull(symbol);
        Assert.Equal(SymbolKind.Event, symbol.Kind);
        Assert.IsNotType<PropertySymbol>(symbol);
    }

    [Fact]
    public void Declares_TraitSymbol()
    {
        var model = Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                trait Iterator {
                    fn next(): number
                }
                """
            )
        );

        var trait = Assert.IsType<TraitDeclaration>(model.Tree.Statements.Single());

        var symbol = model.GetDeclarationSymbol(trait, SymbolKind.Trait);
        Assert.NotNull(symbol);
        Assert.Equal("Iterator", symbol.Name);
        Assert.Equal(SymbolKind.Trait, symbol.Kind);
        Assert.Equal(trait, symbol.Declaration);
        Assert.False(symbol.IsIntrinsic);
        Assert.False(symbol.IsMutable);
    }

    [Fact]
    public void Declares_VariableSymbol()
    {
        var model = Utility.GetSemanticModel("let x = 1; x;");
        Assert.Equal(2, model.Tree.Statements.Count);

        var firstStatement = model.Tree.Statements.First();
        var secondStatement = model.Tree.Statements.Last();
        var variableDeclaration = Assert.IsType<VariableDeclaration>(firstStatement);
        var expressionStatement = Assert.IsType<ExpressionStatement>(secondStatement);
        var identifier = Assert.IsType<Identifier>(expressionStatement.Expression);
        var symbol = model.GetSymbol(identifier);
        Assert.NotNull(symbol);
        Assert.Equal("x", symbol.Name);
        Assert.Equal(SymbolKind.Variable, symbol.Kind);
        Assert.Equal(variableDeclaration, symbol.Declaration);

        var declaringSymbol = model.GetDeclaringSymbol(identifier);
        var declarationSymbol = model.GetDeclarationSymbol(variableDeclaration);
        Assert.NotNull(declaringSymbol);
        Assert.NotNull(declarationSymbol);
        Assert.Equal(declaringSymbol, declarationSymbol);
        Assert.Equal("x", declarationSymbol.Name);
        Assert.Equal(SymbolKind.Variable, declarationSymbol.Kind);
        Assert.Equal(variableDeclaration, declarationSymbol.Declaration);
    }

    [Fact]
    public void Declares_Enum_VariableSymbol()
    {
        var model = Utility.GetSemanticModel("enum Colors { Red, Green, Blue }; Colors.Red");
        Utility.AssertNoErrors(model);

        var declaration = model.Tree.Statements.First();
        var expressionStatement = Assert.IsType<ExpressionStatement>(model.Tree.Statements.Last());
        var qualifiedName = Assert.IsType<QualifiedName>(expressionStatement.Expression);
        var symbol = model.GetSymbol(qualifiedName.Identifier);
        Assert.NotNull(symbol);
        Assert.Equal("Colors", symbol.Name);
        Assert.Equal(SymbolKind.Variable, symbol.Kind);
        Assert.Equal(declaration, symbol.Declaration);
    }

    [Fact]
    public void Declares_EnumTypeSymbol()
    {
        var model = Utility.GetSemanticModel("enum Colors { Red, Green, Blue };");
        var enumDeclaration = Assert.IsType<EnumDeclaration>(model.Tree.Statements.First());
        var symbol = model.GetDeclarationSymbol(enumDeclaration, SymbolKind.EnumType);
        Assert.NotNull(symbol);
        Assert.Equal("Colors", symbol.Name);
        Assert.Equal(SymbolKind.EnumType, symbol.Kind);
        Assert.Equal(enumDeclaration, symbol.Declaration);
    }

    [Fact]
    public void Declares_ParameterSymbol()
    {
        var model = Utility.GetSemanticModel("fn test(x: number) { }");
        var functionDeclaration = Assert.IsType<FunctionDeclaration>(model.Tree.Statements.First());
        Assert.NotNull(functionDeclaration.Parameters);

        var parameter = functionDeclaration.Parameters!.ParameterList.First();
        var symbol = model.GetDeclarationSymbol(parameter);
        Assert.NotNull(symbol);
        Assert.Equal("x", symbol.Name);
        Assert.Equal(SymbolKind.Parameter, symbol.Kind);
        Assert.Equal(parameter, symbol.Declaration);
    }

    [Fact]
    public void Declares_FunctionSymbol()
    {
        var model = Utility.GetSemanticModel("fn test() { }");
        var functionDeclaration = Assert.IsType<FunctionDeclaration>(model.Tree.Statements.First());
        var symbol = model.GetDeclarationSymbol(functionDeclaration);
        Assert.NotNull(symbol);
        Assert.Equal("test", symbol.Name);
        Assert.Equal(SymbolKind.Function, symbol.Kind);
        Assert.Equal(functionDeclaration, symbol.Declaration);
    }

    [Theory]
    [InlineData("sealed interface Foo { foo: number }", true)]
    [InlineData("interface Foo { foo: number }")]
    [InlineData("interface Nutz; interface Ballz; sealed interface Foo: Nutz, Ballz { foo: number }", true, false, null, 2)]
    [InlineData("declare sealed interface Foo { foo: number }", true, true, typeof(Declare))]
    [InlineData("declare interface Foo { foo: number }", false, true, typeof(Declare))]
    public void Declares_InterfaceSymbol(string source, bool isSealed = false, bool isAmbient = false, Type? declarationType = null, int constraintCount = 0)
    {
        var model = Utility.AssertNoErrors(Utility.GetSemanticModel(source));
        var statement = model.Tree.Statements.Last();
        Assert.IsType(declarationType ?? typeof(InterfaceDeclaration), statement);

        if (declarationType == typeof(Declare))
            statement = ((Declare)statement).Signature;

        var symbol = model.GetDeclarationSymbol(statement, SymbolKind.Interface);
        Assert.NotNull(symbol);

        var interfaceSymbol = Assert.IsType<InterfaceSymbol>(symbol);
        Assert.Equal("Foo", interfaceSymbol.Name);
        Assert.Equal(SymbolKind.Interface, interfaceSymbol.Kind);
        Assert.Equal(statement, interfaceSymbol.Declaration);
        Assert.Equal(isSealed, interfaceSymbol.IsSealed);
        Assert.Equal(isAmbient, interfaceSymbol.IsAmbient);
        Assert.Empty(interfaceSymbol.Implementations);
        Assert.Empty(interfaceSymbol.Implements);

        var property = Assert.Single(interfaceSymbol.Properties);
        Assert.Equal("foo", property.Name);
        Assert.False(property.HasIntrinsicAttribute("hello"));
        Assert.True(property.IsValueSymbol);
        Assert.False(property.IsTypeSymbol);
        Assert.False(property.IsMutable);
        Assert.Null(property.PointsTo);
        Assert.Empty(property.Attributes);

        if (constraintCount > 0)
        {
            Assert.NotNull(interfaceSymbol.Constraints);
            Assert.Equal(constraintCount, interfaceSymbol.Constraints.Count);
        }

        Assert.False(interfaceSymbol.IsIntrinsic);
        Assert.False(interfaceSymbol.IsMutable);
    }

    [Fact]
    public void Declares_TypeAliasSymbol()
    {
        var model = Utility.GetSemanticModel("type MyNumber = number");
        var typeAlias = Assert.IsType<TypeAlias>(model.Tree.Statements.First());
        var symbol = model.GetDeclarationSymbol(typeAlias);
        Assert.NotNull(symbol);
        Assert.Equal("MyNumber", symbol.Name);
        Assert.Equal(SymbolKind.Type, symbol.Kind);
        Assert.Equal(typeAlias, symbol.Declaration);
    }

    [Fact]
    public void Declares_TypeParameterSymbol()
    {
        var model = Utility.GetSemanticModel("type Container<T> = T");
        var typeAlias = Assert.IsType<TypeAlias>(model.Tree.Statements.First());
        Assert.NotNull(typeAlias.TypeParameters);

        var typeParameter = typeAlias.TypeParameters.ParameterList.First();
        var symbol = model.GetDeclarationSymbol(typeParameter);
        Assert.NotNull(symbol);
        Assert.Equal("T", symbol.Name);
        Assert.Equal(SymbolKind.Type, symbol.Kind);
        Assert.Equal(typeParameter, symbol.Declaration);
    }

    [Fact]
    public void Declares_DeclareVariableSymbol()
    {
        var model = Utility.GetSemanticModel("declare let x: number");
        Utility.AssertNoErrors(model);

        var declare = Assert.IsType<Declare>(model.Tree.Statements.Single());
        var sig = Assert.IsType<DeclareVariableSignature>(declare.Signature);
        var symbol = model.GetDeclarationSymbol(sig);
        Assert.NotNull(symbol);
        Assert.Equal("x", symbol.Name);
        Assert.Equal(SymbolKind.Variable, symbol.Kind);
        Assert.False(symbol.IsMutable);
        Assert.Equal(sig, symbol.Declaration);
    }

    [Fact]
    public void Declares_DeclareVariableSymbol_Mutable()
    {
        var model = Utility.GetSemanticModel("declare mut y: string");
        var declare = Assert.IsType<Declare>(model.Tree.Statements.Single());
        var sig = Assert.IsType<DeclareVariableSignature>(declare.Signature);
        var symbol = model.GetDeclarationSymbol(sig);
        Assert.NotNull(symbol);
        Assert.Equal("y", symbol.Name);
        Assert.True(symbol.IsMutable);
    }

    [Fact]
    public void Declares_DeclareFunctionSymbol()
    {
        var model = Utility.GetSemanticModel("declare fn add(a: number, b: number): number");
        Utility.AssertNoErrors(model);

        var declare = Assert.IsType<Declare>(model.Tree.Statements.Single());
        var sig = Assert.IsType<DeclareFunctionSignature>(declare.Signature);
        var symbol = model.GetDeclarationSymbol(sig);
        Assert.NotNull(symbol);
        Assert.Equal("add", symbol.Name);
        Assert.Equal(SymbolKind.Function, symbol.Kind);
        Assert.Equal(sig, symbol.Declaration);
    }

    [Fact]
    public void Declares_DeclareFunction_InsideBlock()
    {
        var model = Utility.GetSemanticModel("{ declare fn helper(): string; helper; }");
        Utility.AssertNoErrors(model);
    }

    [Fact]
    public void Declares_DeclareEventSymbol()
    {
        var model = Utility.AssertNoErrors(Utility.GetSemanticModel("declare event consumer(param: string);"));

        var declare = Assert.IsType<Declare>(model.Tree.Statements.Single());
        var sig = Assert.IsType<EventDeclaration>(declare.Signature);
        var symbol = model.GetDeclarationSymbol(sig, SymbolKind.Event);
        Assert.NotNull(symbol);
        Assert.Equal("consumer", symbol.Name);
        Assert.Equal(SymbolKind.Event, symbol.Kind);
        Assert.Equal(sig, symbol.Declaration);
        Assert.True(symbol.IsAmbient);
        Assert.False(symbol.IsMutable);
    }
    #endregion Declares

    #region ReservedLuauKeywords
    [Theory]
    [InlineData("let repeat = 1;", "repeat")]
    [InlineData("fn until() {}", "until")]
    [InlineData("interface local;", "local")]
    public void ThrowsFor_ReservedLuauKeywordDeclaration(string source, string keyword)
    {
        var diagnostics = Utility.GetSemanticModel(source).Diagnostics;
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.ReservedLuauKeyword,
            $"'{keyword}' is a reserved Luau keyword and cannot be used as a declaration name."
        );
    }

    [Fact]
    public void ThrowsFor_ReservedLuauKeywordParameter()
    {
        var diagnostics = Utility.GetSemanticModel("fn foo(local: number): void {}").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.ReservedLuauKeyword, "'local' is a reserved Luau keyword and cannot be used as a declaration name.");
    }

    [Theory]
    [InlineData("declare let local: number;", "local")]
    [InlineData("declare fn end(): void;", "end")]
    [InlineData("declare fn f(function: number): void;", "function")]
    [InlineData("declare interface repeat;", "repeat")]
    public void ThrowsFor_ReservedLuauKeywordName_InAmbientDeclaration(string source, string keyword)
    {
        var diagnostics = Utility.GetSemanticModel(source).Diagnostics;
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.ReservedLuauKeyword,
            $"'{keyword}' is a reserved Luau keyword and cannot be used as a declaration name."
        );
    }

    [Fact]
    public void Allows_SelfAsDeclarationName()
    {
        var model = Utility.GetSemanticModel("fn foo(self: number): void {}");
        Assert.Null(model.Diagnostics.Find(d => d.Code == InternalCodes.ReservedLuauKeyword));
    }
    #endregion ReservedLuauKeywords

    #region Destructuring
    [Fact]
    public void Resolves_ArrayDestructuring_DeclaresAllBindings() =>
        Utility.AssertNoErrors(Utility.GetSemanticModel("let array = [1, 2]; let [first, second] = array; print(first); print(second);").Diagnostics);

    [Fact]
    public void Resolves_ObjectDestructuring_DeclaresAllBindings() =>
        Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                "interface User { name: string } let user = new User { name: \"Ada\" }; let { name } = user; print(name);"
            ).Diagnostics
        );

    [Fact]
    public void Resolves_ObjectDestructuring_WithAlias_DeclaresBindingUnderAliasName()
    {
        var diagnostics = Utility.GetSemanticModel(
            "interface User { age: number } let user = new User { age: 30 }; let { age: userAge } = user; print(userAge);"
        ).Diagnostics;

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void ThrowsFor_ObjectDestructuring_AliasName_IsNotVisibleUnderOriginalName()
    {
        var diagnostics = Utility.GetSemanticModel(
            "interface User { age: number } let user = new User { age: 30 }; let { age: userAge } = user; print(age);"
        ).Diagnostics;

        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find name 'age'.");
    }

    [Fact]
    public void ThrowsFor_Destructuring_MissingInitializer()
    {
        var diagnostics = Utility.GetSemanticModel("let [first, second];").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.MustHaveInitializer, "Destructuring declarations must be initialized.");
    }

    [Fact]
    public void ThrowsFor_Destructuring_WithMutKeyword()
    {
        var diagnostics = Utility.GetSemanticModel("let array = [1, 2]; mut [first, second] = array;").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidDestructureTarget, "Destructuring declarations must use 'let', not 'mut'.");
    }

    [Fact]
    public void ThrowsFor_Destructuring_DuplicateBindingName()
    {
        var diagnostics = Utility.GetSemanticModel("let array = [1, 2]; let [x, x] = array;").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.DuplicateName, "Variable 'x' is already declared in this scope.");
    }

    [Fact]
    public void Resolves_TupleDestructuring_DeclaresAllBindings() =>
        Utility.AssertNoErrors(
            Utility.GetSemanticModel("let t: (string, number) = (\"abc\", 420); let (one, two) = t; print(one); print(two);").Diagnostics
        );

    [Fact]
    public void Resolves_TupleConstraint_ResolvesTupleName() =>
        Utility.AssertNoErrors(Utility.GetSemanticModel("declare fn something<T: Tuple>(..args: T): void;").Diagnostics);
    #endregion Destructuring

    #region Decorators
    [Fact]
    public void Resolves_DecoratorAttribute_AsOrdinaryValueReference() =>
        Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                fn log(f: fn(): void, name: string): void { f(); }
                [log]
                fn do_something() { }
                """
            )
        );

    [Fact]
    public void ThrowsFor_DecoratorAttribute_UnknownName()
    {
        var diagnostics = Utility.GetSemanticModel("[unknown_decorator]\nfn do_something() { }").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find name 'unknown_decorator'.");
    }

    [Fact]
    public void Resolves_InterfaceDecoratorAttribute_AsOrdinaryValueReference() =>
        Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                fn validate(f: fn(): Foo, name: string): Foo { return f(); }
                [validate]
                interface Foo { x: number }
                """
            )
        );

    [Fact]
    public void ThrowsFor_InterfaceDecoratorAttribute_UnknownName()
    {
        var diagnostics = Utility.GetSemanticModel("[unknown_decorator]\ninterface Foo { x: number }").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find name 'unknown_decorator'.");
    }
    #endregion Decorators

    [Fact]
    public void DeclareFunctionSignature_TryGetIntrinsicAttribute_FindsAttributeByName()
    {
        var semanticModel = Utility.GetSemanticModel("[luau_name(\"Bar\")]\ndeclare fn foo(): void;");
        Utility.AssertNoErrors(semanticModel.Diagnostics);

        var signature = semanticModel.Tree.GetDescendants<DeclareFunctionSignature>().Single();
        Assert.True(signature.TryGetIntrinsicAttribute(semanticModel, "luau_name", out var attribute));
        Assert.Equal("luau_name", attribute.Name);
        Assert.False(signature.TryGetIntrinsicAttribute(semanticModel, "luau_method", out _));
    }

    #region CFrameMembers
    [Fact]
    public void Resolves_CFrameDecomposition()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            let cf = CFrame.create(1, 2, 3);
            let (x, y, z, r00, r01, r02, r10, r11, r12, r20, r21, r22) = cf.get_components();
            let (rx, ry, rz) = cf.to_orientation();
            let (axis, angle) = cf.to_axis_angle();
            print(x, r22, rx, axis, angle);
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Resolves_CFrameEulerAngleVariants()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            let cf = CFrame.create(1, 2, 3);
            let (ax, ay, az) = cf.to_euler_angles_xyz();
            let (bx, by, bz) = cf.to_euler_angles_yxz();
            print(ax, ay, az, bx, by, bz);
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Resolves_CFrameSpaceHelpers()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            let cf = CFrame.create(1, 2, 3);
            print(cf.inverse());
            print(cf.lerp(CFrame.identity, 0.5));
            print(cf.to_world_space(CFrame.identity));
            print(cf.point_to_object_space(Vector3.zero));
            print(cf.vector_to_world_space(Vector3.one));
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Resolves_CFrameDecomposition_ToMethodCalls()
    {
        var luau = Utility.GetLuauAST("let cf = CFrame.create(1, 2, 3); let (rx, ry, rz) = cf.to_orientation();", true).Render();

        Assert.Contains("CFrame.new(1, 2, 3)", luau);
        Assert.Contains("cf:ToOrientation()", luau);
    }
    #endregion CFrameMembers
}