using Loom.Core.Diagnostics;
using Loom.Core.TypeChecking;
using Loom.Core.TypeChecking.Types;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.Testing;

[Collection("Assembly")]
public class TypeCheckerTest
{
    [Fact]
    public void ThrowsFor_MacroReference_InVariableDeclaration()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let x = Result.ok;");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidMacroReference,
            "Invocation macro 'ok' cannot be used as a value. Call it directly (e.g. ok(...)) or pass it as a function argument."
        );
    }

    [Fact]
    public void ThrowsFor_MacroReference_InArrayLiteral()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let x = [Result.ok];");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidMacroReference,
            "Invocation macro 'ok' cannot be used as a value. Call it directly (e.g. ok(...)) or pass it as a function argument."
        );
    }

    [Fact]
    public void Allows_UseAfterIf_WhenElseBranchTerminates()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            fn test(c: bool): number {
                mut x: number;
                if c {
                    x = 1;
                } else {
                    return 0;
                }
                return x + 1;
            }
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Allows_MacroReference_AsFunctionArgument()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            declare fn consume<T, E>(callback: fn(value: T): Result<T, E>): void;
            consume(Result.ok);
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    #region ThrowsFor
    [Fact]
    public void ThrowsFor_Variable_DeclaredType_Mismatch()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let x: number = 'hello'");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '\"hello\"' is not assignable to type 'number'.");
    }

    [Fact]
    public void ThrowsFor_Assignment_DeclaredType_Mismatch()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("mut x = 69; x = false");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type 'false' is not assignable to type 'number'.");
    }

    [Fact]
    public void ThrowsFor_GenericTypeMismatch()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("type Id<T> = T; let x: Id<number> = 'hello'");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '\"hello\"' is not assignable to type 'number'.");
    }

    [Fact]
    public void ThrowsFor_UndefinedIdentifier()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("x");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindSymbol, "Cannot find symbol for declaration of variable 'x'.");
    }

    [Fact]
    public void ThrowsFor_UndefinedType()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let x: A = 1");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindSymbol, "Cannot find symbol for declaration of type 'A'.");
    }

    [Fact]
    public void ThrowsFor_NonGeneric()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("type A = number; let x: A<number> = 1");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.NotGeneric, "Type 'A' is not generic and cannot receive type arguments.");
    }

    [Fact]
    public void ThrowsFor_IncorrectGenericArity()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("type A<T> = T; let x: A<number, bool> = 1");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.GenericArity, "Type 'A<T>' expects 1 type argument, but 2 were provided.");
    }

    [Fact]
    public void ThrowsFor_MismatchedDefaultType()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("type Id<T: number = \"abc\"> = T");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '\"abc\"' is not assignable to type 'number'.");
    }

    [Fact]
    public void ThrowsFor_QualifiedName_InvalidTarget()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("fn foo -> 42; foo.abc");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidAccess, "Cannot access property 'abc' on type 'fn(): 42'.");
    }

    [Fact]
    public void ThrowsFor_PropertyAccess_InvalidTarget()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("(69).abc");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidAccess, "Cannot access property 'abc' on type '69'.");
    }

    [Fact]
    public void ThrowsFor_ElementAccess_InvalidTarget()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("fn foo -> 42; foo[0]");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidAccess, "Cannot index value of type 'fn(): 42'.");
    }

    [Fact]
    public void ThrowsFor_ElementAccess_InvalidIndex()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let arr: number[] = [1, 2, 3]; arr[true]");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidAccess,
            "Expression of type 'true' cannot be used to index type 'number[]'. Index is not of type 'number'."
        );
    }

    [Fact]
    public void ThrowsFor_ElementAccess_ImmutableAssignment()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let arr: number[] = [1, 2, 3]; arr[0] = 69;");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.AssignToImmutable, "Cannot assign to immutable index 'number'.");
    }

    [Theory]
    [InlineData("type T = T")]
    [InlineData("type T = T | number")]
    [InlineData("type T = T & number")]
    [InlineData("type T = none | string & bool & T | number")]
    [InlineData("type X<T = T> = T")]
    [InlineData("type X<T: number = T> = T")]
    [InlineData("type X<T = T | number> = T")]
    [InlineData("type X<T = T & number> = T")]
    [InlineData("type X<T: string = T & number> = T")]
    [InlineData("type X<T = none | string & bool & T | number> = T")]
    [InlineData("type X<T: bool = none | string & bool & T | number> = T")]
    public void ThrowsFor_CircularTypeAlias_Reference(string source)
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InfiniteType, "Type 'T' circularly references itself.");
    }

    [Theory]
    [InlineData("1 + true")]
    [InlineData("true + 1")]
    [InlineData("'abc' + 69")]
    [InlineData("'hello' - 'world'")]
    [InlineData("1 - 'hello'")]
    [InlineData("true * false")]
    [InlineData("1 / true")]
    [InlineData("1 // '2'")]
    [InlineData("5 % '3'")]
    [InlineData("1 ^ true")]
    [InlineData("1 & '2'")]
    [InlineData("1 | true")]
    [InlineData("1 << '2'")]
    [InlineData("1 >> true")]
    [InlineData("1 >>> '2'")]
    [InlineData("true && 5")]
    [InlineData("'hello' && false")]
    [InlineData("5 || 'world'")]
    [InlineData("true || 42")]
    public void ThrowsFor_BinaryOperator_InvalidOperandTypes(string source)
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Assert.Contains(diagnostics.Set, d => d.Code is InternalCodes.TypeMismatch or InternalCodes.InvalidBinaryOp);
    }

    [Theory]
    [InlineData("!5")]
    [InlineData("!'hello'")]
    [InlineData("!42")]
    [InlineData("~true")]
    [InlineData("~'hello'")]
    [InlineData("-true")]
    [InlineData("-'hello'")]
    [InlineData("-false")]
    public void ThrowsFor_UnaryOperator_InvalidOperandType(string source)
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Assert.Contains(diagnostics.Set, d => d.Code is InternalCodes.TypeMismatch or InternalCodes.InvalidUnaryOp);
    }

    [Fact]
    public void ThrowsFor_NonGenericFunctionCall_ArgumentTypeMismatch()
    {
        const string source = """
            fn add(a: number, b: number) -> a + b
            add(1, "two")
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type '\"two\"' is not assignable to type 'number'."
        );
    }

    [Fact]
    public void ThrowsFor_GenericFunctionCall_InferenceConflict()
    {
        const string source = """
            fn first<T>(a: T, b: T) -> a
            first(42, "hello")
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '\"hello\"' is not assignable to type '42'.");
    }

    [Fact]
    public void ThrowsFor_FunctionCall_IncorrectGenericArity()
    {
        const string source = """
            fn id<T>(value: T) -> value
            id::<number, string>(69)
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.GenericArity,
            "Function expects 1 type argument, but 2 were provided."
        );
    }

    [Fact]
    public void ThrowsFor_FunctionCall_IncorrectArity()
    {
        const string source = """
            fn id(value: number) -> value
            id(69, 420)
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvocationArity,
            "Function expects 1 argument, but 2 were provided."
        );
    }

    [Fact]
    public void ThrowsFor_FunctionCall_WithOptionalParams_IncorrectArity()
    {
        const string source = """
            fn id(value: number, other: number?) -> value
            id(69, 420, 1337)
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvocationArity,
            "Function expects 1-2 arguments, but 3 were provided."
        );
    }

    [Fact]
    public void ThrowsFor_GenericFunctionCall_ExplicitTypeArgumentMismatch()
    {
        const string source = """
            fn id<T>(value: T) -> value
            id::<string>(69)
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type '69' is not assignable to type 'string'."
        );
    }

    [Fact]
    public void ThrowsFor_GenericFunctionCall_WithConstraintViolation()
    {
        const string source = """
            fn identity<T: number>(value: T) -> value
            identity("hello")
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.ConstraintViolation,
            "Type '\"hello\"' does not satisfy constraint 'number' for type parameter 'T'."
        );
    }

    [Fact]
    public void ThrowsFor_GenericTypeAlias_WithConstraintViolation()
    {
        const string source = """
            type Box<T: number> = T
            let x: Box<string> = "hello"
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.ConstraintViolation,
            "Type 'string' does not satisfy constraint 'number' for type parameter 'T'."
        );
    }

    [Fact]
    public void ThrowsFor_GenericTypeAlias_MissingRequiredTypeParameter()
    {
        const string source = """
            type Id<T, U = number> = T
            type X = Id
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.GenericArity, "Type 'Id<T, U = number>' expects 1-2 type arguments, but 0 were provided.");
    }

    [Fact]
    public void ThrowsFor_NonNumeric_RangeLiteral()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("'a'..'b'");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '\"a\"' is not assignable to type 'number'.");
    }

    [Fact]
    public void ThrowsFor_QualifiedName_Chained_AfterNumber()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let r = (1..10); r.minimum.foo");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidAccess, "Cannot access property 'foo' on type 'number'.");
    }

    [Fact]
    public void ThrowsFor_QualifiedName_Chained_MissingIntermediate()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let r = (1..10); r.missing.next");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidAccess,
            "Expression of type '\"missing\"' cannot be used to index type 'Range'. Property 'missing' does not exist on type 'Range'."
        );
    }

    [Fact]
    public void ThrowsFor_PropertyAccess_Chained_AfterNumber()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("(1..10).minimum.foo");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidAccess, "Cannot access property 'foo' on type 'number'.");
    }

    [Fact]
    public void ThrowsFor_PropertyAccess_Chained_MissingIntermediate()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("(1..10).missing.next");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidAccess,
            "Expression of type '\"missing\"' cannot be used to index type 'Range'. Property 'missing' does not exist on type 'Range'."
        );
    }

    [Fact]
    public void ThrowsFor_StringEnumMemberWithoutInitializer()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("enum Colors : string { Red, Green = \"00FF00\" }");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.StringEnumMemberMustHaveInitializer,
            "Member 'Red' of string enum 'Colors' must have an initializer."
        );
    }

    [Fact]
    public void ThrowsFor_InvalidEnumBaseType()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("enum Flags : bool { Flag1 = true }");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidEnumBaseType,
            "Invalid enum base type.",
            "valid types are 'string' and 'number'"
        );
    }

    [Fact]
    public void ThrowsFor_EnumMemberNonConstantInitializer()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("enum Test { A = 1 + 2, B }");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.DynamicEnumMemberInitializer,
            "Enum member initializers must be constant values."
        );
    }

    [Fact]
    public void ThrowsFor_EnumTypeMismatch()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("enum Status { Active, Inactive } let x: Status = 5");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '5' is not assignable to type '0 | 1'.");
    }

    [Fact]
    public void Allows_ReservedLuauKeywordAsEnumMemberName()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("enum Test { until, A }");
        Assert.Null(diagnostics.Find(d => d.Code == InternalCodes.ReservedLuauKeyword));
    }

    [Fact]
    public void Allows_ReservedLuauKeywordAsStringEnumMemberName()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("enum Test: string { until = \"until\" }");
        Assert.Null(diagnostics.Find(d => d.Code == InternalCodes.ReservedLuauKeyword));
    }

    [Fact]
    public void ThrowsFor_IfStatement_NonBooleanCondition()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("if 42 { 1 }");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type '42' is not assignable to type 'bool'."
        );
    }

    [Fact]
    public void ThrowsFor_IfStatement_WithOptionalCondition()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let x: bool? = true; if x { 1 }");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type 'bool?' is not assignable to type 'bool'."
        );
    }

    [Fact]
    public void ThrowsFor_Never_InBinaryOperation()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let x = none; if x != none x + 1");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidBinaryOp, "No binary operation for 'never' + 'number'.");
    }

    [Fact]
    public void ThrowsFor_Never_InUnaryOperation()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let x = none; if x != none { -x }");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidUnaryOp, "No unary operation for -never.");
    }

    [Fact]
    public void ThrowsFor_InvalidCall()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let x = 1; x()");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidInvocation, "Cannot call value of type '1'.");
    }

    [Fact]
    public void ThrowsFor_Cast_Mismatch()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("69 as string");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '69' is not assignable to type 'string'.");
    }

    [Fact]
    public void ThrowsFor_AssignToImmutable_ObjectIndexer()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            "interface ImmutRecord<K, V> { [K]: V }; let x = none as never as ImmutRecord<string, bool>; x['abc'] = false"
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.AssignToImmutable, "Cannot assign to immutable index 'string'.");
    }

    [Fact]
    public void ThrowsFor_AssignToImmutable_ObjectProperty()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("interface Obj { prop: number }; let x = none as never as Obj; x.prop = 69");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.AssignToImmutable, "Cannot assign to immutable property 'prop'.");
    }

    [Fact]
    public void ThrowsFor_AssignToImmutable_Nested_ObjectProperty()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            "interface Inner { prop: number } interface Obj { inner: Inner }; let x = none as never as Obj; x.inner.prop = 69"
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.AssignToImmutable, "Cannot assign to immutable property 'prop'.");
    }

    [Theory]
    [InlineData("let s = \"abcdef\"; s[1..3] = \"abc\"", "string[Range]")]
    [InlineData("let s = \"abcdef\"; s[1] = \"a\"", "string[number]")]
    [InlineData("let a = mut [1, 2, 3]; a[1..2] = [69, 420]", "number[mut][Range]")]
    public void ThrowsFor_AccessMacro_Assignment(string source, string assignType)
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidAccess, $"Cannot assign to '{assignType}' because the expression will be replaced by a macro.");
    }

    [Fact]
    public void ThrowsFor_IndexedType_InvalidTarget()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("type X = number['abc']");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidAccess, "Type '\"abc\"' cannot be used to index type 'number'.");
    }

    [Fact]
    public void ThrowsFor_ParameterDefaultMismatch()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("fn foo(a: number = 'oops') {}");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '\"oops\"' is not assignable to type 'number'.");
    }

    [Fact]
    public void ThrowsFor_InterfaceInvocation_NotAnInterface()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let x = 1; new x {}");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidInvocation, "Type '1' is not an interface.");
    }

    [Fact]
    public void ThrowsFor_InterfaceInvocation_PropertyNotFound()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("interface I { x: number } new I { foo: 1 }");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidAccess, "Property 'foo' does not exist on interface 'I'.");
    }

    [Fact]
    public void ThrowsFor_InterfaceInvocation_PropertyTypeMismatch()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("interface I { x: number } new I { x: 'abc' }");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '\"abc\"' is not assignable to type 'number'.");
    }

    [Fact]
    public void ThrowsFor_InterfaceInvocation_IndexerRequired()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("interface I { } new I { [0]: 1 }");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidAccess, "Interface 'I' does not have an indexer.");
    }

    [Fact]
    public void ThrowsFor_InterfaceInvocation_IndexerTypeMismatch()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("interface I { [number]: string } new I { ['text']: 1 }");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '\"text\"' is not assignable to type 'number'.");
    }

    [Fact]
    public void ThrowsFor_InterfaceInvocation_MissingPropertyInitializer()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("interface I { x: number, y: number } new I { x: 1 }");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.IncompleteInterfaceInvocation, "Missing property initializer for 'y' in interface 'I'.");
    }

    [Fact]
    public void ThrowsFor_InterfaceInvocation_GenericWrongArity()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("interface I<T> { value: T } new I::<number, string> { value: 1 }");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.GenericArity, "Interface 'I<T>' expects 1 type argument, but 2 were provided.");
    }

    [Fact]
    public void ThrowsFor_InterfaceInvocation_GenericConstraintViolation()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("interface I<T: number> { value: T } new I::<string> { value: 'x' }");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.ConstraintViolation, "Type 'string' does not satisfy constraint 'number' for type parameter 'T'.");
    }

    [Fact]
    public void ThrowsFor_WhileLoop_NonBooleanCondition()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("while 1 { }");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '1' is not assignable to type 'bool'.");
    }

    [Fact]
    public void ThrowsFor_After_NonNumberDuration()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("after true { }");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type 'true' is not assignable to type 'number'.");
    }

    [Fact]
    public void ThrowsFor_FunctionTypeParameterDefault_ConstraintViolation()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("fn foo<T: number = 'hello'>() {}");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '\"hello\"' is not assignable to type 'number'.");
    }

    [Fact]
    public void ThrowsFor_IndexedType_MissingProperty()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("enum E { A, B } type X = E[\"nonexistent\"]");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidAccess,
            "Expression of type '\"nonexistent\"' cannot be used to index type '{ A: 0, B: 1 }'. Property 'nonexistent' does not exist on type '{ A: 0, B: 1 }'."
        );
    }

    [Fact]
    public void ThrowsFor_TernaryOperator_NonBoolCondition()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("1 ? 2 : 3");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '1' is not assignable to type 'bool'.");
    }

    [Fact]
    public void ThrowsFor_TernaryOperator_OptionalBoolConditionWithoutNarrowing()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let b: bool? = true; b ? 1 : 2");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type 'bool?' is not assignable to type 'bool'.");
    }

    [Fact]
    public void ThrowsFor_TernaryOperator_ConditionIsString()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("'hi' ? 1 : 2");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '\"hi\"' is not assignable to type 'bool'.");
    }

    [Fact]
    public void ThrowsFor_TernaryOperator_ConditionIsNumber()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("42 ? 1 : 2");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '42' is not assignable to type 'bool'.");
    }

    [Fact]
    public void ThrowsFor_TernaryOperator_ConditionIsOptionalStringWithoutNarrowing()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let s: string? = 'a'; s ? 1 : 2");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type 'string?' is not assignable to type 'bool'.");
    }

    [Fact]
    public void ThrowsFor_KeyOf_OnPrimitive()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("type K = keyof(number)");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidKeyOf, "Cannot access keys of type 'number'.");
    }

    [Fact]
    public void ThrowsFor_KeyOf_OnFunctionType()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("type K = keyof(fn(): void)");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidKeyOf, "Cannot access keys of type 'fn(): void'.");
    }

    [Fact]
    public void ThrowsFor_NestedKeyOf()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("interface I { a: number } type K = keyof(keyof(I))");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidKeyOf, "Cannot access keys of type 'string'.");
    }

    [Fact]
    public void ThrowsFor_GenericInterfaceWithConstraintViolation()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("interface I<T: number> { value: T }; new I::<string> { value: 'x' }");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.ConstraintViolation, "Type 'string' does not satisfy constraint 'number' for type parameter 'T'.");
    }

    [Fact]
    public void ThrowsFor_ConstraintViolation_DeepInstantiation()
    {
        const string source = """
            type Box<T> = T
            type NumericBox<T: Box<number>> = T
            let x: NumericBox<string> = "hello"
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.ConstraintViolation, "Type 'string' does not satisfy constraint 'Box<number>' for type parameter 'T'.");
    }

    [Fact]
    public void ThrowsFor_GenericTypeAlias_UnionArgumentViolatesConstraint()
    {
        const string source = """
            type OnlyNum<T: number> = T
            let x: OnlyNum<number | string> = 1
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.ConstraintViolation,
            "Type 'number | string' does not satisfy constraint 'number' for type parameter 'T'."
        );
    }

    [Fact]
    public void ThrowsFor_GenericFunctionCall_InferredUnionViolatesConstraint()
    {
        const string source = """
            fn onlyNum<T: number>(x: T) -> x
            onlyNum(42 as (number | string))
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.ConstraintViolation,
            "Type 'number | string' does not satisfy constraint 'number' for type parameter 'T'."
        );
    }

    [Fact]
    public void ThrowsFor_Inference_VariableDeclarationAnnotation_ViolatesConstraint()
    {
        const string source = """
            fn create<T: number>(value: T) -> value
            let x: string = create()
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.ConstraintViolation,
            "Type 'string' does not satisfy constraint 'number' for type parameter 'T'."
        );
    }

    [Fact]
    public void ThrowsFor_Inference_ArgumentBasedBinding_TakesPriorityOverConflictingContext()
    {
        const string source = """
            fn wrap<T>(value: T) -> value
            let x: string = wrap(42)
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '42' is not assignable to type 'string'.");
    }

    [Fact]
    public void Infers_GenericFunctionArgument_FromExpectedParameterType()
    {
        const string source = """
            let numbers = [1, 2, 3, 4];
            fn map<T, U>(array: T[], converter: fn(e: T): U): U[] {
              let new_array: U[mut] = mut [];
              for v : array
                new_array.push(converter(v));

              return new_array;
            }

            fn id<T>(n: T) -> n;
            map(numbers, id);
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
    }

    [Fact]
    public void Infers_GenericFunctionArgument_ResultTypeIsPrecise()
    {
        const string source = """
            let numbers = [1, 2, 3, 4];
            fn map<T, U>(array: T[], converter: fn(e: T): U): U[] {
              let new_array: U[mut] = mut [];
              for v : array
                new_array.push(converter(v));

              return new_array;
            }

            fn id<T>(n: T) -> n;
            let result: number[] = map(numbers, id);
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
    }

    [Fact]
    public void ThrowsFor_GenericFunctionArgument_InferredTypeViolatesConstraint()
    {
        const string source = """
            fn map<T, U>(array: T[], converter: fn(e: T): U): U[] {
              let new_array: U[mut] = mut [];
              for v : array
                new_array.push(converter(v));

              return new_array;
            }

            fn identity<T: string>(x: T) -> x;
            let numbers = [1, 2, 3];
            map(numbers, identity);
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type 'fn<T: string>(T: string): T: string' is not assignable to type 'fn(number): number'."
        );
    }

    [Fact]
    public void Allows_GenericFunction_UsedAsPlainValue_NotAsArgument()
    {
        const string source = """
            fn id<T>(n: T) -> n;
            print(id::<number>(5));
            print(id(5));
            let f = id;
            print(f::<string>("hi"));
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
    }

    [Fact]
    public void ThrowsFor_Indexer_Override()
    {
        const string source = """
            interface Def {
                [string]: number;
            }

            interface Abc: Def {
                [number]: string;
            }

            let abc = new Abc { };
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.ConstraintIndexerOverride, "An indexer is already declared within constraint 'Def'.");
    }

    [Fact]
    public void ThrowsFor_Property_Override()
    {
        const string source = """
            interface Def {
                abc: number;
            }

            interface Abc: Def {
                abc: string;
            }

            let abc = new Abc { abc: 69 };
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.ConstraintPropertyOverride, "Property 'abc' is already declared within constraint 'Def'.");
    }

    [Theory]
    [InlineData(": string", "string")]
    [InlineData("", "\"\"")]
    public void ThrowsFor_Implement_ReturnTypeMismatch(string explicitReturn, string expected)
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            $$"""
            trait Iterator {
                fn next(): number
            }

            interface Foo;

            implement Iterator for Foo {
                fn next(){{explicitReturn}} {
                    return ""
                }
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            $"Type '{expected}' is not assignable to type 'number'."
        );
    }

    [Fact]
    public void ThrowsFor_Implement_ParameterTypeMismatch()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            trait Add {
                fn add(x: number): void
            }

            interface Foo;

            implement Add for Foo {
                fn add(x: string) { }
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type 'string' is not assignable to type 'number'."
        );
    }

    [Fact]
    public void ThrowsFor_Implement_DefaultValueWrongType()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            trait Add {
                fn add(x: number): void
            }

            interface Foo;

            implement Add for Foo {
                fn add(x = "") { }
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type '\"\"' is not assignable to type 'number'."
        );
    }

    [Fact]
    public void ThrowsFor_UnconstrainedTypeParameter_Index()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("fn abc<T>(i: T) -> ([1, 2, 3])[i]");

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidAccess,
            "Expression of type 'T' cannot be used to index type 'number[]'. Type parameter 'T' is unconstrained."
        );
    }
    #endregion ThrowsFor

    [Fact]
    public void WarnsFor_NullCoalescing_NonOptional()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("1 ?? 2");
        Assert.Contains(diagnostics.Set, d => d.Code == InternalCodes.RedundantCode);
    }

    [Fact]
    public void WarnsFor_UseRangeLiteral()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("new Range { minimum: 69, maximum: 420 }");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.SimplifiableCode, "Use a range literal.");
    }
    
    #region Checks
    [Theory]
    [InlineData("number", PrimitiveTypeKind.Number)]
    [InlineData("string", PrimitiveTypeKind.String)]
    [InlineData("bool", PrimitiveTypeKind.Bool)]
    public void Checks_Generic_IndexedType(string typeName, PrimitiveTypeKind expectedKind)
    {
        var type = Utility.GetLastStatementType(
            $$"""
            interface Names { number: number; string: string; bool: bool; }
            fn get_type<K: keyof(Names)>(k: K) -> none as never as Names[K];
            get_type("{{typeName}}");
            """
        );

        var primitive = Assert.IsType<PrimitiveType>(type);
        Assert.Equal(expectedKind, primitive.Kind);
    }

    [Fact]
    public void Checks_Generic_InterfaceIndex()
    {
        var type = Utility.GetLastStatementType(
            """
            interface Foo { bar: number, baz: string }
            fn idx<T: Foo, K: keyof(T)>(foo: T, k: K) -> foo[k];

            let foo = new Foo { bar: 69, baz: "abc" };
            idx(foo, "bar")
            """
        );

        Assert.Equal(PrimitiveType.Number, type);
    }

    [Fact]
    public void Checks_Generic_ArrayIndex()
    {
        var type = Utility.GetLastStatementType(
            """
            fn idx<T, I: number>(arr: T[], i: I) -> arr[i];
            idx([1, 2, 3], 2);
            """
        );

        Assert.Equal(PrimitiveType.Number, type);
    }

    [Fact]
    public void Checks_TraitMethod_FromInterfaceInvocation()
    {
        var type = Utility.GetLastStatementType(
            """
            trait Iterator {
                fn next(): number
            }

            interface Foo;

            implement Iterator for Foo {
                fn next() {
                    return 42
                }
            }

            let foo = new Foo {};
            foo.next;
            """
        );

        var functionType = Assert.IsType<FunctionType>(type);
        Assert.Empty(functionType.TypeParameters);
        Assert.Empty(functionType.ParameterTypes);
        Assert.Equal(PrimitiveType.Number, functionType.ReturnType);
    }

    [Fact]
    public void Allows_InterfaceProperty_AlongsideTraitMethods() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                trait Iterator {
                    fn next(): number
                }

                interface Foo {
                    value: number
                }

                implement Iterator for Foo {
                    fn next() {
                        return 420 * value
                    }
                }

                let foo = new Foo { value: 42 };
                foo.value;
                foo.next();
                """
            )
        );

    [Fact]
    public void Allows_Interface_WithMultipleTraits() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                trait A {
                    fn a: number
                }

                trait B {
                    fn b: string
                }

                interface Foo;

                implement A for Foo {
                    fn a {
                        return 1
                    }
                }

                implement B for Foo {
                    fn b {
                        return ""
                    }
                }

                let foo = new Foo {};
                foo.a();
                foo.b();
                """
            )
        );

    [Fact]
    public void Allows_GenericTraitImplementation() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                trait Iterator<T> {
                    fn next(): T
                }

                interface Numbers;

                implement Iterator<number> for Numbers {
                    fn next() {
                        return 1
                    }
                }
                """
            )
        );

    [Fact]
    public void Allows_InterfaceInvocation_WithImplementedTraitMethod() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                trait Iterator {
                    fn next(): number
                }

                interface Foo;

                implement Iterator for Foo {
                    fn next() {
                        return 42
                    }
                }

                let foo = new Foo {};
                foo.next();
                """
            )
        );

    [Fact]
    public void Allows_Implement_ReturnInference() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                trait Iterator {
                    fn next(): number
                }

                interface Foo;

                implement Iterator for Foo {
                    fn next() {
                        return 1
                    }
                }
                """
            )
        );

    [Fact]
    public void Allows_Implement_ParameterInference() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                trait Iterator {
                    fn next(x: number): number
                }

                interface Foo;

                implement Iterator for Foo {
                    fn next(x) {
                        return x
                    }
                }
                """
            )
        );

    [Fact]
    public void Checks_Trait_EmptyObject()
    {
        var type = Utility.GetLastStatementType("trait Empty { }");
        var interfaceType = Assert.IsType<InterfaceType>(type);
        Assert.Equal(ObjectType.Empty, interfaceType.ObjectType);
    }

    [Fact]
    public void Checks_Trait_MethodsBecomeObjectProperties()
    {
        var type = Utility.GetLastStatementType(
            """
            trait Math {
                fn add(a: number, b: number): number
                fn negate(x: number): number
            }
            """
        );

        var interfaceType = Assert.IsType<InterfaceType>(type);
        Assert.Collection(
            interfaceType.ObjectType.Properties,
            p => Assert.Equal("add", p.Name),
            p => Assert.Equal("negate", p.Name)
        );
    }

    [Fact]
    public void Checks_Trait_ProducesGenericType()
    {
        var type = Utility.GetLastStatementType(
            """
            trait Iterator<T> {
                fn next(): T
            }
            """
        );

        var generic = Assert.IsType<GenericType>(type);
        Assert.Single(generic.Parameters);

        var underlying = Assert.IsType<InterfaceType>(generic.UnderlyingType);
        Assert.Equal("Iterator", underlying.Name);

        var next = underlying.ObjectType.Properties.Single();
        var fn = Assert.IsType<FunctionType>(next.ValueType);
        var parameter = Assert.IsType<TypeParameter>(fn.ReturnType);
        Assert.Equal("T", parameter.Name);
        Assert.Null(parameter.Constraint);
        Assert.Null(parameter.DefaultType);
    }

    [Fact]
    public void Checks_Trait_ProducesInterfaceType()
    {
        var type = Utility.GetLastStatementType(
            """
            trait Iterator {
                fn next(): number
            }
            """
        );

        var interfaceType = Assert.IsType<InterfaceType>(type);
        Assert.Equal("Iterator", interfaceType.Name);
        Assert.Single(interfaceType.ObjectType.Properties);

        var next = interfaceType.ObjectType.Properties.Single();
        Assert.Equal("next", next.Name);

        var fn = Assert.IsType<FunctionType>(next.ValueType);
        Assert.Equal(PrimitiveType.Number, fn.ReturnType);
    }

    [Fact]
    public void Checks_CastToIntersectionType_Never()
    {
        var result = Utility.AssertNoErrors(Utility.TypeCheck("69 as (number & string)"));
        Assert.Equal(PrimitiveType.Never, result.ReturnType);
    }

    [Fact]
    public void Checks_InterfaceInvocation_ConstraintMembers()
    {
        const string source = """
            interface Def {
                [string | number]: number;
                def: string;
            }

            interface Abc: Def {
                abc: number;
            }

            let abc = new Abc { abc: 69, def: "foo", ["balls"]: 69, [69]: 420 };
            """;

        var result = Utility.AssertNoErrors(Utility.TypeCheck(source));
        Assert.IsType<InterfaceType>(result.ReturnType);
    }

    [Fact]
    public void Checks_GenericInference_RepeatedIdenticalLiteralPreserved()
    {
        const string source = """
            fn pair<T>(a: T, b: T) -> a
            pair(1, 1)
            """;

        var result = Utility.AssertNoErrors(Utility.TypeCheck(source));
        Assert.Equal(new LiteralType(1L), result.ReturnType);
    }

    [Fact]
    public void Checks_GenericInference_ThreeLiteralArgumentsStillWiden()
    {
        const string source = """
            fn first<T>(a: T, b: T, c: T) -> a
            first(1, 2, 3)
            """;

        var result = Utility.AssertNoErrors(Utility.TypeCheck(source));
        Assert.Equal(PrimitiveType.Number, result.ReturnType);
    }

    [Fact]
    public void Checks_GenericInference_DefaultTypeUsedWhenParameterUnconstrained()
    {
        const string source = """
            fn value<T = string>() -> none as never as T
            value()
            """;

        var result = Utility.AssertNoErrors(Utility.TypeCheck(source));
        Assert.Equal(PrimitiveType.String, result.ReturnType);
    }

    [Fact]
    public void Checks_Narrowing_MultipleConditions()
    {
        const string source = """
            interface A { kind: "A", value: number }
            interface B { kind: "B", value: string }

            let x: A | B = none as never;
            if x.kind == "A" && x.kind == "A" {
                x.value
            }
            """;

        var result = Utility.AssertNoErrors(Utility.TypeCheck(source));
        var optional = Assert.IsType<OptionalType>(result.ReturnType);
        Assert.Equal(PrimitiveType.Number, optional.NonNullableType);
    }

    [Fact]
    public void Checks_NameOfEnumMember()
    {
        const string source = """
            enum Color { Red }

            nameof(Color.Red)
            """;

        var result = Utility.TypeCheck(source);
        Utility.AssertNoErrors(result);

        Assert.Equal(new LiteralType("Color.Red"), result.ReturnType);
    }

    [Fact]
    public void Checks_Narrowing_NestedPropertyByEnum()
    {
        const string source = """
            enum Kind { A, B }

            interface A { kind: Kind['A'], value: number }
            interface B { kind: Kind['B'], value: string }
            interface WithChild<T> { child: T }
            type U = WithChild<A> | WithChild<B>

            let x: U = none as never;
            if x.child.kind == Kind.A {
                x.child.value
            }
            """;

        var result = Utility.TypeCheck(source);
        Utility.AssertNoErrors(result);

        var optional = Assert.IsType<OptionalType>(result.ReturnType);
        Assert.Equal(PrimitiveType.Number, optional.NonNullableType);
    }

    [Fact]
    public void Checks_Narrowing_ElementAccessByEnum()
    {
        const string source = """
            enum Kind { A, B }

            interface A { kind: Kind['A'], value: number }
            interface B { kind: Kind['B'], value: string }

            let xs: (A | B)[] = [];

            if xs[0].kind == Kind.A {
                xs[0].value
            }
            """;

        var result = Utility.TypeCheck(source);
        Utility.AssertNoErrors(result);

        var optional = Assert.IsType<OptionalType>(result.ReturnType);
        Assert.Equal(PrimitiveType.Number, optional.NonNullableType);
    }

    [Fact]
    public void Checks_Narrowing_PropertyByEnum()
    {
        const string source = """
            enum Kind { A, B }

            interface A { kind: Kind['A'], value: number }
            interface B { kind: Kind['B'], value: string }

            let x: A | B = none as never;
            if x.kind == Kind.A {
                x.value
            }
            """;

        var result = Utility.TypeCheck(source);
        Utility.AssertNoErrors(result);

        var optional = Assert.IsType<OptionalType>(result.ReturnType);
        Assert.Equal(PrimitiveType.Number, optional.NonNullableType);
    }

    [Fact]
    public void Checks_Narrowing_ElementAccessOfProperty()
    {
        const string source = """
            interface A { kind: "A", vals: number[] }
            interface B { kind: "B", vals: string[] }
            type U = A | B;

            let x: U = new A { kind: "A", vals: [] };
            if x.kind == "A" {
                x.vals[0]
            }
            """;

        var result = Utility.TypeCheck(source);
        Utility.AssertNoErrors(result);

        var optional = Assert.IsType<OptionalType>(result.ReturnType);
        Assert.Equal(PrimitiveType.Number, optional.NonNullableType);
    }

    [Fact]
    public void Checks_Narrowing_ElementAccessPropertyChain()
    {
        const string source = """
            interface A { kind: "A", val: number }
            interface B { kind: "B", val: string }
            interface WithChild<T> { child: T }
            type U = (WithChild<A> | WithChild<B>)[];

            let arr: U = [];
            if arr[0].child.kind == "A" {
                arr[0].child.val
            }
            """;

        var result = Utility.TypeCheck(source);
        Utility.AssertNoErrors(result);

        var optional = Assert.IsType<OptionalType>(result.ReturnType);
        Assert.Equal(PrimitiveType.Number, optional.NonNullableType);
    }

    [Fact]
    public void Checks_Narrowing_PropertyAfterElementAfterProperty()
    {
        const string source = """
            interface A { kind: "A", val: number }
            interface B { kind: "B", val: string }
            interface WithItems<T> { items: T[] }
            type U = WithItems<A | B>;

            let x: U = none as never;
            if x.items[0].kind == "A" {
                x.items[0].val
            }
            """;

        var result = Utility.TypeCheck(source);
        Utility.AssertNoErrors(result);

        var optional = Assert.IsType<OptionalType>(result.ReturnType);
        Assert.Equal(PrimitiveType.Number, optional.NonNullableType);
    }

    [Fact]
    public void Checks_Narrowing_NotEquals_ElementAccess()
    {
        const string source = """
            interface A { kind: "A", val: number }
            interface B { kind: "B", val: string }
            type U = A | B;

            let arr: U[] = [];
            if arr[0].kind != "A" {
                arr[0].val
            }
            """;

        var result = Utility.TypeCheck(source);
        Utility.AssertNoErrors(result);

        var optional = Assert.IsType<OptionalType>(result.ReturnType);
        Assert.Equal(PrimitiveType.String, optional.NonNullableType);
    }

    [Fact]
    public void Checks_Narrowing_RepeatedElementAccess()
    {
        const string source = """
            interface A { kind: "A", val: number }
            interface B { kind: "B", val: string }
            type U = A | B;

            let arr: U[] = [];
            if arr[0].kind == "A" {
                arr[0].kind;
                arr[0].val;
            }
            """;

        var result = Utility.TypeCheck(source);
        Utility.AssertNoErrors(result);

        var optional = Assert.IsType<OptionalType>(result.ReturnType);
        Assert.Equal(PrimitiveType.Number, optional.NonNullableType);
    }

    [Fact]
    public void Checks_Narrowing_DifferentIndicesRemainIndependent()
    {
        const string source = """
            interface A { kind: "A", val: number }
            interface B { kind: "B", val: string }
            type U = A | B;

            let arr: U[] = [];
            if arr[0].kind == "A" {
                arr[1].val
            }
            """;

        var result = Utility.TypeCheck(source);
        Utility.AssertNoErrors(result);

        var optional = Assert.IsType<OptionalType>(result.ReturnType);
        Assert.IsType<UnionType>(optional.NonNullableType);
    }

    [Fact]
    public void Checks_Narrowing_LogicalAnd_ElementAccess()
    {
        const string source = """
            interface A { kind: "A", val: number }
            interface B { kind: "B", val: string }
            type U = A | B;

            let arr: U[] = [];
            if arr[0].kind == "A" && arr[0].kind == "A" {
                arr[0].val
            }
            """;

        var result = Utility.TypeCheck(source);
        Utility.AssertNoErrors(result);

        var optional = Assert.IsType<OptionalType>(result.ReturnType);
        Assert.Equal(PrimitiveType.Number, optional.NonNullableType);
    }

    [Fact]
    public void Checks_Narrowing_NullableElementAccess()
    {
        const string source = """
            interface A { kind: "A", val: number }
            interface B { kind: "B", val: string }
            type U = A | B;

            let arr: U?[] = [];
            if arr[0] != none && arr[0].kind == "A" {
                arr[0].val
            }
            """;

        var result = Utility.TypeCheck(source);
        Utility.AssertNoErrors(result);

        var optional = Assert.IsType<OptionalType>(result.ReturnType);
        Assert.Equal(PrimitiveType.Number, optional.NonNullableType);
    }

    [Fact]
    public void Checks_Narrowing_AndFalseBranch_MergedProperty()
    {
        const string source = """
            interface A { kind: "A", val: number };
            interface B { kind: "B", val: string };
            type U = A | B;
            let x = none as never as U;
            if x.kind == "A" && x.val == 5 {
            } else {
                x.val
            }
            """;

        var result = Utility.TypeCheck(source);
        Utility.AssertNoErrors(result);
        var optional = Assert.IsType<OptionalType>(result.ReturnType);
        var union = Assert.IsType<UnionType>(optional.NonNullableType);
        Assert.Equal(2, union.Types.Count);
        Assert.Contains(union.Types, t => t.Equals(PrimitiveType.Number));
        Assert.Contains(union.Types, t => t.Equals(PrimitiveType.String));
    }

    [Fact]
    public void Checks_Narrowing_ThreeVariantOr_FalseBranch()
    {
        const string source = """
            interface A { kind: "A", val: number }
            interface B { kind: "B", val: string }
            interface C { kind: "C", val: bool }
            type U = A | B | C;
            let x = none as never as U;
            if x.kind == "A" || x.kind == "B" {
            } else {
                x.val && true
            }
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
    }

    [Fact]
    public void Checks_Narrowing_AndOfParenthesizedOr()
    {
        const string source = """
            interface A { kind: "A", val: number }
            interface B { kind: "B", val: string }
            type U = A | B;
            let x = none as never as U;
            while (x.kind == "A" || x.kind == "B") && x.kind == "A" {
                x.val
            }
            """;

        var result = Utility.AssertNoErrors(Utility.TypeCheck(source));
        Assert.Equal(PrimitiveType.Number, result.ReturnType);
    }

    [Fact]
    public void Checks_Narrowing_NotOfOr()
    {
        const string source = """
            interface A { kind: "A", val: number }
            interface B { kind: "B", val: string }
            interface C { kind: "C", val: bool }
            type U = A | B | C;
            let x = none as never as U;
            while !(x.kind == "A" || x.kind == "B") {
                x.val
            }
            """;

        var result = Utility.AssertNoErrors(Utility.TypeCheck(source));
        Assert.Equal(PrimitiveType.Bool, result.ReturnType);
    }

    [Fact]
    public void Checks_Narrowing_DoubleNegation()
    {
        const string source = """
            interface A { kind: "A", val: number }
            interface B { kind: "B", val: string }
            type U = A | B;
            let x = none as never as U;
            while !!(x.kind == "A") {
                x.val
            }
            """;

        var result = Utility.AssertNoErrors(Utility.TypeCheck(source));
        Assert.Equal(PrimitiveType.Number, result.ReturnType);
    }

    [Fact]
    public void Checks_Narrowing_EqualityOperandsReversed()
    {
        const string source = """
            interface A { kind: "A", val: number }
            interface B { kind: "B", val: string }
            type U = A | B;
            let x = none as never as U;
            while "A" == x.kind {
                x.val
            }
            """;

        var result = Utility.AssertNoErrors(Utility.TypeCheck(source));
        Assert.Equal(PrimitiveType.Number, result.ReturnType);
    }

    [Fact]
    public void Checks_Narrowing_ChainedAnd_ThreeConditions()
    {
        const string source = """
            interface Loading { kind: "Loading" }
            interface Success { kind: "Success", data: string, ok: bool }
            interface Error { kind: "Error", error: string }
            type Status = Loading | Success | Error;
            let s = none as never as Status;
            while s.kind == "Success" && s.data == "x" && s.ok == true {
                s.data
            }
            """;

        var result = Utility.AssertNoErrors(Utility.TypeCheck(source));
        var literal = Assert.IsType<LiteralType>(result.ReturnType);
        Assert.Equal("x", literal.Value);
    }

    [Fact]
    public void Checks_Narrowing_NestedFlowScopes_Accumulate()
    {
        const string source = """
            interface A { kind: "A", val: number }
            interface B { kind: "B", val: string }
            type U = A | B;
            let x = none as never as U;
            while x.kind == "A" {
                while x.val > 0 {
                    x.val
                }
            }
            """;

        var result = Utility.AssertNoErrors(Utility.TypeCheck(source));
        Assert.Equal(PrimitiveType.Number, result.ReturnType);
    }

    [Fact]
    public void Checks_Narrowing_PropertyOfElementAccess()
    {
        const string source = """
            interface A { kind: "A", val: number }
            interface B { kind: "B", val: string }
            type U = A | B;
            let arr: U[] = [];
            if arr[0].kind == "A" {
                arr[0].val
            }
            """;

        var result = Utility.TypeCheck(source);
        Utility.AssertNoErrors(result);
        var optional = Assert.IsType<OptionalType>(result.ReturnType);
        Assert.Equal(PrimitiveType.Number, optional.NonNullableType);
    }

    [Fact]
    public void Checks_Narrowing_SiblingIfStatements_NoLeakage()
    {
        const string source = """
            interface A { kind: "A", val: number }
            interface B { kind: "B", val: string }
            type U = A | B;
            let x = none as never as U;
            if x.kind == "A" { x.val }
            if x.kind == "B" { x.val }
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
    }

    [Fact]
    public void Checks_Narrowing_ComplexLogicalExpression()
    {
        const string source = """
            interface A { kind: "A", val: number };
            interface B { kind: "B", val: string };
            type U = A | B;
            let x = none as never as U;
            if (x.kind == "A" && x.val > 0) || (x.kind == "B" && x.val == "hi") {
                x.val
            }
            """;

        var result = Utility.TypeCheck(source);
        Utility.AssertNoErrors(result);
        var optional = Assert.IsType<OptionalType>(result.ReturnType);
        var union = Assert.IsType<UnionType>(optional.NonNullableType);
        Assert.Equal(2, union.Types.Count);
    }

    [Fact]
    public void Checks_Narrowing_LogicalNot()
    {
        const string source = """
            interface A { kind: "A", val: number };
            interface B { kind: "B", val: string };
            type U = A | B;

            let x = none as never as U;
            while !(x.kind == "A") {
                x.val
            }
            """;

        var result = Utility.AssertNoErrors(Utility.TypeCheck(source));
        Assert.Equal(PrimitiveType.String, result.ReturnType);
    }

    [Fact]
    public void Checks_Narrowing_LogicalAnd()
    {
        const string source = """
            interface A { kind: "A", val: number };
            interface B { kind: "B", val: string };
            type U = A | B;

            let x = none as never as U;
            while x.kind == "A" && x.val == 0 {
                x.val
            }
            """;

        var result = Utility.AssertNoErrors(Utility.TypeCheck(source));
        Assert.Equal(new LiteralType(0L), result.ReturnType);
    }

    [Fact]
    public void Checks_Narrowing_LogicalOr()
    {
        const string source = """
            interface A { kind: "A", val: number };
            interface B { kind: "B", val: string };
            interface C { kind: "C", val: bool };
            type U = A | B | C;
            let x = none as never as U;
            if x.kind == "A" || x.kind == "B" {
                x.val
            }
            """;

        var result = Utility.TypeCheck(source);
        Utility.AssertNoErrors(result);
        var optional = Assert.IsType<OptionalType>(result.ReturnType);
        var union = Assert.IsType<UnionType>(optional.NonNullableType);
        Assert.Equal(2, union.Types.Count);
        Assert.Contains(union.Types, t => t.Equals(PrimitiveType.Number));
        Assert.Contains(union.Types, t => t.Equals(PrimitiveType.String));
    }

    [Fact]
    public void Checks_Parenthesized_Narrowing()
    {
        const string source = """
            interface A { kind: "A", val: number };
            interface B { kind: "B", val: string };
            type U = A | B;

            let x = none as never as U;
            while ((((x.kind == "A")))) {
                x.val
            }
            """;

        var result = Utility.AssertNoErrors(Utility.TypeCheck(source));
        Assert.Equal(PrimitiveType.Number, result.ReturnType);
    }

    [Fact]
    public void Checks_Enum_KeyOf()
    {
        var type = Utility.GetLastStatementType("enum E { A, B } type K = keyof(E)");
        var union = Assert.IsType<UnionType>(type);
        Assert.Equal(2, union.Types.Count);
        Assert.Contains(union.Types, t => t is LiteralType { Value: "A" });
        Assert.Contains(union.Types, t => t is LiteralType { Value: "B" });
    }

    [Fact]
    public void Checks_Enum_IndexedType_WithExplicitValues()
    {
        var type = Utility.GetLastStatementType("enum E { A = 42.69, B = 99 } type T = E[\"A\"]");
        var literal = Assert.IsType<LiteralType>(type);
        Assert.Equal(42.69d, literal.Value);
    }

    [Fact]
    public void Checks_Enum_IndexedType_WithStringEnum()
    {
        var type = Utility.GetLastStatementType("enum E : string { A = \"foo\", B = nameof(E) } type T = E[\"B\"]");
        var literal = Assert.IsType<LiteralType>(type);
        Assert.Equal("E", literal.Value);
    }

    [Fact]
    public void Checks_Enum_AsTypeAnnotation_WithMatchingNumberLiteral()
    {
        const string source = """
            enum Status { Active, Inactive }
            let x: Status = 0
            x
            """;

        var type = Utility.GetLastStatementType(source);
        var union = Assert.IsType<UnionType>(type);
        Assert.Contains(union.Types, t => t is LiteralType { Value: 0d });
        Assert.Contains(union.Types, t => t is LiteralType { Value: 1d });
    }

    [Fact]
    public void Checks_Enum_AsTypeAnnotation_WithMatchingStringLiteral()
    {
        const string source = """
            enum Status : string { Active = "on", Inactive = "off" }
            let x: Status = "on"
            x
            """;

        var type = Utility.GetLastStatementType(source);
        var union = Assert.IsType<UnionType>(type);
        Assert.Contains(union.Types, t => t is LiteralType { Value: "on" });
        Assert.Contains(union.Types, t => t is LiteralType { Value: "off" });
    }

    [Fact]
    public void Checks_Enum_AsGenericArgument()
    {
        const string source = """
            enum Status { Active = 69.420, Inactive }
            fn id<T>(value: T): T -> value
            let x = id(Status.Inactive)
            x
            """;

        var type = Utility.GetLastStatementType(source);
        var literal = Assert.IsType<LiteralType>(type);
        Assert.Equal(70d, literal.Value);
    }

    [Fact]
    public void Checks_Narrowing_UnionWithThreeVariants_Equals()
    {
        const string source = """
            interface Loading { kind: "Loading" }
            interface Success { kind: "Success", data: string }
            interface Error { kind: "Error", error: string }
            type Status = Loading | Success | Error;

            let s = none as never as Status;
            if s.kind == "Success" {
                s.data
            }
            """;

        var result = Utility.TypeCheck(source);
        Utility.AssertNoErrors(result);

        var optional = Assert.IsType<OptionalType>(result.ReturnType);
        Assert.Equal(PrimitiveType.String, optional.NonNullableType);
    }

    [Fact]
    public void Checks_Narrowing_UnionWithThreeVariants_NotEquals()
    {
        const string source = """
            interface Loading { kind: "Loading" }
            interface Success { kind: "Success", data: string }
            interface Error { kind: "Error", error: string }
            type Status = Loading | Success | Error;

            let s = none as never as Status;
            if s.kind != "Loading" {
               s.kind
            }
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertNoErrors(diagnostics);
        var result = Utility.TypeCheck(source);
        Utility.AssertNoErrors(result);

        var optional = Assert.IsType<OptionalType>(result.ReturnType);
        Assert.IsType<UnionType>(optional.NonNullableType);
    }

    [Fact]
    public void Checks_Narrowing_NestedPropertyPath_Works()
    {
        const string source = """
            interface A { kind: "A", val: number }
            interface B { kind: "B", val: string };
            type Inner = A | B;

            interface Outer { inner: Inner };
            let o = none as never as Outer;
            if o.inner.kind == "A" {
                o.inner.val
            }
            """;

        var result = Utility.TypeCheck(source);
        Utility.AssertNoErrors(result);

        var optional = Assert.IsType<OptionalType>(result.ReturnType);
        Assert.Equal(PrimitiveType.Number, optional.NonNullableType);
    }

    [Fact]
    public void Checks_Narrowing_NestedElementAccess()
    {
        const string source = """
            let matrix = [[1, 2], [3, 4]];
            if matrix[0][1] == 2 {
                matrix[0][1]
            }
            """;

        var result = Utility.TypeCheck(source);
        Utility.AssertNoErrors(result);

        var optional = Assert.IsType<OptionalType>(result.ReturnType);
        var literal = Assert.IsType<LiteralType>(optional.NonNullableType);
        Assert.Equal(2L, literal.Value);
    }

    [Theory]
    [InlineData(true, "value", "number")]
    [InlineData(false, "error", "\"do_something failed to execute\"")]
    public void Checks_DiscriminatedUnion_Narrowing(bool ok, string property, string typeString)
    {
        var source = $$"""
            enum MyErrors: string {
                DoSomethingFailed = "do_something failed to execute"
            }

            let result = Result.ok::<number, MyErrors>(69);
            if {{(ok ? "" : "!")}}result.ok
                result.{{property}}
            """;

        var result = Utility.TypeCheck(source);
        Utility.AssertNoErrors(result);

        var optional = Assert.IsType<OptionalType>(result.ReturnType);
        Assert.IsType<PrimitiveType>(optional.NonNullableType, exactMatch: false);
        Assert.Equal(typeString, optional.NonNullableType.ToString());
    }

    [Fact]
    public void Checks_Inference_IntersectionParameterMultipleTypeParameters_UsesUnknownForUninferred()
    {
        const string source = "fn mix<A, B>(x: A & B): A & B -> x; mix(42)";
        var type = Utility.GetLastStatementType(source);
        Assert.Equal(PrimitiveType.Never, type);
    }

    [Fact]
    public void Checks_Inference_MissingRequiredTypeParameter_UsesUnknownForUninferred()
    {
        const string source = "fn identity<T, U = number>(value: T?) -> value; identity(none)";
        var type = Utility.GetLastStatementType(source);
        Assert.True(Type.IsNone(type));
    }

    [Fact]
    public void Checks_Inference_UnionMultipleTypeParams_UsesUnknownForUninferred()
    {
        const string source = "fn id<T, U>(x: T | U) -> x; id(42)";
        var type = Utility.GetLastStatementType(source);
        Assert.Equal(new LiteralType(42L), type);
    }

    [Fact]
    public void Checks_Inference_UnionArgumentMismatchedMemberCount_UsesUnknownForUninferred()
    {
        const string source = "fn create<T>(): T -> none as never as T; create()";
        var type = Utility.GetLastStatementType(source);
        Assert.True(Type.IsUnknown(type));
    }

    [Fact]
    public void Checks_Inference_EnclosingFunctionHasNoDeclaredReturnType_UsesUnknown()
    {
        const string source = "fn create<T>(): T -> none as never as T; fn make() { return create() }";
        var type = Utility.GetLastStatementType(source);
        var fnType = Assert.IsType<FunctionType>(type);
        Assert.True(Type.IsUnknown(fnType.ReturnType));
    }

    [Fact]
    public void Checks_Inference_NestedInBinaryOperator_UsesUnknown()
    {
        const string source = "fn create<T>(): T -> none as never as T; fn make() { return create() }";
        var type = Utility.GetLastStatementType(source);
        var fnType = Assert.IsType<FunctionType>(type);
        Assert.True(Type.IsUnknown(fnType.ReturnType));
    }

    [Fact]
    public void Checks_Inference_VariableDeclarationWithoutAnnotation_UsesUnknown()
    {
        const string source = "fn create<T>(): T -> none as never as T; let x = create()";
        var type = Utility.GetLastStatementType(source);
        Assert.True(Type.IsUnknown(type));
    }

    [Fact]
    public void Checks_Inference_ReturnTypeOnlyTypeParameterCannotInfer_UsesUnknown()
    {
        const string source = "fn create<T>(): T -> none as never as T; create()";
        var type = Utility.GetLastStatementType(source);
        Assert.Equal(PrimitiveType.Unknown, type);
    }

    [Fact]
    public void Checks_DefaultParameterValue()
    {
        var type = Utility.AssertNoErrors(Utility.TypeCheck("fn abc(x = 1) -> x; abc()")).ReturnType;
        Assert.Equal(new LiteralType(1L), type);
    }

    [Fact]
    public void Checks_Inference_ContextualReturnType_ResolvesSecondTypeParameterOnly()
    {
        const string source = """
            interface Pair<A, B> { first: A, second: B }
            fn makePair<A, B>(first: A): Pair<A, B> -> new Pair { first: first, second: none as never as B }
            fn compute: Pair<number, string> {
                return makePair(42)
            }
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
    }

    [Fact]
    public void Checks_Inference_ReturnTypeOnlyTypeParameter_VariableDeclarationContext()
    {
        const string source = "fn create<T>(): T -> none as never as T; let x: number = create(); x";
        var type = Utility.GetLastStatementType(source);
        Assert.True(type.Equals(PrimitiveType.Number), $"Expected 'number', got '{type}'");
    }

    [Fact]
    public void Checks_Inference_ReturnTypeOnlyTypeParameter_AssignmentContext()
    {
        const string source = """
            fn create<T>(): T -> none as never as T
            mut x: number = 0
            x = create()
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
    }

    [Fact]
    public void Checks_Inference_ReturnTypeOnlyTypeParameter_ParameterDefaultContext()
    {
        const string source = """
            fn create<T>(): T -> none as never as T
            fn use(x: number = create()) -> x
            use()
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Checks_Inference_ContextualReturnType_OverridesDefaultTypeParameter()
    {
        const string source = """
            interface ResultThing<T, E> { value: T?, error: E? }
            fn ok<T, E = string>(value: T): ResultThing<T, E> -> new ResultThing { value: value, error: none as never as E }
            enum MyErrors: string { Failed = "failed" }
            fn compute: ResultThing<number, MyErrors> {
                return ok(69)
            }
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
    }

    [Fact]
    public void Checks_Inference_MissingContext_FallsBackToDefaultTypeParameter()
    {
        const string source = """
            interface ResultThing<T, E> { value: T?, error: E? }
            fn ok<T, E = string>(value: T): ResultThing<T, E> -> new ResultThing { value: value, error: none as never as E }
            ok(69)
            """;

        var type = Utility.GetLastStatementType(source);
        var iface = Assert.IsType<InterfaceType>(type);
        var errorProp = iface.ObjectType.GetProperty("error")!;
        var optional = Assert.IsType<OptionalType>(TypeSimplifier.Simplify(errorProp.ValueType));
        Assert.True(optional.NonNullableType.Equals(PrimitiveType.String), $"Expected default 'string', got '{optional.NonNullableType}'");
    }

    [Fact]
    public void Checks_Inference_ReturnTypeOnlyTypeParameter_ContextualArrayType()
    {
        const string source = "fn create<T>(): T -> none as never as T; let x: string[] = create(); x";
        var type = Utility.GetLastStatementType(source);
        var array = Assert.IsType<ArrayType>(type);
        Assert.True(array.ElementType.Equals(PrimitiveType.String), $"Expected 'string', got '{array.ElementType}'");
    }

    [Fact]
    public void Checks_Inference_ReturnTypeOnlyTypeParameter_NestedReturnUsesEnclosingFunction()
    {
        const string source = """
            fn create<T>(): T -> none as never as T
            fn make: number {
                if true { return create() } else { return 0 }
            }
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
    }

    [Fact]
    public void Checks_ReturnType_Inference()
    {
        const string source = """
            interface ResultThing<T, Error> {
                value: T?;
                error: Error?;
            }

            fn ok<T, Error>(value: T): ResultThing<T, Error> {
                return new ResultThing { value: value, error: none as never as Error };
            }

            enum MyErrors: string {
                DoSomethingFailed = "do_something failed to execute"
            }

            fn do_something: ResultThing<number, MyErrors> {
                return ok(69);
            }
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
    }

    [Fact]
    public void Checks_Inference_RecursiveGenericType_DoesNotLoop()
    {
        const string source = """
            interface List<T> { value: T, next: List<T>? }
            fn first<T>(list: List<T>): T -> list.value
            first(new List { value: 42, next: none })
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
        var type = Utility.GetLastStatementType(source);
        var literal = Assert.IsType<LiteralType>(type);
        Assert.Equal(42L, literal.Value);
    }

    [Fact]
    public void Checks_NestedInstantiatedType_Expand()
    {
        const string source = """
            type Box<T> = T
            let x: Box<Box<number>> = 42
            x
            """;

        var type = Utility.GetLastStatementType(source);
        Assert.True(type.Equals(PrimitiveType.Number), $"Expected 'number', got '{type}'");
    }

    [Fact]
    public void Checks_Inference_FromNoneArgument_OptionalParameter()
    {
        const string source = """
            fn id<T>(x: T?) -> x
            id(none)
            """;

        var type = Utility.GetLastStatementType(source);
        Assert.True(Type.IsNone(type), $"Expected 'none', got '{type}'");
    }

    [Fact]
    public void Checks_Substitution_DeepFunctionTypes()
    {
        const string source = """
            fn compose<T, U>(f: fn(p: T): U, g: fn(p: U): T): fn(p: T): T -> f;
            fn a(x: number): string -> "";
            fn b(s: string): number -> 0;
            compose(a, b);
            """;

        var type = Utility.GetLastStatementType(source);
        var fnType = Assert.IsType<FunctionType>(type);
        Assert.True(fnType.ReturnType.Equals(PrimitiveType.Number));
    }

    [Fact]
    public void Checks_GenericFunction_TypeParameterUsedInArrayType()
    {
        const string source = """
            fn first<T>(arr: T[]) -> arr[0]
            first(["hello", "world"])
            """;

        var type = Utility.GetLastStatementType(source);
        Assert.True(type.Equals(PrimitiveType.String), $"Expected 'string', got '{type}'");
    }

    [Fact]
    public void Checks_GenericInterface_IndexerInferenceWithSameTypeParameter()
    {
        const string source = """
            interface Duo<T> { first: T, [number]: T }
            new Duo { first: 42, [0]: 99 }
            """;

        var result = Utility.TypeCheck(source);
        Utility.AssertNoErrors(result);
        var iface = Assert.IsType<InterfaceType>(result.ReturnType);
        var prop = iface.ObjectType.GetProperty("first")!;
        Assert.True(prop.ValueType.Equals(PrimitiveType.Number));
        var indexer = iface.ObjectType.Indexer!;
        Assert.True(indexer.ValueType.Equals(PrimitiveType.Number));
    }

    [Fact]
    public void Checks_GenericConstraint_IntersectionType()
    {
        const string source = """
            interface A { a: number }
            interface B { b: string }
            fn merge<T: A & B>(obj: T): T -> obj
            merge(none as never as (A & B))
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Checks_KeyOf_OnGenericInstantiation()
    {
        const string source = """
            interface Box<T> { value: T }
            type K = keyof(Box<number>)
            """;

        var type = Utility.GetLastStatementType(source);
        var literal = Assert.IsType<LiteralType>(type);
        Assert.Equal("value", literal.Value);
    }

    [Fact]
    public void Checks_Inference_ThroughAliasedGenericParameter()
    {
        const string source = """
            type NumList = number[]
            fn head<T>(list: T[]) -> list[0]
            head([1, 2, 3] as NumList)
            """;

        var type = Utility.GetLastStatementType(source);
        Assert.True(type.Equals(PrimitiveType.Number), $"Expected 'number', got '{type}'");
    }

    [Fact]
    public void Checks_InterfaceInvocation_OnlyIndexer()
    {
        const string source = """
            interface StrNum { [string]: number }
            new StrNum { ["key"]: 42 }
            """;

        var result = Utility.TypeCheck(source);
        Utility.AssertNoErrors(result);
    }

    [Fact]
    public void Checks_Inference_GenericFunctionTypeParameter()
    {
        const string source = """
            fn apply<T>(f: fn(): T): T -> f()
            fn getAnswer -> 42
            apply(getAnswer)
            """;

        var type = Utility.GetLastStatementType(source);
        var literal = Assert.IsType<LiteralType>(type);
        Assert.Equal(42L, literal.Value);
    }

    [Fact]
    public void Checks_InstantiatedType_WithUnknownArgument()
    {
        const string source = """
            type Box<T> = T
            let x: Box<unknown> = 1
            x
            """;

        var type = Utility.GetLastStatementType(source);
        Assert.True(Type.IsUnknown(type), $"Expected 'unknown', got '{type}'");
    }

    [Fact]
    public void Checks_InstantiatedType_WithNeverArgument()
    {
        const string source = """
            type Box<T> = T
            let x: Box<never> = none as never
            x
            """;

        var type = Utility.GetLastStatementType(source);
        Assert.True(Type.IsNever(type), $"Expected 'never', got '{type}'");
    }

    [Fact]
    public void Checks_Inference_OmittedOptionalArgument()
    {
        const string source = """
            fn wrap<T>(a: T, b: T? = none) -> a
            wrap(42)
            """;

        var type = Utility.GetLastStatementType(source);
        var literal = Assert.IsType<LiteralType>(type);
        Assert.Equal(42L, literal.Value);
    }

    [Fact]
    public void Checks_Inference_DeeplyNestedObjectParameter()
    {
        const string source = """
            interface B<T> { b: T }
            interface A<T> { a: B<T> }
            fn foo<T>(x: A<T>) -> x.a.b;
            foo(new A { a: new B { b: 42 } });
            """;

        var type = Utility.GetLastStatementType(source);
        var literal = Assert.IsType<LiteralType>(type);
        Assert.Equal(42L, literal.Value);
    }

    [Fact]
    public void Checks_GenericConstraint_UsingInstantiatedType()
    {
        const string source = """
            type Box<T> = T
            type Container<T: Box<number>> = T
            let x: Container<number> = 42
            x
            """;

        var type = Utility.GetLastStatementType(source);
        Assert.Equal(PrimitiveType.Number, type);
    }

    [Fact]
    public void Checks_TypeAlias_ChainExpansion()
    {
        const string source = """
            type B = number;
            type A = B;
            let x: A = 1;
            x
            """;

        var type = Utility.GetLastStatementType(source);
        Assert.True(type.Equals(PrimitiveType.Number), $"Expected 'number', got '{type}'");
    }

    [Fact]
    public void Checks_Inference_ReturnTypeOnlyTypeParameterUsesDefault()
    {
        const string source = "fn create<T = number>(): T -> 42; create()";
        var type = Utility.GetLastStatementType(source);
        Assert.True(type.IsAssignableTo(PrimitiveType.Number));
    }

    [Fact]
    public void Checks_Inference_GenericInterfaceInvocationPropertyTypes()
    {
        const string source = "interface Box<T> { value: T }; new Box { value: 42 }";
        var type = Utility.GetLastStatementType(source);
        var iface = Assert.IsType<InterfaceType>(type);
        var prop = iface.ObjectType.Properties.Single();
        Assert.True(prop.ValueType.IsAssignableTo(PrimitiveType.Number));
    }

    [Fact]
    public void Checks_Inference_GenericInterfaceInvocationIndexerTypes()
    {
        const string source = "interface Map<K, V> { [K]: V }; new Map { [\"a\"]: 1 }";
        var type = Utility.GetLastStatementType(source);
        var iface = Assert.IsType<InterfaceType>(type);
        Assert.NotNull(iface.ObjectType.Indexer);
        Assert.True(iface.ObjectType.Indexer.KeyType.IsAssignableTo(PrimitiveType.String));
        Assert.True(iface.ObjectType.Indexer.ValueType.IsAssignableTo(PrimitiveType.Number));
    }

    [Fact]
    public void Checks_Inference_GenericInterfaceWithDefaultParameters()
    {
        const string source = "interface I<T = number> { value: T }; new I { value: 42 }";
        var type = Utility.GetLastStatementType(source);
        var iface = Assert.IsType<InterfaceType>(type);
        Assert.True(iface.ObjectType.Properties.Single().ValueType.IsAssignableTo(PrimitiveType.Number));
    }

    [Fact]
    public void Checks_Inference_AliasExpansionInParameter()
    {
        const string source = "type Num = number; fn id<T>(x: T): T -> x; id(42)";
        var type = Utility.GetLastStatementType(source);
        var literal = Assert.IsType<LiteralType>(type);
        Assert.Equal(42L, literal.Value);
    }

    [Fact]
    public void Checks_Inference_AliasExpansionInArgument()
    {
        const string source = "type Num = number; fn id<T>(x: T): T -> x; let n: Num = 42; id(n)";
        var type = Utility.GetLastStatementType(source);
        Assert.True(type.IsAssignableTo(PrimitiveType.Number));
    }

    [Fact]
    public void Checks_Inference_TypeParameterCapturesFullUnionArgument()
    {
        const string source = "fn id<T>(x: T): T -> x; id(42 as (number | string))";
        var type = Utility.GetLastStatementType(source);
        var union = Assert.IsType<UnionType>(type);
        Assert.Equal(2, union.Types.Count);
        Assert.Contains(union.Types, t => t.Equals(PrimitiveType.Number));
        Assert.Contains(union.Types, t => t.Equals(PrimitiveType.String));
    }

    [Fact]
    public void Checks_Inference_TypeParameterCapturesFunctionType()
    {
        const string source = "fn apply<T>(f: fn(): T): T -> f(); fn do -> 42; apply(do)";
        var type = Utility.GetLastStatementType(source);
        var literal = Assert.IsType<LiteralType>(type);
        Assert.Equal(42L, literal.Value);
    }

    [Fact]
    public void Checks_Inference_MultipleParametersWithSharedTypeParameter()
    {
        const string source = "fn both<T>(a: T, b: T): T -> a; both(42, 69)";
        var type = Utility.GetLastStatementType(source);
        Assert.True(type.IsAssignableTo(PrimitiveType.Number));
    }

    [Fact]
    public void Checks_Inference_IntersectionParameterSingleTypeParameter()
    {
        const string source = "fn id<T>(x: T & number): T & number -> x; id(42)";
        var type = Utility.GetLastStatementType(source);
        Assert.True(type.IsAssignableTo(PrimitiveType.Number));
    }

    [Fact]
    public void Checks_Inference_NestedGenerics()
    {
        const string source = "type List<T> = T[]; fn first<T>(list: List<T>): T -> list[0]; first([1, 2, 3])";
        var type = Utility.GetLastStatementType(source);
        Assert.True(type.Equals(PrimitiveType.Number));
    }

    [Fact]
    public void Checks_Inference_GenericTypeWithDefaultParameter()
    {
        const string source = "type Container<T = number> = T; fn wrap<T = Container>(value: T): T -> value; wrap(42)";
        var type = Utility.GetLastStatementType(source);
        Assert.True(type.IsAssignableTo(PrimitiveType.Number));
    }

    [Fact]
    public void Checks_Inference_ArrayOfGenericElement()
    {
        const string source = "fn head<T>(arr: T[]): T -> arr[0]; head([true, false])";
        var type = Utility.GetLastStatementType(source);
        Assert.True(type.Equals(PrimitiveType.Bool));
    }

    [Fact]
    public void Checks_Inference_OptionalParameterWithNonNullableArgument()
    {
        const string source = "fn unwrap<T>(x: T?): T -> x as never as T; unwrap(42)";
        var type = Utility.GetLastStatementType(source);
        var literal = Assert.IsType<LiteralType>(type);
        Assert.Equal(42L, literal.Value);
    }

    [Fact]
    public void Checks_Inference_OptionalParameterWithNullableArgument()
    {
        const string source = "fn id<T>(x: T?): T? -> x; let v: number? = 42; id(v)";
        var type = Utility.GetLastStatementType(source);
        var optional = Assert.IsType<OptionalType>(type);
        Assert.True(optional.NonNullableType.Equals(PrimitiveType.Number));
    }

    [Fact]
    public void Checks_Inference_UnionParameterSingleTypeParameter()
    {
        const string source = "fn id<T>(x: T | string): T | string -> x; id(42)";
        var type = Utility.GetLastStatementType(source);
        var union = Assert.IsType<UnionType>(type);
        Assert.Equal(2, union.Types.Count);
        Assert.Contains(union.Types, t => t is LiteralType { Value: 42L });
        Assert.Contains(union.Types, t => t.Equals(PrimitiveType.String));
    }

    [Fact]
    public void Checks_FunctionWithEmptyBody()
    {
        const string source = """
            fn foo() {}
            foo()
            """;

        var type = Utility.GetLastStatementType(source);
        Assert.True(type.Equals(PrimitiveType.Void), $"Expected 'void', got '{type}'");
    }

    [Fact]
    public void Checks_FunctionWithBlockNoReturn()
    {
        const string source = """
            fn bar() { let x = 1; }
            bar()
            """;

        var type = Utility.GetLastStatementType(source);
        Assert.True(type.Equals(PrimitiveType.Void), $"Expected 'void', got '{type}'");
    }

    [Fact]
    public void Checks_InterfaceInvocationWithPropertyAndIndexInitializers()
    {
        const string source = """
            interface I { x: number, [string]: bool }
            new I { x: 1, ["key"]: true }
            """;

        var result = Utility.TypeCheck(source);
        Utility.AssertNoErrors(result);

        var iface = Assert.IsType<InterfaceType>(result.ReturnType);
        Assert.Equal("I", iface.Name);
    }

    [Fact]
    public void Checks_GenericInterfaceWithConstraint_Valid()
    {
        var result = Utility.TypeCheck("interface I<T: number> { value: T }; new I::<number> { value: 42 }");
        Utility.AssertNoErrors(result);
    }

    [Fact]
    public void Checks_IndexedTypeWithUnionOfStringLiterals()
    {
        const string source = """
            interface I { a: number; b: string }
            type K = "a" | "b"
            type V = I[K]
            let x: V = 42
            x
            """;

        var type = Utility.GetLastStatementType(source);
        var union = Assert.IsType<UnionType>(type);
        Assert.Equal(2, union.Types.Count);
        Assert.Contains(union.Types, t => t.Equals(PrimitiveType.Number));
        Assert.Contains(union.Types, t => t.Equals(PrimitiveType.String));
    }

    [Fact]
    public void Checks_CastToUnionType()
    {
        var type = Utility.GetLastStatementType("69 as (number | string)");
        var union = Assert.IsType<UnionType>(type);
        Assert.Equal(2, union.Types.Count);
        Assert.Contains(union.Types, t => t.Equals(PrimitiveType.Number));
        Assert.Contains(union.Types, t => t.Equals(PrimitiveType.String));
    }

    [Theory]
    [InlineData(
        """
        fn abc(x: number?) {
            if x == none return;
            x + 69;
        }
        """
    )]
    [InlineData(
        """
        fn abc(x: number?) {
            if x == none { return; }
            x + 69;
        }
        """
    )]
    [InlineData(
        """
        fn abc {
            let x = none as never as Result<number, string>;
            if !x.ok return;
            x.value;
        }
        """
    )]
    [InlineData(
        """
        fn abc {
            let x = none as never as Result<number, string>;
            if x.ok {
                x.value;
                return;
            }
            
            x.error
        }
        """
    )]
    public void Checks_NarrowingAfter_EarlyReturn(string source)
    {
        var diagnostics = Utility.TypeCheck(source).Diagnostics;
        Utility.AssertNoErrors(diagnostics);
    }

    [Theory]
    [InlineData(
        """
        mut x: number?;
        while true {
            if x == none break;
            x + 69;
        }
        x;
        """
    )]
    [InlineData(
        """
        let x: number?[] = [];
        for n : x {
            if n == none { break }
            n + 69;
        }
        """
    )]
    [InlineData(
        """
        let x = Result.ok(69);
        while true {
            if !x.ok break;
            x.value;
        }
        """
    )]
    public void Checks_NarrowingAfter_LoopBreakGuard(string source)
    {
        var diagnostics = Utility.TypeCheck(source).Diagnostics;
        Utility.AssertNoErrors(diagnostics);
    }

    [Theory]
    [InlineData(
        """
        mut x: number?;
        while true {
            if x == none { continue; }
            x + 69;
        }
        x;
        """
    )]
    [InlineData(
        """
        let x = Result.ok(69);
        while true {
            if x.ok continue;
            x.error;
        }
        """
    )]
    [InlineData(
        """
        let x: number?[] = [];
        for n : x {
            if n == none continue;
            n + 69;
        }
        """
    )]
    public void Checks_NarrowingAfter_LoopContinueGuard(string source)
    {
        var diagnostics = Utility.TypeCheck(source).Diagnostics;
        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Checks_Narrowing_ElementAccessWithLiteralIndex()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let arr = [1, 2, 3]; if arr[0] == 1 { arr[0] + 1 }");
        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Checks_Narrowing_WhileWithPropertyAccess()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            "interface HasVal { val: number? }; let obj = none as never as HasVal; while obj.val != none { obj.val + 1; break; }"
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Checks_Narrowing_NonOptionalComparedToNone()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let x = 42; if x == none { 1 }");
        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Checks_GenericFunction_InferenceWithIntersectionParameter_AndSingleArgument()
    {
        const string source = """
            fn id<T>(a: T & number) -> a
            id(42)
            """;

        var type = Utility.GetLastStatementType(source);
        var literal = Assert.IsType<LiteralType>(type);
        Assert.Equal(42L, literal.Value);
    }

    [Fact]
    public void Checks_GenericFunction_InferenceWithUnionParameter_AndSingleArgument()
    {
        const string source = """
            fn id<T>(a: T | string) -> a
            id(42)
            """;

        var type = Utility.GetLastStatementType(source);
        var union = Assert.IsType<UnionType>(type);
        Assert.Equal(2, union.Types.Count);
        Assert.Equal(42L, Assert.IsType<LiteralType>(union.Types[0]).Value);
        Assert.Equal(PrimitiveTypeKind.String, Assert.IsType<PrimitiveType>(union.Types[1]).Kind);
    }

    [Fact]
    public void Checks_InterfaceInvocation_InferenceFromUnionProperty()
    {
        const string source = """
            interface I<T> { value: T | string }
            new I { value: 42 }
            """;

        var result = Utility.TypeCheck(source);
        Utility.AssertNoErrors(result);
        var iface = Assert.IsType<InterfaceType>(result.ReturnType);
        var prop = iface.ObjectType.Properties.Single();
        var union = Assert.IsType<UnionType>(prop.ValueType);
        Assert.Contains(union.Types, t => t is LiteralType { Value: 42L });
    }

    [Fact]
    public void Checks_GenericFunction_InferenceFromGenericTypeAliasWithMultipleParameters()
    {
        const string source = """
            interface ResultThing<T, E> { ok: T, err: E }
            fn unwrap<T, E>(r: ResultThing<T, E>) -> r.ok
            unwrap(new ResultThing { ok: 1, err: "oops" })
            """;

        var type = Utility.GetLastStatementType(source);
        var literal = Assert.IsType<LiteralType>(type);
        Assert.Equal(1L, literal.Value);
    }

    [Fact]
    public void Checks_TypeParameterWithConstraintAndDefault_Valid()
    {
        const string source = """
            type Id<T: number = 42> = T
            let x: Id = 42
            x
            """;

        var type = Utility.GetLastStatementType(source);
        var literal = Assert.IsType<LiteralType>(type);
        Assert.Equal(42L, literal.Value);
    }

    [Fact]
    public void Checks_ForLoopOverObjectValues()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("interface Data { a: number; b: string } for v : new Data { a: 1, b: 'hi' } { v }");
        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Checks_UnaryPlusOperator()
    {
        var type = Utility.GetLastStatementType("+5");
        var literal = Assert.IsType<LiteralType>(type);
        Assert.Equal(5L, literal.Value);
    }

    [Fact]
    public void Checks_KeyOf_OnObjectType_WithProperties()
    {
        var type = Utility.GetLastStatementType("interface I { a: number, b: string } type K = keyof(I)");
        var union = Assert.IsType<UnionType>(type);
        Assert.Equal(2, union.Types.Count);
        Assert.Contains(union.Types, t => t is LiteralType { Value: "a" });
        Assert.Contains(union.Types, t => t is LiteralType { Value: "b" });
    }

    [Fact]
    public void Checks_KeyOf_OnObjectType_WithIndexerOnly()
    {
        var type = Utility.GetLastStatementType("interface I { [number]: string } type K = keyof(I)");
        Assert.Equal(PrimitiveType.Number, type);
    }

    [Fact]
    public void Checks_KeyOf_OnObjectType_WithPropertiesAndIndexer()
    {
        var type = Utility.GetLastStatementType("interface I { a: number, [number]: bool } type K = keyof(I)");
        var union = Assert.IsType<UnionType>(type);
        Assert.Equal(2, union.Types.Count);
        Assert.Contains(union.Types, t => t is LiteralType { Value: "a" });
        Assert.Contains(union.Types, t => t.Equals(PrimitiveType.Number));
    }

    [Fact]
    public void Checks_KeyOf_OnInterface_ResolvesObjectType()
    {
        var type = Utility.GetLastStatementType("interface I { x: number, y: number } type K = keyof(I)");
        var union = Assert.IsType<UnionType>(type);
        Assert.Equal(2, union.Types.Count);
        Assert.Contains(union.Types, t => t is LiteralType { Value: "x" });
        Assert.Contains(union.Types, t => t is LiteralType { Value: "y" });
    }

    [Fact]
    public void Checks_KeyOf_OnGenericInterface_Instantiated()
    {
        var type = Utility.GetLastStatementType("interface Box<T> { value: T } type K = keyof(Box<number>)");
        var literal = Assert.IsType<LiteralType>(type);
        Assert.Equal("value", literal.Value);
    }

    [Fact]
    public void Checks_KeyOf_OnNestedObject_ReturnsNestedKeys()
    {
        var type = Utility.GetLastStatementType("interface Inner { a: number, b: string } interface Outer { inner: Inner } type K = keyof(Outer['inner'])");
        var union = Assert.IsType<UnionType>(type);
        Assert.Equal(2, union.Types.Count);
        Assert.Contains(union.Types, t => t is LiteralType { Value: "a" });
        Assert.Contains(union.Types, t => t is LiteralType { Value: "b" });
    }

    [Fact]
    public void Checks_TypeOf_OnNumberLiteral()
    {
        var type = Utility.GetLastStatementType("type X = typeof(69)");
        Assert.Equal(new LiteralType(69L), type);
    }

    [Fact]
    public void Checks_TypeOf_OnStringLiteral()
    {
        var type = Utility.GetLastStatementType("type X = typeof(\"hi\")");
        Assert.Equal(new LiteralType("hi"), type);
    }

    [Fact]
    public void Checks_TypeOf_OnMutVariable_Widens()
    {
        var type = Utility.GetLastStatementType("mut x = 69; type X = typeof(x)");
        Assert.Equal(PrimitiveType.Number, type);
    }

    [Fact]
    public void Checks_TypeOf_OnBinaryExpression()
    {
        var type = Utility.GetLastStatementType("mut x = 1; type X = typeof(x + 1)");
        Assert.Equal(PrimitiveType.Number, type);
    }

    [Fact]
    public void Checks_TypeOf_OnInterfaceInstance()
    {
        var type = Utility.GetLastStatementType(
            """
            interface Foo {
                foo: number,
                bar: string
            }

            let foo = new Foo { foo: 69, bar: "hi" };
            type X = typeof(foo);
            """
        );

        var interfaceType = Assert.IsType<InterfaceType>(type);
        Assert.Equal("Foo", interfaceType.Name);
    }

    [Fact]
    public void Checks_KeyOf_OnTypeOf()
    {
        var type = Utility.GetLastStatementType(
            """
            interface I { a: number, b: string }
            let i = new I { a: 1, b: "x" };
            type K = keyof(typeof(i));
            """
        );

        var union = Assert.IsType<UnionType>(type);
        Assert.Equal(2, union.Types.Count);
        Assert.Contains(union.Types, t => t is LiteralType { Value: "a" });
        Assert.Contains(union.Types, t => t is LiteralType { Value: "b" });
    }

    [Fact]
    public void Checks_TernaryOperator_Basic()
    {
        var type = Utility.GetLastStatementType("true ? 1 : 2");
        var union = Assert.IsType<UnionType>(type);
        Assert.Equal(2, union.Types.Count);
        Assert.Contains(union.Types, t => t is LiteralType { Value: 1L });
        Assert.Contains(union.Types, t => t is LiteralType { Value: 2L });
    }

    [Fact]
    public void Checks_TernaryOperator_ConditionIsBoolIdentifier()
    {
        var type = Utility.GetLastStatementType("let cond = true; cond ? 1 : 2");
        var union = Assert.IsType<UnionType>(type);
        Assert.Equal(2, union.Types.Count);
    }

    [Fact]
    public void Checks_TernaryOperator_ConditionIsComparison()
    {
        var type = Utility.GetLastStatementType("let x = 5; x > 3 ? 'yes' : 'no'");
        var union = Assert.IsType<UnionType>(type);
        Assert.Equal(2, union.Types.Count);
        Assert.Contains(union.Types, t => t is LiteralType { Value: "yes" });
        Assert.Contains(union.Types, t => t is LiteralType { Value: "no" });
    }

    [Fact]
    public void Checks_TernaryOperator_BranchesSameType()
    {
        var type = Utility.GetLastStatementType("true ? 42 : 69");
        var union = Assert.IsType<UnionType>(type);
        Assert.Equal(2, union.Types.Count);
        Assert.Contains(union.Types, t => t is LiteralType { Value: 42L });
        Assert.Contains(union.Types, t => t is LiteralType { Value: 69L });
    }

    [Fact]
    public void Checks_TernaryOperator_Nested()
    {
        var type = Utility.GetLastStatementType("true ? 1 : false ? 2 : 3");
        var union = Assert.IsType<UnionType>(type);
        Assert.Equal(3, union.Types.Count);
        Assert.Contains(union.Types, t => t is LiteralType { Value: 1L });
        Assert.Contains(union.Types, t => t is LiteralType { Value: 2L });
        Assert.Contains(union.Types, t => t is LiteralType { Value: 3L });
    }

    [Fact]
    public void Checks_TernaryOperator_WithNarrowing_NonNullable()
    {
        var type = Utility.GetLastStatementType("let x: number? = 5; x != none ? x + 1 : 0");
        var primitive = Assert.IsType<PrimitiveType>(type);
        Assert.Equal(PrimitiveTypeKind.Number, primitive.Kind);
    }

    [Fact]
    public void Checks_TernaryOperator_WithNarrowing_EqualsLiteral()
    {
        var type = Utility.GetLastStatementType("let x: number | string = 42; x == 42 ? x + 1 : x");
        var union = Assert.IsType<UnionType>(type);
        Assert.Equal(2, union.Types.Count);
        Assert.Contains(union.Types, t => t.Equals(PrimitiveType.Number));
        Assert.Contains(union.Types, t => t.Equals(PrimitiveType.String));
    }

    [Fact]
    public void Checks_TernaryOperator_WithNarrowing_NoneCheck()
    {
        var type = Utility.GetLastStatementType("let x: number? = none; x == none ? 0 : x + 1");
        var primitive = Assert.IsType<PrimitiveType>(type);
        Assert.Equal(PrimitiveTypeKind.Number, primitive.Kind);
    }

    [Fact]
    public void Checks_TernaryOperator_WithNarrowing_NotNoneCheck()
    {
        var type = Utility.GetLastStatementType("let x: number? = 5; x != none ? x + 1 : 0");
        var primitive = Assert.IsType<PrimitiveType>(type);
        Assert.Equal(PrimitiveTypeKind.Number, primitive.Kind);
    }

    [Fact]
    public void Checks_TernaryOperator_WithNeverBranch()
    {
        var type = Utility.GetLastStatementType("true ? 1 : (2 as never)");
        var literal = Assert.IsType<LiteralType>(type);
        Assert.Equal(1L, literal.Value);
    }

    [Fact]
    public void Checks_TernaryOperator_InAssignment()
    {
        var type = Utility.GetLastStatementType("let z = true ? 1 : 'a'");
        var union = Assert.IsType<UnionType>(type);
        Assert.Equal(2, union.Types.Count);
        Assert.Contains(union.Types, t => t is LiteralType { Value: 1L });
        Assert.Contains(union.Types, t => t is LiteralType { Value: "a" });
    }

    [Fact]
    public void Checks_TernaryOperator_AsFunctionArgument()
    {
        const string source = """
                    fn foo(x: number | string) -> x
                    foo(true ? 1 : 'a')
            """;

        var type = Utility.GetLastStatementType(source);
        var union = Assert.IsType<UnionType>(type);
        Assert.Equal(2, union.Types.Count);
        Assert.Contains(union.Types, t => t.Equals(PrimitiveType.String));
        Assert.Contains(union.Types, t => t.Equals(PrimitiveType.Number));
    }

    [Fact]
    public void Checks_TernaryOperator_WithOptionalConditionNarrowed()
    {
        var type = Utility.GetLastStatementType("let b: bool? = true; b == true ? 1 : 2");
        var union = Assert.IsType<UnionType>(type);
        Assert.Equal(2, union.Types.Count);
    }

    [Fact]
    public void Checks_After_PropagatesBodyType()
    {
        var type = Utility.GetLastStatementType("after 1 { 42 }");
        var literal = Assert.IsType<LiteralType>(type);
        Assert.Equal(42L, literal.Value);
    }

    [Fact]
    public void Checks_If_UnionOfBranches()
    {
        var type = Utility.GetLastStatementType("if true { 1 } else { \"hi\" }");
        var union = Assert.IsType<UnionType>(type);
        Assert.Equal(2, union.Types.Count);
        Assert.Contains(union.Types, t => t is LiteralType { Value: 1L });
        Assert.Contains(union.Types, t => t is LiteralType { Value: "hi" });
    }

    [Fact]
    public void Checks_Function_NoParameters_InferredReturn()
    {
        const string source = """
            fn answer -> 42
            answer()
            """;

        var type = Utility.GetLastStatementType(source);
        var literal = Assert.IsType<LiteralType>(type);
        Assert.Equal(42L, literal.Value);
    }

    [Fact]
    public void Checks_ParameterDefault_Used()
    {
        const string source = """
            fn greet(name: string = "world") -> name
            greet()
            """;

        var type = Utility.GetLastStatementType(source);
        Assert.True(type.Equals(PrimitiveType.String), $"Expected 'string', got '{type}'");
    }

    [Fact]
    public void Checks_Narrowing_EnumMemberEquals()
    {
        const string source = """
            enum Status { Active, Inactive }
            let x: Status = Status.Active
            if x == Status.Active { x }
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Checks_Narrowing_NameOfEquals()
    {
        const string source = """
            let x = nameof(x)
            if x == "x" { x }
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Checks_AssignmentToMutableIndexer()
    {
        const string source = """
            interface Store { mut [string]: number }
            let store = none as never as Store
            store["key"] = 42
            """;

        var result = Utility.TypeCheck(source);
        Utility.AssertNoErrors(result);
        var literal = Assert.IsType<LiteralType>(result.ReturnType);
        Assert.Equal(42L, literal.Value);
    }

    [Fact]
    public void Checks_While_PropagatesBodyType()
    {
        var type = Utility.GetLastStatementType("while true { 42; break }");
        Assert.True(Type.IsNone(type), $"Expected 'void', got '{type}'");
    }

    [Fact]
    public void Checks_After_PropagatesBodyType_WithExpression()
    {
        var type = Utility.GetLastStatementType("after 1 { \"done\" }");
        Assert.True(type.Equals(new LiteralType("done")), $"Expected 'string', got '{type}'");
    }

    [Fact]
    public void Checks_InterfaceInheritance_TwoLevels()
    {
        const string source = """
            interface A { x: number }
            interface B : A { }
            interface C : B { }
            let obj = none as never as C
            obj.x
            """;

        var type = Utility.GetLastStatementType(source);
        Assert.True(type.Equals(PrimitiveType.Number), $"Expected 'number', got '{type}'");
    }

    [Fact]
    public void Checks_ForLoop_OverArray_BreakInside()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("for x : [1, 2] { break }");
        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Checks_ForLoop_OverArray_ContinueInside()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("for x : [1, 2] { continue }");
        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Checks_ForLoop_OverArray_Nested()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let matrix = [[1, 2], [3, 4]]; for row : matrix { for elem : row { elem } }");
        Utility.AssertNoErrors(diagnostics);
    }

    [Theory]
    [InlineData("for x : [1, 2, 3] { x }")]
    [InlineData("for x : 1..10 { x }")]
    public void Checks_ForLoop_ElementType(string source)
    {
        var type = Utility.GetLastStatementType(source);
        var element = Assert.IsType<PrimitiveType>(type);
        Assert.Equal(PrimitiveTypeKind.Number, element.Kind);
    }

    [Fact]
    public void Checks_NullCoalescing_TwoOptionalOperands()
    {
        var type = Utility.GetLastStatementType("let a: number? = 1; let b: string? = 'hi'; a ?? b");
        var union = Assert.IsType<UnionType>(type);
        Assert.Equal(2, union.Types.Count);
        Assert.Contains(union.Types, t => t.Equals(PrimitiveType.Number));
        Assert.Contains(union.Types, t => t.Equals(PrimitiveType.String));
    }

    [Fact]
    public void Checks_GenericFunctionCall_ZeroArgumentsExplicit()
    {
        const string source = """
            fn nothing<T>() -> 42
            nothing::<number>()
            """;

        var type = Utility.GetLastStatementType(source);
        var literal = Assert.IsType<LiteralType>(type);
        Assert.Equal(42L, literal.Value);
    }

    [Fact]
    public void Checks_DeclareGenericInterface()
    {
        var type = Utility.GetLastStatementType("declare interface Box<T> { value: T }");
        var generic = Assert.IsType<GenericType>(type);
        Assert.Single(generic.Parameters);
        Assert.Equal("T", generic.Parameters[0].Name);
    }

    [Fact]
    public void Checks_Function_ExplicitReturnTypeOverridesInference()
    {
        var type = Utility.GetLastStatementType("fn double(x: number): number -> x * 2");
        var functionType = Assert.IsType<FunctionType>(type);
        Assert.True(functionType.ReturnType.Equals(PrimitiveType.Number), $"Expected 'number', got '{functionType.ReturnType}'");
    }

    [Fact]
    public void Checks_AssignmentToMutableInterfaceProperty()
    {
        const string source = """
            interface Mutable { mut value: number }
            let x = none as never as Mutable
            x.value = 42
            """;

        var result = Utility.TypeCheck(source);
        Utility.AssertNoErrors(result);
        var assignmentType = result.ReturnType;
        var literal = Assert.IsType<LiteralType>(assignmentType);
        Assert.Equal(42L, literal.Value);
    }

    [Fact]
    public void Checks_TypeAlias_PartialTypeArguments()
    {
        const string source = """
            type Pair<T, U = number> = T
            type X = Pair<string>
            let x: X = "hello"
            x
            """;

        var type = Utility.GetLastStatementType(source);
        Assert.True(type.Equals(PrimitiveType.String), $"Expected 'string', got '{type}'");
    }

    [Fact]
    public void Checks_FunctionType_WithTypeParameterDefault()
    {
        var type = Utility.GetLastStatementType("let f: fn<T = number>(x: T): T;");
        var functionType = Assert.IsType<FunctionType>(type);
        Assert.Single(functionType.TypeParameters);
        var tp = functionType.TypeParameters[0];
        Assert.Equal("T", tp.Name);
        Assert.NotNull(tp.DefaultType);
        Assert.True(tp.DefaultType.Equals(PrimitiveType.Number), $"Expected default 'number', got '{tp.DefaultType}'");
    }

    [Fact]
    public void Checks_InterfaceDeclaration_GenericWithDefault()
    {
        var type = Utility.GetLastStatementType("interface I<T = number> { value: T }");
        var generic = Assert.IsType<GenericType>(type);
        Assert.Single(generic.Parameters);
        var tp = generic.Parameters[0];
        Assert.Equal("T", tp.Name);
        Assert.NotNull(tp.DefaultType);
        Assert.True(tp.DefaultType.Equals(PrimitiveType.Number), $"Expected default 'number', got '{tp.DefaultType}'");
    }

    [Fact]
    public void Checks_Narrowing_While_NonNullable()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let x: number? = 69; while x != none { x + 420; break; }");
        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Checks_Narrowing_While_EqualsLiteral()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let x: number | string = 42; while x == 42 { x + 1; break; }");
        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Checks_Narrowing_While_NotEqualsLiteral()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let x: number | string = 'hi'; while x != 'hi' { x + 1; break; }");
        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Checks_WhileLoop_WithBreak()
    {
        var result = Utility.TypeCheck("while true { break }");
        Utility.AssertNoErrors(result);
    }

    [Fact]
    public void Checks_WhileLoop_WithContinue()
    {
        var result = Utility.TypeCheck("while true { continue }");
        Utility.AssertNoErrors(result);
    }

    [Fact]
    public void Checks_InterfaceInvocation_ChainedPropertyAccess()
    {
        var type = Utility.GetLastStatementType("interface I { x: number } new I { x: 1 }.x");
        Assert.True(type.Equals(PrimitiveType.Number), $"Expected 'number', got '{type}'");
    }

    [Fact]
    public void Checks_InterfaceInvocation_NonGeneric_PropertyInitializers()
    {
        var type = Utility.GetLastStatementType("interface I { x: number, y: string } new I { x: 42, y: 'hello' }");
        var iface = Assert.IsType<InterfaceType>(type);
        Assert.Equal("I", iface.Name);
    }

    [Fact]
    public void Checks_InterfaceInvocation_NonGeneric_ShorthandPropertyInitializers()
    {
        var type = Utility.GetLastStatementType("interface I { x: number, y: string } let x = 42; let y = 'hello'; new I { x, y }");
        var iface = Assert.IsType<InterfaceType>(type);
        Assert.Equal("I", iface.Name);
    }

    [Fact]
    public void Checks_InterfaceInvocation_NonGeneric_IndexInitializer()
    {
        var type = Utility.GetLastStatementType("interface I { [string]: number } new I { ['key']: 42 }");
        var iface = Assert.IsType<InterfaceType>(type);
        Assert.Equal("I", iface.Name);
    }

    [Fact]
    public void Checks_InterfaceInvocation_Generic_ExplicitTypeArgs()
    {
        var type = Utility.GetLastStatementType("interface I<T> { value: T } new I::<number> { value: 42 }");
        var iface = Assert.IsType<InterfaceType>(type);
        Assert.Equal("I", iface.Name);
        var prop = iface.ObjectType.Properties.Single();
        Assert.True(prop.ValueType.Equals(PrimitiveType.Number));
    }

    [Fact]
    public void Checks_InterfaceInvocation_Generic_DefaultTypeArgs()
    {
        var result = Utility.TypeCheck("interface I<T = number> { value: T } new I { value: 42 }");
        Utility.AssertNoErrors(result);

        var iface = Assert.IsType<InterfaceType>(result.ReturnType);
        Assert.Equal("I", iface.Name);
    }

    [Fact]
    public void Checks_InterfaceInheritance_MemberAccess()
    {
        var type = Utility.GetLastStatementType("interface A { x: number } interface B : A { } let b = none as never as B; b.x");
        Assert.True(type.Equals(PrimitiveType.Number), $"Expected 'number', got '{type}'");
    }

    [Fact]
    public void Checks_InterfaceInheritance_IndexerAccess()
    {
        var type = Utility.GetLastStatementType("interface A { [string]: number } interface B : A { } let b = none as never as B; b['hello']");
        Assert.True(type.Equals(PrimitiveType.Number), $"Expected 'number', got '{type}'");
    }

    [Fact]
    public void Checks_CompoundAssignment_Add()
    {
        var type = Utility.GetLastStatementType("mut x = 1; x += 2");
        Assert.True(type.IsAssignableTo(PrimitiveType.Number), $"Expected 'number', got '{type}'");
    }

    [Fact]
    public void Checks_CompoundAssignment_Concat()
    {
        var type = Utility.GetLastStatementType("mut s = 'a'; s += 'b'");
        Assert.True(type.IsAssignableTo(PrimitiveType.String), $"Expected 'string', got '{type}'");
    }

    [Fact]
    public void Checks_ReturnTypeInference_BlockMultipleReturns()
    {
        var type = Utility.GetLastStatementType("fn abs(x: number) { if x >= 0 { return x } else { return -x } }");
        var fnType = Assert.IsType<FunctionType>(type);
        Assert.True(fnType.ReturnType.Equals(PrimitiveType.Number), $"Expected 'number', got '{fnType.ReturnType}'");
    }

    [Theory]
    [InlineData("interface I { foo: number }; type Foo = I['foo'];")]
    [InlineData("type Foo = number[][number]")]
    public void Checks_IndexedType(string source)
    {
        var type = Utility.GetLastStatementType(source);
        var primitive = Assert.IsType<PrimitiveType>(type);
        Assert.Equal(PrimitiveTypeKind.Number, primitive.Kind);
    }

    [Fact]
    public void Checks_Declared_InterfaceDeclaration()
    {
        var type = Utility.GetLastStatementType("declare interface I { }");
        var iface = Assert.IsType<InterfaceType>(type);
        Assert.Equal("I", iface.Name);
        Assert.Empty(iface.Constraints);
        Assert.Null(iface.ObjectType.Indexer);
        Assert.Empty(iface.ObjectType.Properties);
    }

    [Fact]
    public void Checks_InterfaceDeclaration_Empty()
    {
        var type = Utility.GetLastStatementType("interface I { }");
        var iface = Assert.IsType<InterfaceType>(type);
        Assert.Equal("I", iface.Name);
        Assert.Empty(iface.Constraints);
        Assert.Null(iface.ObjectType.Indexer);
        Assert.Empty(iface.ObjectType.Properties);
    }

    [Fact]
    public void Checks_InterfaceDeclaration_WithProperties()
    {
        var type = Utility.GetLastStatementType("interface I { x: number, y: string }");
        var iface = Assert.IsType<InterfaceType>(type);
        Assert.Equal(2, iface.ObjectType.Properties.Count);

        var x = iface.ObjectType.Properties[0];
        Assert.Equal("x", x.Name);
        Assert.False(x.IsMutable);
        Assert.True(x.ValueType.Equals(PrimitiveType.Number));

        var y = iface.ObjectType.Properties[1];
        Assert.Equal("y", y.Name);
        Assert.False(y.IsMutable);
        Assert.True(y.ValueType.Equals(PrimitiveType.String));
    }

    [Fact]
    public void Checks_InterfaceDeclaration_WithMutableProperty()
    {
        var type = Utility.GetLastStatementType("interface I { mut count: number }");
        var iface = Assert.IsType<InterfaceType>(type);
        var prop = iface.ObjectType.Properties.Single();
        Assert.True(prop.IsMutable);
        Assert.True(prop.ValueType.Equals(PrimitiveType.Number));
    }

    [Fact]
    public void Checks_InterfaceDeclaration_WithIndexer()
    {
        var type = Utility.GetLastStatementType("interface I { [number]: string }");
        var iface = Assert.IsType<InterfaceType>(type);
        Assert.NotNull(iface.ObjectType.Indexer);
        Assert.True(iface.ObjectType.Indexer.KeyType.Equals(PrimitiveType.Number));
        Assert.True(iface.ObjectType.Indexer.ValueType.Equals(PrimitiveType.String));
        Assert.False(iface.ObjectType.Indexer.IsMutable);
    }

    [Fact]
    public void Checks_InterfaceDeclaration_WithMutableIndexer()
    {
        var type = Utility.GetLastStatementType("interface I { mut [string]: bool }");
        var iface = Assert.IsType<InterfaceType>(type);
        Assert.True(iface.ObjectType.Indexer!.IsMutable);
        Assert.True(iface.ObjectType.Indexer.KeyType.Equals(PrimitiveType.String));
        Assert.True(iface.ObjectType.Indexer.ValueType.Equals(PrimitiveType.Bool));
    }

    [Fact]
    public void Checks_InterfaceDeclaration_WithConstraint()
    {
        var type = Utility.GetLastStatementType("interface A { } interface B : A { }");
        var ifaceB = Assert.IsType<InterfaceType>(type);
        Assert.Single(ifaceB.Constraints);
        Assert.Equal("A", ifaceB.Constraints.First().Name);
    }

    [Fact]
    public void Checks_InterfaceDeclaration_WithMultipleConstraints()
    {
        var type = Utility.GetLastStatementType("interface A { } interface B { } interface C : A, B { }");
        var ifaceC = Assert.IsType<InterfaceType>(type);
        Assert.Equal(2, ifaceC.Constraints.Count);
        Assert.Equal("A", ifaceC.Constraints.First().Name);
        Assert.Equal("B", ifaceC.Constraints.Last().Name);
    }

    [Fact]
    public void Checks_InterfaceDeclaration_Generic()
    {
        var type = Utility.GetLastStatementType("interface I<T> { value: T }");
        var generic = Assert.IsType<GenericType>(type);
        Assert.Single(generic.Parameters);
        Assert.Equal("T", generic.Parameters.First().Name);

        var iface = Assert.IsType<InterfaceType>(generic.UnderlyingType);
        Assert.Equal("I", iface.Name);

        var prop = iface.ObjectType.Properties.Single();
        Assert.Equal("value", prop.Name);

        var typeParam = Assert.IsType<TypeParameter>(prop.ValueType);
        Assert.Equal("T", typeParam.Name);
    }

    [Fact]
    public void Checks_AsExpression_Chained_Unknown()
    {
        var type = Utility.GetLastStatementType("69 as unknown as number");
        Assert.True(type.Equals(PrimitiveType.Number), $"Expected 'number', got '{type}'");
    }

    [Fact]
    public void Checks_AsExpression_Chained_Never()
    {
        var type = Utility.GetLastStatementType("69 as never as number");
        Assert.True(type.Equals(PrimitiveType.Number), $"Expected 'number', got '{type}'");
    }

    [Fact]
    public void Checks_AsExpression_WithUnknown()
    {
        var type = Utility.GetLastStatementType("69 as unknown");
        Assert.True(type.Equals(PrimitiveType.Unknown), $"Expected 'unknown', got '{type}'");
    }

    [Fact]
    public void Checks_AsExpression_WithNever()
    {
        var type = Utility.GetLastStatementType("69 as never");
        Assert.True(type.Equals(PrimitiveType.Never), $"Expected 'never', got '{type}'");
    }

    [Fact]
    public void Checks_AsExpression()
    {
        var type = Utility.GetLastStatementType("69 as number");
        Assert.True(type.Equals(PrimitiveType.Number), $"Expected 'number', got '{type}'");
    }

    [Fact]
    public void Checks_DeclareVariable_HasDeclaredType()
    {
        var type = Utility.GetLastStatementType("declare let x: number");
        Assert.True(type.Equals(PrimitiveType.Number), $"Expected 'number', got '{type}'");
    }

    [Fact]
    public void Checks_DeclareMutableVariable_HasDeclaredType()
    {
        var type = Utility.GetLastStatementType("declare mut y: string");
        Assert.True(type.Equals(PrimitiveType.String), $"Expected 'string', got '{type}'");
    }

    [Fact]
    public void Checks_DeclareFunction_HasFunctionType()
    {
        var type = Utility.GetLastStatementType("declare fn add(a: number, b: number): bool");
        var fnType = Assert.IsType<FunctionType>(type);
        Assert.True(fnType.ReturnType.Equals(PrimitiveType.Bool), $"Expected 'bool', got '{fnType.ReturnType}'");
        Assert.Equal(2, fnType.ParameterTypes.Count);
        Assert.All(fnType.ParameterTypes, t => Assert.True(t.Equals(PrimitiveType.Number), $"Expected 'number', got '{t}'"));
    }

    [Fact]
    public void Checks_DeclareFunction_Generic_HasGenericFunctionType()
    {
        var type = Utility.GetLastStatementType("declare fn id<T>(value: T): T");
        var fnType = Assert.IsType<FunctionType>(type);
        Assert.Single(fnType.TypeParameters);
        Assert.Equal("T", fnType.TypeParameters[0].Name);

        var paramType = Assert.IsType<TypeParameter>(fnType.ParameterTypes[0]);
        Assert.Equal("T", paramType.Name);
        var returnType = Assert.IsType<TypeParameter>(fnType.ReturnType);
        Assert.Equal("T", returnType.Name);
    }

    [Fact]
    public void Checks_DeclareVariable_CanBeUsed()
    {
        var type = Utility.GetLastStatementType("declare let x: number; x");
        Assert.True(type.Equals(PrimitiveType.Number), $"Expected 'number', got '{type}'");
    }

    [Fact]
    public void Checks_DeclareFunction_CanBeInvoked()
    {
        const string source = """
            declare fn add(a: number, b: number): number;
            add(1, 2)
            """;

        var type = Utility.GetLastStatementType(source);
        Assert.True(type.Equals(PrimitiveType.Number), $"Expected 'number', got '{type}'");
    }

    [Fact]
    public void Checks_DeclareFunction_Generic_CanBeInvoked()
    {
        const string source = """
            declare fn id<T>(value: T): T;
            id(42)
            """;

        var type = Utility.GetLastStatementType(source);
        var literal = Assert.IsType<LiteralType>(type);
        Assert.Equal(42L, literal.Value);
    }

    [Fact]
    public void Checks_DeclareFunction_VoidReturnType()
    {
        var type = Utility.GetLastStatementType("declare fn print(msg: string): void");
        var fnType = Assert.IsType<FunctionType>(type);
        Assert.True(fnType.ReturnType.Equals(PrimitiveType.Void), $"Expected 'void', got '{fnType.ReturnType}'");
    }

    [Fact]
    public void Narrowing_NonNullable() => Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics("let x: number? = 69; if x != none x + 420"));

    [Fact]
    public void Narrowing_EqualsLiteral_ThenBranch()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let x: number | string = 42; if x == 42 { x + 1 }");
        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Narrowing_EqualsLiteral_ElseBranch()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let x: number | string = 42; if x == 42 { } else { x + 1 }");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidBinaryOp, "No binary operation for 'string' + 'number'.");
    }

    [Fact]
    public void Narrowing_NotEqualsLiteral_ThenBranch()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let x: number | string = 'hi'; if x != 'hi' { x + 1 }");
        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Narrowing_BooleanLiteral()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let x: bool | number = true; if x == true { x && false }");
        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Narrowing_Chained()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            "interface Inner { val: number? } interface Outer { inner: Inner } let obj = none as never as Outer; if obj.inner.val == 10 { obj.inner.val + 1 }"
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Checks_EnumTypeAnnotation()
    {
        var type = Utility.GetLastStatementType("enum Status { Active, Inactive } let x: Status = Status.Active");
        var union = Assert.IsType<UnionType>(type);
        Assert.Equal(2, union.Types.Count);
        Assert.Equal(0d, Assert.IsType<LiteralType>(union.Types.First()).Value);
        Assert.Equal(1d, Assert.IsType<LiteralType>(union.Types.Last()).Value);
    }

    [Fact]
    public void Checks_IndexedEnumTypeAnnotation()
    {
        var type = Utility.GetLastStatementType("enum Status { Active, Inactive } let x: Status['Active'] = Status.Active");
        var literal = Assert.IsType<LiteralType>(type);
        Assert.Equal(0d, literal.Value);
    }

    [Fact]
    public void Checks_EnumWithNumberBaseTypeExplicit()
    {
        var type = Utility.GetLastStatementType("enum Values : number { One = 1, Two = 2, Three = 3 }");
        var objectType = Assert.IsType<ObjectType>(type);
        Assert.Equal(3, objectType.Properties.Count);

        var oneType = Assert.IsType<LiteralType>(objectType.Properties[0].ValueType);
        Assert.Equal(1d, oneType.Value);

        var twoType = Assert.IsType<LiteralType>(objectType.Properties[1].ValueType);
        Assert.Equal(2d, twoType.Value);

        var threeType = Assert.IsType<LiteralType>(objectType.Properties[2].ValueType);
        Assert.Equal(3d, threeType.Value);
    }

    [Fact]
    public void Checks_EnumWithNumberBaseTypeImplicit()
    {
        var type = Utility.GetLastStatementType("enum Values : number { A, B, C }");
        var objectType = Assert.IsType<ObjectType>(type);
        Assert.Equal(3, objectType.Properties.Count);

        var aType = Assert.IsType<LiteralType>(objectType.Properties[0].ValueType);
        Assert.Equal(0d, aType.Value);

        var bType = Assert.IsType<LiteralType>(objectType.Properties[1].ValueType);
        Assert.Equal(1d, bType.Value);

        var cType = Assert.IsType<LiteralType>(objectType.Properties[2].ValueType);
        Assert.Equal(2d, cType.Value);
    }

    [Fact]
    public void Checks_EmptyEnum()
    {
        var type = Utility.GetLastStatementType("enum Empty { }");
        var objectType = Assert.IsType<ObjectType>(type);
        Assert.Empty(objectType.Properties);
    }

    [Fact]
    public void Checks_EnumDeclaration_WithImplicitNumberValues()
    {
        var type = Utility.GetLastStatementType("enum Abc { A, B, C }");
        var objectType = Assert.IsType<ObjectType>(type);
        Assert.Equal(3, objectType.Properties.Count);

        Assert.Equal("A", objectType.Properties[0].Name);
        var aType = Assert.IsType<LiteralType>(objectType.Properties[0].ValueType);
        Assert.Equal(0d, aType.Value);

        Assert.Equal("B", objectType.Properties[1].Name);
        var bType = Assert.IsType<LiteralType>(objectType.Properties[1].ValueType);
        Assert.Equal(1d, bType.Value);

        Assert.Equal("C", objectType.Properties[2].Name);
        var cType = Assert.IsType<LiteralType>(objectType.Properties[2].ValueType);
        Assert.Equal(2d, cType.Value);
    }

    [Fact]
    public void Checks_EnumDeclaration_WithExplicitNumberValues()
    {
        var type = Utility.GetLastStatementType("enum Status { Active = 1, Inactive = 0, Pending = 2 }");
        var objectType = Assert.IsType<ObjectType>(type);
        Assert.Equal(3, objectType.Properties.Count);

        Assert.Equal("Active", objectType.Properties[0].Name);
        var activeType = Assert.IsType<LiteralType>(objectType.Properties[0].ValueType);
        Assert.Equal(1d, activeType.Value);

        Assert.Equal("Inactive", objectType.Properties[1].Name);
        var inactiveType = Assert.IsType<LiteralType>(objectType.Properties[1].ValueType);
        Assert.Equal(0d, inactiveType.Value);

        Assert.Equal("Pending", objectType.Properties[2].Name);
        var pendingType = Assert.IsType<LiteralType>(objectType.Properties[2].ValueType);
        Assert.Equal(2d, pendingType.Value);
    }

    [Fact]
    public void Checks_EnumDeclaration_WithMixedImplicitAndExplicitValues()
    {
        var type = Utility.GetLastStatementType("enum Mixed { A, B = 69, C }");
        var objectType = Assert.IsType<ObjectType>(type);
        Assert.Equal(3, objectType.Properties.Count);

        Assert.Equal("A", objectType.Properties[0].Name);
        var aType = Assert.IsType<LiteralType>(objectType.Properties[0].ValueType);
        Assert.Equal(0d, aType.Value);

        Assert.Equal("B", objectType.Properties[1].Name);
        var bType = Assert.IsType<LiteralType>(objectType.Properties[1].ValueType);
        Assert.Equal(69d, bType.Value);

        Assert.Equal("C", objectType.Properties[2].Name);
        var cType = Assert.IsType<LiteralType>(objectType.Properties[2].ValueType);
        Assert.Equal(70d, cType.Value);
    }

    [Fact]
    public void Checks_EnumDeclaration_WithStringBackedValues()
    {
        var type = Utility.GetLastStatementType("enum Colors : string { Red = \"FF0000\", Green = \"00FF00\", Blue = \"0000FF\" }");
        var objectType = Assert.IsType<ObjectType>(type);
        Assert.Equal(3, objectType.Properties.Count);

        Assert.Equal("Red", objectType.Properties[0].Name);
        var redType = Assert.IsType<LiteralType>(objectType.Properties[0].ValueType);
        Assert.Equal("FF0000", redType.Value);

        Assert.Equal("Green", objectType.Properties[1].Name);
        var greenType = Assert.IsType<LiteralType>(objectType.Properties[1].ValueType);
        Assert.Equal("00FF00", greenType.Value);

        Assert.Equal("Blue", objectType.Properties[2].Name);
        var blueType = Assert.IsType<LiteralType>(objectType.Properties[2].ValueType);
        Assert.Equal("0000FF", blueType.Value);
    }

    [Fact]
    public void Checks_EnumMemberAccess()
    {
        var type = Utility.GetLastStatementType("enum Status { Active, Inactive }; Status.Active");
        var literal = Assert.IsType<LiteralType>(type);
        Assert.Equal(0d, literal.Value);
    }

    [Fact]
    public void Checks_EnumMemberAccess_WithExplicitValue()
    {
        var type = Utility.GetLastStatementType("enum Priority { Low = 10, High = 20 } Priority.High");
        var literal = Assert.IsType<LiteralType>(type);
        Assert.Equal(20d, literal.Value);
    }

    [Fact]
    public void Checks_QualifiedName_SingleDot_OnRange()
    {
        var type = Utility.GetLastStatementType("let r = (1..10); r.minimum");
        var primitive = Assert.IsType<PrimitiveType>(type);
        Assert.Equal(PrimitiveTypeKind.Number, primitive.Kind);

        type = Utility.GetLastStatementType("let r = (1..10); r.maximum");
        primitive = Assert.IsType<PrimitiveType>(type);
        Assert.Equal(PrimitiveTypeKind.Number, primitive.Kind);
    }

    [Theory]
    [InlineData(".minimum")]
    [InlineData(".maximum")]
    [InlineData("['minimum']")]
    [InlineData("['maximum']")]
    public void Checks_Access_OnRange(string access)
    {
        var type = Utility.GetLastStatementType($"(1..10){access}");
        var primitive = Assert.IsType<PrimitiveType>(type);
        Assert.Equal(PrimitiveTypeKind.Number, primitive.Kind);
    }

    [Fact]
    public void Checks_RangeLiteral_ElementAccess()
    {
        var type = Utility.GetLastStatementType("let x = [1, 2, 3]; x[1..10]");
        var array = Assert.IsType<ArrayType>(type);
        var primitive = Assert.IsType<PrimitiveType>(array.ElementType);
        Assert.Equal(PrimitiveTypeKind.Number, primitive.Kind);
    }

    [Fact]
    public void Checks_RangeLiteral_StringElementAccess()
    {
        var type = Utility.GetLastStatementType("let x = 'abcdef'; x[1..3]");
        var primitive = Assert.IsType<PrimitiveType>(type);
        Assert.Equal(PrimitiveTypeKind.String, primitive.Kind);
    }

    [Fact]
    public void Checks_StringElementAccess()
    {
        var type = Utility.GetLastStatementType("let x = 'abcdef'; x[1]");
        var primitive = Assert.IsType<PrimitiveType>(type);
        Assert.Equal(PrimitiveTypeKind.String, primitive.Kind);
    }

    [Fact]
    public void Checks_RangeLiteral()
    {
        var type = Utility.GetLastStatementType("1..10");
        Assert.True(type.Equals(Intrinsics.Range), $"Expected 'Range', got '{type}'");
    }

    [Fact]
    public void Checks_NameOf()
    {
        var type = Utility.GetLastStatementType("let x = 1; nameof(x)");
        var literal = Assert.IsType<LiteralType>(type);
        Assert.Equal("x", literal.Value);
    }

    [Fact]
    public void Checks_NameOf_QualifiedName()
    {
        var type = Utility.GetLastStatementType("let r = 1..10; nameof(r.minimum)");
        var literal = Assert.IsType<LiteralType>(type);
        Assert.Equal("r.minimum", literal.Value);
    }

    [Fact]
    public void Checks_ElementAccess_NestedArray()
    {
        var type = Utility.GetLastStatementType("let matrix = [[1, 2], [3, 4]]; matrix[0][1]");
        var primitive = Assert.IsType<PrimitiveType>(type);
        Assert.Equal(PrimitiveTypeKind.Number, primitive.Kind);
    }

    [Fact]
    public void Checks_ElementAccess_AsAssignmentTarget()
    {
        var type = Utility.GetLastStatementType("let arr = mut [1, 2, 3]; arr[0] = 42;");
        var literal = Assert.IsType<LiteralType>(type);
        Assert.Equal(42L, literal.Value);
    }

    [Fact]
    public void Checks_ElementAccess_ArrayIndexWithExpression()
    {
        var type = Utility.GetLastStatementType("let arr = [10, 20, 30]; let i = 1; arr[i + 1]");
        var primitive = Assert.IsType<PrimitiveType>(type);
        Assert.Equal(PrimitiveTypeKind.Number, primitive.Kind);
    }

    [Theory]
    [InlineData("let arr = [1, 2, 3]; arr[0]")]
    [InlineData("let arr = mut [1, 2, 3]; arr[0]")]
    [InlineData("mut arr = mut [1, 2, 3]; arr[0]")]
    [InlineData("mut arr = [1, 2, 3]; arr[0]")]
    public void Checks_ElementAccess_ArrayIndex(string source)
    {
        var type = Utility.GetLastStatementType(source);
        var primitive = Assert.IsType<PrimitiveType>(type);
        Assert.Equal(PrimitiveTypeKind.Number, primitive.Kind);
    }

    [Fact]
    public void Checks_GenericFunctionCall_WithRequiredTypeParameter()
    {
        const string source = """
            fn identity<T, U = number>(value: T?) -> value
            identity::<number>()
            """;

        var type = Utility.GetLastStatementType(source);
        var optional = Assert.IsType<OptionalType>(type);
        var primitive = Assert.IsType<PrimitiveType>(optional.NonNullableType);
        Assert.Equal(PrimitiveTypeKind.Number, primitive.Kind);
    }

    [Fact]
    public void Checks_GenericTypeAlias_WithDefaultAndConstraint()
    {
        const string source = """
            type Container<T: number = 42> = T
            let x: Container = 42
            x
            """;

        var result = Utility.AssertNoErrors(Utility.TypeCheck(source));
        var literal = Assert.IsType<LiteralType>(result.ReturnType);
        Assert.Equal(42L, literal.Value);
    }

    [Fact]
    public void Checks_GenericFunctionCall_WithMultipleDefaults()
    {
        const string source = """
            fn pair<A = number, B = string>(a: A, b: B) -> a
            pair(42, "hello")
            """;

        var type = Utility.GetLastStatementType(source);
        var literal = Assert.IsType<LiteralType>(type);
        Assert.Equal(42L, literal.Value);
    }

    [Fact]
    public void Checks_GenericFunctionCall_WithConstraintSatisfied()
    {
        const string source = """
            fn identity<T: number>(value: T) -> value
            identity(42)
            """;

        var type = Utility.GetLastStatementType(source);
        var literal = Assert.IsType<LiteralType>(type);
        Assert.Equal(42L, literal.Value);
    }

    [Fact]
    public void Checks_GenericFunctionCall_WithDefaultTypeParameter()
    {
        const string source = """
            fn wrap<T = number>(value: T) -> value
            wrap(42)
            """;

        var type = Utility.GetLastStatementType(source);
        var literal = Assert.IsType<LiteralType>(type);
        Assert.Equal(42L, literal.Value);
    }

    [Fact]
    public void Checks_GenericFunctionCall_ExplicitTypeArgumentOverridesDefault()
    {
        const string source = """
            fn wrap<T = number>(value: T) -> value
            wrap::<string>("hello")
            """;

        var type = Utility.GetLastStatementType(source);
        var primitive = Assert.IsType<PrimitiveType>(type);
        Assert.Equal(PrimitiveTypeKind.String, primitive.Kind);
    }

    [Fact]
    public void Checks_NonGenericFunctionCall()
    {
        const string source = """
            fn add(a: number, b: number): number -> a + b
            add(1, 2)
            """;

        var type = Utility.GetLastStatementType(source);
        Assert.True(type.Equals(PrimitiveType.Number), $"Expected 'number', got '{type}'");
    }

    [Fact]
    public void Checks_Function()
    {
        var type = Utility.GetLastStatementType("fn concat(a: string, b: string) -> a + b");
        var functionType = Assert.IsType<FunctionType>(type);
        Assert.True(functionType.ReturnType.Equals(PrimitiveType.String), $"Expected 'string', got '{functionType.ReturnType}'");
        Assert.Empty(functionType.TypeParameters);
        Assert.Equal(2, functionType.ParameterTypes.Count);
        Assert.All(functionType.ParameterTypes, t => Assert.True(t.Equals(PrimitiveType.String), $"Expected 'string', got '{t}'"));
    }

    [Fact]
    public void Checks_GenericFunctionCall_InferredLiteralType()
    {
        const string source = """
            fn id<T>(value: T) -> value
            id(69)
            """;

        var type = Utility.GetLastStatementType(source);
        var literal = Assert.IsType<LiteralType>(type);
        Assert.Equal(69L, literal.Value);
    }

    [Fact]
    public void Checks_GenericFunctionCall_InferredArrayType()
    {
        const string source = """
            fn id<T>(value: T[]) -> value
            id([69])
            """;

        var type = Utility.GetLastStatementType(source);
        var array = Assert.IsType<ArrayType>(type);
        var primitive = Assert.IsType<PrimitiveType>(array.ElementType);
        Assert.Equal(PrimitiveTypeKind.Number, primitive.Kind);
    }

    [Fact]
    public void Checks_GenericFunctionCall_InferredGenericType()
    {
        const string source = """
            type Arr<T> = T[]
            fn id<T>(value: Arr<T>) -> value
            id([69])
            """;

        var type = Utility.GetLastStatementType(source);
        var array = Assert.IsType<ArrayType>(type);
        var primitive = Assert.IsType<PrimitiveType>(array.ElementType);
        Assert.Equal(PrimitiveTypeKind.Number, primitive.Kind);
    }

    [Fact]
    public void Checks_GenericFunctionCall_InferredPrimitiveType()
    {
        const string source = """
            fn id<T>(value: T) -> value
            let x: number = 42
            id(x)
            """;

        var type = Utility.GetLastStatementType(source);
        Assert.True(type.Equals(PrimitiveType.Number), $"Expected 'number', got '{type}'");
    }

    [Fact]
    public void Checks_GenericFunctionCall_WithOptionalReturn()
    {
        const string source = """
            fn opt<T>(value: T): T? -> value
            opt(69)
            """;

        var type = Utility.GetLastStatementType(source);
        var optional = Assert.IsType<OptionalType>(type);
        var inner = Assert.IsType<LiteralType>(optional.NonNullableType);
        Assert.Equal(69L, inner.Value);
    }

    [Fact]
    public void Checks_GenericFunctionCall_WithMultipleTypeParameters()
    {
        const string source = """
            fn pair<A, B>(a: A, b: B) -> a
            pair(42, "hello")
            """;

        var type = Utility.GetLastStatementType(source);
        var literal = Assert.IsType<LiteralType>(type);
        Assert.Equal(42L, literal.Value);
    }

    [Fact]
    public void Checks_GenericFunctionCall_InferenceAcrossMultipleParameters_Widens()
    {
        const string source = """
            fn first<T>(a: T, b: T) -> a
            first(42, 69)
            """;

        var type = Utility.GetLastStatementType(source);
        Assert.Equal(PrimitiveType.Number, type);
    }

    [Fact]
    public void Checks_GenericFunctionCall_ExplicitTypeArgument()
    {
        const string source = """
            fn id<T>(value: T) -> value
            id::<number>(69)
            """;

        var type = Utility.GetLastStatementType(source);
        Assert.True(type.Equals(PrimitiveType.Number), $"Expected 'number', got '{type}'");
    }

    [Fact]
    public void Checks_GenericPropertyMethodCall_ExplicitTypeArgument()
    {
        const string source = """
            interface Thing {}
            interface Widget : Thing {}

            interface World<X> {
                [luau_name("FindFirstChildOfClass"), luau_method]
                find_first_child_of_class: fn<T: Thing>: T;
            }

            let world = none as never as World<number>;
            world.find_first_child_of_class::<Widget>()
            """;

        var type = Utility.GetLastStatementType(source);
        Assert.False(type is TypeVariable, $"Expected 'Widget', got a type variable ('{type}')");
        var interfaceType = Assert.IsType<InterfaceType>(type);
        Assert.Equal("Widget", interfaceType.Name);
    }

    [Fact]
    public void Checks_GenericFunctionCall_ExplicitTypeArgument_WithOptional()
    {
        const string source = """
            fn opt<T>(value: T): T? -> value
            opt::<number>(69)
            """;

        var type = Utility.GetLastStatementType(source);
        var optional = Assert.IsType<OptionalType>(type);
        var inner = Assert.IsType<PrimitiveType>(optional.NonNullableType);
        Assert.Equal(PrimitiveTypeKind.Number, inner.Kind);
    }

    [Fact]
    public void Checks_NonGenericFunctionCall_InferredReturnType()
    {
        const string source = """
            fn concat(a: string, b: string) -> a + b
            concat("hello", " world")
            """;

        var type = Utility.GetLastStatementType(source);
        Assert.True(type.Equals(PrimitiveType.String), $"Expected 'string', got '{type}'");
    }

    [Theory]
    [InlineData("1 == '1'", "bool")]
    [InlineData("1 != '1'", "bool")]
    public void Checks_BinaryOperator_WithMixedTypes_ReturnsExpectedType(string source, string expectedTypeName)
    {
        var type = Utility.GetLastStatementType(source);
        var expectedType = expectedTypeName switch
        {
            "string" => PrimitiveType.String,
            "bool" => PrimitiveType.Bool,
            "number" => PrimitiveType.Number,
            _ => PrimitiveType.Never
        };

        Assert.True(
            type.Equals(expectedType),
            $"Expected '{expectedTypeName}', got '{type}' for expression '{source}'"
        );
    }

    [Theory]
    [InlineData("1 + 1", "number")]
    [InlineData("'a' + 'b'", "string")]
    [InlineData("1 - 2", "number")]
    [InlineData("1 / 2", "number")]
    [InlineData("1 * 2", "number")]
    [InlineData("true && false", "bool")]
    [InlineData("1 < 2", "bool")]
    [InlineData("'a' > 'b'", "bool")]
    [InlineData("1 ?? 1", "1")]
    [InlineData("1 ?? 'a'", "1 | \"a\"")]
    [InlineData("mut x: string? = 'a'; 1 ?? x", "1 | string")]
    [InlineData("mut x: string? = 'a'; x ?? 'foo'", "string")]
    public void Checks_BinaryOperator_ReturnsExpectedType(string source, string expectedTypeName)
    {
        var type = Utility.GetLastStatementType(source);
        Assert.True(
            expectedTypeName == type.ToString(),
            $"Expected '{expectedTypeName}', got '{type}' for expression '{source}'"
        );
    }

    [Theory]
    [InlineData("!true", "bool")]
    [InlineData("~5", "number")]
    [InlineData("~0", "number")]
    [InlineData("-5", "number")]
    [InlineData("-0", "number")]
    [InlineData("-(5)", "number")]
    public void Checks_UnaryOperator_ValidOperand_ReturnsExpectedType(string source, string expectedTypeName)
    {
        var type = Utility.GetLastStatementType(source);
        var expectedType = expectedTypeName == "bool" ? PrimitiveType.Bool : PrimitiveType.Number;
        Assert.True(type.Equals(expectedType), $"Expected '{expectedTypeName}', got '{type}' for expression '{source}'");
    }

    [Theory]
    [InlineData("type A = number")]
    [InlineData("type A = number; let x: A = 1")]
    [InlineData("type Id<T> = T; let x: Id<number> = 1")]
    [InlineData("type Id<T> = T; type X = Id<number>; let x: X = 1")]
    [InlineData("type Id<T> = T; let x: Id<number> = 1; x")]
    [InlineData("type Id<T> = T; let x = 1; let y: Id<number> = x; y")]
    [InlineData("type Const<A, B> = A; let x: Const<number, string> = 1")]
    [InlineData("type Id<T> = T; type NumId = Id<number>; type X = NumId; let x: X = 1")]
    public void Checks_TypeAlias_Resolution(string source)
    {
        var narrowType = Utility.GetLastStatementType(source);
        var type = narrowType is InstantiatedType instantiated ? instantiated.Expand() : narrowType;
        Assert.True(
            type.Equals(PrimitiveType.Number),
            $"Expected 'number', got '{type}'"
        );
    }

    [Fact]
    public void Checks_Assignment_Resolution()
    {
        var type = Utility.GetLastStatementType("mut x = 42; x = 69");
        var literal = Assert.IsType<LiteralType>(type);
        Assert.Equal(69L, literal.Value);
    }

    [Theory]
    [InlineData("let x = 42; x")]
    [InlineData("let x = 42; let y = x; y")]
    [InlineData("let x = 42; let y = x; let z = y; z;")]
    public void Checks_Identifier_Resolution(string source)
    {
        var type = Utility.GetLastStatementType(source);
        var literal = Assert.IsType<LiteralType>(type);
        Assert.Equal(42L, literal.Value);
    }

    [Fact]
    public void Checks_Identifier_ResolvesAnnotatedType()
    {
        var type = Utility.GetLastStatementType("let x: number = 42; x;");
        Assert.True(type.Equals(PrimitiveType.Number), $"Expected 'number', got '{type}'");
    }

    [Fact]
    public void Checks_VariableDeclaration_WidenedInference()
    {
        var type = Utility.GetLastStatementType("mut x = 42");
        Assert.True(type.Equals(PrimitiveType.Number), $"Expected 'number', got '{type}'");
    }

    [Fact]
    public void Checks_VariableDeclaration_Inference()
    {
        var constType = Utility.GetLastStatementType("let x = 42");
        var literal = Assert.IsType<LiteralType>(constType);
        Assert.Equal(42L, literal.Value);
    }

    [Fact]
    public void Checks_VariableDeclaration_WithTypeAnnotation()
    {
        var type = Utility.GetLastStatementType("let x: number = 42");
        Assert.True(type.Equals(PrimitiveType.Number), $"Expected 'number', got '{type}'");
    }

    [Fact]
    public void Checks_FunctionTypes()
    {
        var type = Utility.GetLastStatementType("mut x: fn<T>(x: T): T?;");
        var function = Assert.IsType<FunctionType>(type);
        Assert.Single(function.TypeParameters);
        Assert.Single(function.ParameterTypes);

        var typeParameter = function.TypeParameters.First();
        Assert.Equal("T", typeParameter.Name);
        Assert.Null(typeParameter.Constraint);
        Assert.Null(typeParameter.DefaultType);

        Assert.IsType<OptionalType>(function.ReturnType);
    }

    [Fact]
    public void Checks_IntersectionTypes()
    {
        var type = Utility.GetLastStatementType("mut x: number & string;");
        var primitive = Assert.IsType<PrimitiveType>(type);
        Assert.Equal(PrimitiveTypeKind.Never, primitive.Kind);
    }

    [Fact]
    public void Checks_UnionTypes()
    {
        var type = Utility.GetLastStatementType("mut x: number | string;");
        var union = Assert.IsType<UnionType>(type);
        Assert.Equal(2, union.Types.Count);

        var firstPrimitive = Assert.IsType<PrimitiveType>(union.Types.First());
        var lastPrimitive = Assert.IsType<PrimitiveType>(union.Types.Last());
        Assert.Equal(PrimitiveTypeKind.Number, firstPrimitive.Kind);
        Assert.Equal(PrimitiveTypeKind.String, lastPrimitive.Kind);
    }

    [Fact]
    public void Checks_Mutable_ArrayTypes()
    {
        var type = Utility.GetLastStatementType("let x: number[mut] = [];");
        var array = Assert.IsType<ArrayType>(type);
        Assert.True(array.IsMutable);

        var primitive = Assert.IsType<PrimitiveType>(array.ElementType);
        Assert.Equal(PrimitiveTypeKind.Number, primitive.Kind);
    }

    [Fact]
    public void Checks_ArrayTypes()
    {
        var type = Utility.GetLastStatementType("let x: number[] = [];");
        var array = Assert.IsType<ArrayType>(type);
        Assert.False(array.IsMutable);

        var primitive = Assert.IsType<PrimitiveType>(array.ElementType);
        Assert.Equal(PrimitiveTypeKind.Number, primitive.Kind);
    }

    [Fact]
    public void Checks_OptionalTypes()
    {
        var type = Utility.GetLastStatementType("mut x: number?;");
        var optional = Assert.IsType<OptionalType>(type);
        var primitive = Assert.IsType<PrimitiveType>(optional.NonNullableType);
        Assert.Equal(PrimitiveTypeKind.Number, primitive.Kind);
    }

    [Fact]
    public void Checks_PrimitiveTypes()
    {
        var type = Utility.GetLastStatementType("mut x: number;");
        var primitive = Assert.IsType<PrimitiveType>(type);
        Assert.Equal(PrimitiveTypeKind.Number, primitive.Kind);
    }

    [Fact]
    public void Checks_LiteralTypes()
    {
        var type = Utility.GetLastStatementType("let x: 69 = 69; x;");
        var literal = Assert.IsType<LiteralType>(type);
        Assert.Equal(69L, literal.Value);
    }

    [Fact]
    public void Checks_MixedOptional_ArrayLiterals()
    {
        var type = Utility.GetLastStatementType("[1, none]");
        var array = Assert.IsType<ArrayType>(type);
        Assert.False(array.IsMutable);

        var optional = Assert.IsType<OptionalType>(array.ElementType);
        var primitive = Assert.IsType<PrimitiveType>(optional.NonNullableType);
        Assert.Equal(PrimitiveTypeKind.Number, primitive.Kind);
    }

    [Fact]
    public void Checks_Mixed_ArrayLiterals()
    {
        var type = Utility.GetLastStatementType("[1, true]");
        var array = Assert.IsType<ArrayType>(type);
        Assert.False(array.IsMutable);

        var union = Assert.IsType<UnionType>(array.ElementType);
        Assert.Equal(2, union.Types.Count);

        var number = Assert.IsType<PrimitiveType>(union.Types.First());
        var boolean = Assert.IsType<PrimitiveType>(union.Types.Last());
        Assert.Equal(PrimitiveTypeKind.Number, number.Kind);
        Assert.Equal(PrimitiveTypeKind.Bool, boolean.Kind);
    }

    [Fact]
    public void Checks_Empty_ArrayLiterals()
    {
        var type = Utility.GetLastStatementType("[]");
        var array = Assert.IsType<ArrayType>(type);
        Assert.False(array.IsMutable);

        var primitive = Assert.IsType<PrimitiveType>(array.ElementType);
        Assert.Equal(PrimitiveTypeKind.Never, primitive.Kind);
    }

    [Fact]
    public void Checks_InterpolatedStringLiteral_IsString()
    {
        var type = Utility.GetLastStatementType("""let name = "world"; $"Welcome, {name}!" """);
        var primitive = Assert.IsType<PrimitiveType>(type);
        Assert.Equal(PrimitiveTypeKind.String, primitive.Kind);
    }

    [Fact]
    public void Checks_InterpolatedStringLiteral_NotLiteralType()
    {
        var type = Utility.GetLastStatementType("""$"just text" """);
        Assert.IsType<PrimitiveType>(type);
    }

    [Fact]
    public void Checks_InterpolatedStringLiteral_TypeChecksHoleExpressions()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("""$"{1 + "a"}" """);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidBinaryOp, "No binary operation for 'number' + 'string'.");
    }

    [Fact]
    public void Checks_ArrayLiterals()
    {
        var type = Utility.GetLastStatementType("[1, 2, 3]");
        var array = Assert.IsType<ArrayType>(type);
        Assert.False(array.IsMutable);

        var primitive = Assert.IsType<PrimitiveType>(array.ElementType);
        Assert.Equal(PrimitiveTypeKind.Number, primitive.Kind);
    }

    [Fact]
    public void Checks_Mutable_ArrayLiterals()
    {
        var type = Utility.GetLastStatementType("let x = mut [1, 2, 3]; x");
        var array = Assert.IsType<ArrayType>(type);
        Assert.True(array.IsMutable);

        var primitive = Assert.IsType<PrimitiveType>(array.ElementType);
        Assert.Equal(PrimitiveTypeKind.Number, primitive.Kind);
    }

    [Fact]
    public void Checks_NumberLiterals()
    {
        var type = Utility.GetLastStatementType("69");
        var literal = Assert.IsType<LiteralType>(type);
        Assert.Equal(69L, literal.Value);
    }

    [Fact]
    public void Checks_StringLiterals()
    {
        var type = Utility.GetLastStatementType("\"hello\"");
        var literal = Assert.IsType<LiteralType>(type);
        Assert.Equal("hello", literal.Value);
    }

    [Fact]
    public void Checks_BoolLiterals()
    {
        var trueType = Utility.GetLastStatementType("true");
        var trueLiteral = Assert.IsType<LiteralType>(trueType);
        Assert.Equal(true, trueLiteral.Value);

        var falseType = Utility.GetLastStatementType("false");
        var falseLiteral = Assert.IsType<LiteralType>(falseType);
        Assert.Equal(false, falseLiteral.Value);
    }
    #endregion Checks

    #region SelfReferentialTypes
    [Fact]
    public void Unifies_FunctionTypes_WithSelfReferentialParameterType_WithoutStackOverflow()
    {
        const string source = """
            declare interface Node {
                next: Node;
            }

            declare fn a(n: Node): void;
            let b: fn(n: Node): void = a;
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
    }

    [Fact]
    public void ThrowsFor_FunctionTypes_WithSelfReferentialParameterType_AndMismatchedReturnType()
    {
        const string source = """
            declare interface Node {
                next: Node;
            }

            declare fn a(n: Node): void;
            let b: fn(n: Node): number = a;
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Assert.Contains(diagnostics.Set, d => d.Code == InternalCodes.TypeMismatch);
    }
    #endregion SelfReferentialTypes

    #region Bidirectional
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
            "Type '\"no\"' is not assignable to type 'number'."
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
            "Type '\"no\"' is not assignable to type 'number'."
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
            "Type '\"no\"' is not assignable to type 'number'."
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
            "Type '\"no\"' is not assignable to type 'number'."
        );
    }

    [Fact]
    public void ThrowsFor_AnnotatedArrayLiteral_ElementMismatch_ReportsOnBadElement()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("""let xs: number[] = [1, "no"]""");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type '\"no\"' is not assignable to type 'number'."
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
            "Type '\"no\"' is not assignable to type 'number'."
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
            "Type '\"no\"' is not assignable to type 'number'."
        );
    }

    [Fact]
    public void ThrowsFor_ArrayLiteral_ElementMismatch_AsExpressionBodyReturn()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("""fn bad(): number[] -> [1, "no"]""");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type '\"no\"' is not assignable to type 'number'."
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
            "Type '\"no\"' is not assignable to type 'number'."
        );
    }

    [Fact]
    public void ThrowsFor_NestedArrayLiteral_ElementMismatch_ReportsOnBadElement()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("""let xs: number[][] = [[1, "no"]]""");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type '\"no\"' is not assignable to type 'number'."
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

        Utility.AssertDiagnostic(diagnostics, InternalCodes.TypeMismatch, "Type '\"no\"' is not assignable to type 'number'.");
    }
    #endregion Bidirectional

    #region Events
    [Fact]
    public void Checks_InterfaceEventMember_AccessedThroughVariable_TypesAsEvent()
    {
        const string source = """
            interface EventObject {
                event consumer(param: string);
            }

            let eo = none as never as EventObject;
            eo.consumer
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
        var type = Utility.GetLastStatementType(source);
        var instantiated = Assert.IsType<InstantiatedType>(type);
        Assert.Equal("Event", instantiated.GenericType.Declaration.Name.Text);
        Assert.True(
            instantiated.Arguments.TakeWhile(Type.IsDefined).Single().Equals(PrimitiveType.String),
            $"Expected first event argument to be 'string', got '{instantiated.Arguments.FirstOrDefault()}'"
        );
    }

    [Fact]
    public void Checks_InterfaceEventMember_Connect_TypesAsEventConnection()
    {
        const string source = """
            interface EventObject {
                event consumer(param: string);
            }

            fn on_consumer(p: string): void { }

            let eo = none as never as EventObject;
            eo.consumer += on_consumer
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
        var type = Utility.GetLastStatementType(source);
        var interfaceType = Assert.IsType<InterfaceType>(type);
        Assert.Equal("ScriptConnection", interfaceType.Name);
    }

    [Fact]
    public void Checks_InterfaceEventMember_Invocation_TypesAsVoid()
    {
        const string source = """
            interface EventObject {
                event consumer(param: string);
            }

            let eo = none as never as EventObject;
            eo.consumer("abc")
            """;

        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics(source));
        var type = Utility.GetLastStatementType(source);
        Assert.True(type.Equals(PrimitiveType.Void), $"Expected 'void', got '{type}'");
    }

    [Fact]
    public void ThrowsFor_FiringConsumerEvent_ThroughInterfaceMember_AccessedThroughVariable()
    {
        const string source = """
            interface EventObject {
                mut consumer: ConsumerEvent<string>;
            }

            let eo = none as never as EventObject;
            eo.consumer("abc")
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidInvocation,
            "Consumer events may only be observed, not fired."
        );
    }

    [Fact]
    public void Checks_InterfaceEvent_WithAttribute_NoErrors() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                interface EventObject {
                    [luau_name("OnConsume")]
                    event consumer(param: string);
                }
                """
            )
        );

    [Fact]
    public void Checks_GlobalEvent_WithAttribute_NoErrors() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(
                """
                [luau_name("OnConsume")]
                event consumer(param: string);
                """
            )
        );

    [Fact]
    public void ThrowsFor_InterfaceEvent_WithNonFunctionAttribute()
    {
        const string source = """
            let not_a_function = 1;
            interface EventObject {
                [not_a_function]
                event consumer(param: string);
            }
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.NonFunctionAttribute,
            "Only functions may be used as attributes."
        );

        Assert.Single(diagnostics.Set, d => d.Code == InternalCodes.NonFunctionAttribute);
    }

    [Fact]
    public void ThrowsFor_GlobalEvent_WithNonFunctionAttribute()
    {
        const string source = """
            let not_a_function = 1;
            [not_a_function]
            event consumer(param: string);
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.NonFunctionAttribute,
            "Only functions may be used as attributes."
        );

        Assert.Single(diagnostics.Set, d => d.Code == InternalCodes.NonFunctionAttribute);
    }
    #endregion Events

    #region Match
    [Fact]
    public void Allows_Match_LiteralAndWildcardArms()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("""match 1 { 0 -> "zero", _ -> "other" }""");
        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Allows_Match_IdentifierBinding()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("""match 1 { x -> x }""");
        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Allows_Match_LetPatternBinding()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("""match 1 { let name -> name }""");
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
        var diagnostics = Utility.GetTypeCheckerDiagnostics("""match 1 { [a] -> a, _ -> 0 }""");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Array pattern cannot match value of type '1'."
        );
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
        var diagnostics = Utility.GetTypeCheckerDiagnostics("""match 1 { x when x > 0 -> x }""");
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
    #endregion Match
}