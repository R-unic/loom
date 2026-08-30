using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving.Symbols;

namespace Loom.Testing.Resolving;

public partial class ResolverTest
{
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
        Utility.AssertNoErrors(Utility.GetSemanticModel("let v: Vector3 = Vector3::zero;\nprint(v.x);").Diagnostics);

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

    /// <summary>
    ///     Bug #231-5: VisitImplement injected a bare-name variable symbol for every one of the interface's
    ///     FullProperties, including statics - which have no self-relative meaning inside a trait method
    ///     body - so a body could reference a static member by its bare name and the generator would emit
    ///     'self.&lt;name&gt;', always nil at runtime. Filtering statics out of the injection means the bare
    ///     name is simply undeclared, same as any other name the body never brought into scope.
    /// </summary>
    [Fact]
    public void ThrowsFor_TraitMethodBody_ReferencingStaticMemberByBareName()
    {
        var diagnostics = Utility.GetSemanticModel(
                """
                interface Vector2 {
                    x: number
                    static origin_label: string
                }

                static Vector2 { origin_label = "origin"; }

                trait Describable {
                    fn describe(): string
                }

                implement Describable for Vector2 {
                    fn describe(): string {
                        return origin_label;
                    }
                }
                """
            )
            .Diagnostics;

        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find name 'origin_label'.");
    }

    [Fact]
    public void ThrowsFor_ImplementOutsideModuleScope()
    {
        var diagnostics = Utility.GetSemanticModel(
            """
            trait Foo { fn a(): void }
            interface Bar { }

            fn f() {
                implement Foo for Bar {
                    fn a() { }
                }
            }
            """
        ).Diagnostics;

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.ImplementOutsideModuleScope,
            "Traits can only be implemented at the top level of a module.",
            "move the 'implement' block out of the enclosing block"
        );
    }

    [Fact]
    public void Resolves_StaticAndInstanceMembers_IntoCorrectlyFlaggedPropertySymbols()
    {
        var model = Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                interface Vector2 {
                    x: number
                    static zero: Vector2
                }
                """
            )
        );

        var iface = Assert.IsType<InterfaceDeclaration>(model.Tree.Statements[0]);
        var symbol = Assert.IsType<InterfaceSymbol>(model.GetDeclarationSymbol(iface, SymbolKind.Interface));

        var instanceProperty = Assert.Single(symbol.Properties, p => p.Name == "x");
        var staticProperty = Assert.Single(symbol.Properties, p => p.Name == "zero");

        Assert.False(instanceProperty.IsStatic);
        Assert.True(staticProperty.IsStatic);
    }

    [Fact]
    public void SynthesizesValueSymbol_ForInterfaceWithStaticMembers()
    {
        var model = Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                interface Vector2 {
                    static zero: Vector2
                }

                Vector2
                """
            )
        );

        var statement = Assert.IsType<ExpressionStatement>(model.Tree.Statements[1]);
        var reference = Assert.IsType<Identifier>(statement.Expression);
        var symbol = model.GetSymbol(reference);

        Assert.NotNull(symbol);
        Assert.True(symbol.IsValueSymbol);
    }

    [Fact]
    public void SynthesizesValueSymbol_ForDeclaredInterfaceWithStaticMembers()
    {
        var model = Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                declare interface Vector2 {
                    static zero: Vector2
                }

                Vector2
                """
            )
        );

        var statement = Assert.IsType<ExpressionStatement>(model.Tree.Statements[1]);
        var reference = Assert.IsType<Identifier>(statement.Expression);
        var symbol = model.GetSymbol(reference);

        Assert.NotNull(symbol);
        Assert.True(symbol.IsValueSymbol);
    }

    [Fact]
    public void ThrowsFor_SynthesizedValueSymbol_CollidingWithExplicitLet()
    {
        var diagnostics = Utility.GetSemanticModel(
            """
            declare let Vector2: number;
            declare interface Vector2 {
                static zero: Vector2
            }
            """
        ).Diagnostics;

        Utility.AssertDiagnostic(diagnostics, InternalCodes.DuplicateName, "Variable 'Vector2' is already declared in this scope.");
    }

    [Fact]
    public void ThrowsFor_StaticBlockOutsideModuleScope()
    {
        var diagnostics = Utility.GetSemanticModel(
            """
            interface Vector2 { static zero: Vector2 }

            fn f() {
                static Vector2 { zero = new Vector2 { }; }
            }
            """
        ).Diagnostics;

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.StaticBlockOutsideModuleScope,
            "Static blocks can only be declared at the top level of a module.",
            "move the 'static' block out of the enclosing block"
        );
    }

    [Fact]
    public void ThrowsFor_DuplicateStaticBlock()
    {
        var diagnostics = Utility.GetSemanticModel(
            """
            interface Vector2 { static zero: Vector2 }

            static Vector2 { zero = new Vector2 { }; }
            static Vector2 { zero = new Vector2 { }; }
            """
        ).Diagnostics;

        Utility.AssertDiagnostic(diagnostics, InternalCodes.DuplicateStaticBlock, "Interface 'Vector2' already has a 'static' block.");
    }

    [Fact]
    public void ThrowsFor_StaticBlockOnAmbientInterface()
    {
        var diagnostics = Utility.GetSemanticModel(
            """
            declare interface Vector2 { static zero: Vector2 }

            static Vector2 { zero = new Vector2 { }; }
            """
        ).Diagnostics;

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.StaticBlockOnAmbientInterface,
            "Interface 'Vector2' is ambient, so its static members need no companion block.",
            "remove the 'static' block - an ambient interface's static signatures are trusted as-is"
        );
    }

    [Fact]
    public void Allows_StaticBlock_OnNonAmbientInterfaceWithStatics() =>
        Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                interface Vector2 { static zero: Vector2 }

                static Vector2 { zero = new Vector2 { }; }
                """
            )
        );

    [Fact]
    public void Allows_DeclareStaticBlock_OnTypeAlias() =>
        Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                type Outcome<T, E> = T | E;

                declare static Outcome {
                    ok: fn<T, E>(value: T): Outcome<T, E>;
                    err: fn<T, E>(error: E): Outcome<T, E>;
                }
                """
            )
        );

    [Fact]
    public void SynthesizesValueSymbol_ForTypeAliasWithDeclareStaticBlock()
    {
        var model = Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                type Outcome<T, E> = T | E;

                declare static Outcome {
                    ok: fn<T, E>(value: T): Outcome<T, E>;
                }

                Outcome
                """
            )
        );

        var statement = Assert.IsType<ExpressionStatement>(model.Tree.Statements[2]);
        var reference = Assert.IsType<Identifier>(statement.Expression);
        var symbol = model.GetSymbol(reference);

        Assert.NotNull(symbol);
        Assert.True(symbol.IsValueSymbol);
    }

    [Fact]
    public void ThrowsFor_DeclareStaticBlock_OutsideModuleScope()
    {
        var diagnostics = Utility.GetSemanticModel(
            """
            type Outcome<T, E> = T | E;

            fn f() {
                declare static Outcome { ok: fn<T, E>(value: T): Outcome<T, E>; }
            }
            """
        ).Diagnostics;

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.StaticBlockOutsideModuleScope,
            "'declare static' blocks can only be declared at the top level of a module.",
            "move the 'declare static' block out of the enclosing block"
        );
    }

    [Fact]
    public void ThrowsFor_DeclareStaticBlock_TargetingInterface()
    {
        var diagnostics = Utility.GetSemanticModel(
            """
            interface Vector2 { x: number }

            declare static Vector2 { zero: fn(): Vector2; }
            """
        ).Diagnostics;

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.DeclareStaticBlockTargetsInterface,
            "'declare static' targets an ambient type alias - interface 'Vector2' already declares its ambient statics inline.",
            "add 'static' members directly inside 'declare interface Vector2 { ... }' instead"
        );
    }

    [Fact]
    public void ThrowsFor_DeclareStaticBlock_TargetingNonAlias()
    {
        var diagnostics = Utility.GetSemanticModel(
            """
            enum Color { Red, Green }

            declare static Color { of: fn(name: string): Color; }
            """
        ).Diagnostics;

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.DeclareStaticBlockTargetsNonAlias,
            "'declare static' may only target a type alias, but 'Color' is not one."
        );
    }

    [Fact]
    public void ThrowsFor_DeclareStaticBlock_UnknownTarget()
    {
        var diagnostics = Utility.GetSemanticModel("declare static Nope { of: fn(): number; }").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindSymbol, "Cannot find type symbol 'Nope'.");
    }

    [Fact]
    public void ThrowsFor_DuplicateDeclareStaticBlock()
    {
        var diagnostics = Utility.GetSemanticModel(
            """
            type Outcome<T, E> = T | E;

            declare static Outcome { ok: fn<T, E>(value: T): Outcome<T, E>; }
            declare static Outcome { err: fn<T, E>(error: E): Outcome<T, E>; }
            """
        ).Diagnostics;

        Utility.AssertDiagnostic(diagnostics, InternalCodes.DuplicateDeclareStaticBlock, "Type alias 'Outcome' already has a 'declare static' block.");
    }

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

    [Fact]
    public void ThrowsFor_InternalOutsideModuleScope()
    {
        var diagnostics = Utility.GetSemanticModel("fn f() { internal let x = 1; }").Diagnostics;
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.ExportOutsideModuleScope,
            "Declarations can only be exported at the top level of a module.",
            "move the 'internal' declaration out of the enclosing block"
        );
    }

    [Fact]
    public void ThrowsFor_InternalMutableVariable()
    {
        var diagnostics = Utility.GetSemanticModel("internal mut x = 1;").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotExportMutable, "Mutable variables cannot be marked internal.", "use 'let' instead of 'mut'");
    }
}
