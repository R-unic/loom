using Loom.Core.Diagnostics;
using Loom.Luau.AST;

namespace Loom.Testing.Generation;

[Collection("Assembly")]
public class MacroExpanderTest
{
    /// <summary>
    ///     A chain bound straight to a name accumulates into that name, so no temporary is declared and
    ///     no copy of it is left behind.
    /// </summary>
    [Theory]
    [InlineData("let kept = numbers.where(fn(n) -> n > 1).length;", "kept")]
    [InlineData("let total = numbers.select(fn(n) -> n * 2).aggregate(0, fn(a, n) -> a + n);", "total")]
    [InlineData("let found = numbers.select(fn(n) -> n * 2).any(fn(n) -> n > 4);", "found")]
    public void Generates_ArrayChain_AccumulatingIntoTheNameItIsBoundTo(string declaration, string name)
    {
        var luauTree = Utility.GetLuauAST($"let numbers = [1, 2, 3]; {declaration}", true);

        Assert.IsType<ForStatement>(luauTree.Statements[^1]);
        Assert.Equal(name, Assert.IsType<LocalVariable>(luauTree.Statements[^2]).Name);
    }

    /// <summary>
    ///     Unless the loop binds that name itself, where accumulating into it would count into the loop
    ///     variable instead. Then the temporary comes back.
    /// </summary>
    [Fact]
    public void Generates_ArrayChain_KeepingATemporaryWhenTheLoopBindsTheSameName()
    {
        var luauTree = Utility.GetLuauAST("let numbers = [1, 2, 3]; let n = numbers.where(fn(n) -> n > 1).length;", true);

        var binding = Assert.IsType<ConstVariable>(luauTree.Statements[^1]);
        Assert.Equal("n", binding.Name);
        Assert.NotEqual("n", Assert.IsType<Identifier>(binding.Initializer).Name);
    }

    /// <summary>A 'mut' binding may be reassigned, so its declaration stays the generator's to write.</summary>
    [Fact]
    public void Generates_ArrayChain_KeepingATemporaryForAMutableBinding()
    {
        var luauTree = Utility.GetLuauAST("let numbers = [1, 2, 3]; mut kept = numbers.where(fn(n) -> n > 1).length;", true);

        var binding = Assert.IsType<LocalVariable>(luauTree.Statements[^1]);
        Assert.Equal("kept", binding.Name);
        Assert.NotEqual("kept", Assert.IsType<Identifier>(binding.Initializer!).Name);
    }

    [Theory]
    [InlineData("CreatableInstance", "new_instance")]
    [InlineData("ServiceInstance", "get_service")]
    [InlineData("Instance", "is_a", "get_service::<Workspace>().")]
    [InlineData("Instance", "find_first_child_of_class", "get_service::<Workspace>().")]
    [InlineData("Instance", "find_first_child_which_is_a", "get_service::<Workspace>().")]
    [InlineData("Instance", "find_first_ancestor_of_class", "get_service::<Workspace>().")]
    [InlineData("Instance", "find_first_ancestor_which_is_a", "get_service::<Workspace>().")]
    public void ThrowsFor_TypeParametersInNewInstanceCall(string constraint, string fnName, string extra = "")
    {
        var diagnostics = Utility.GetGeneratorDiagnostics($"fn abc<T: {constraint}> -> {extra}{fnName}::<T>();", true);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.AbstractTypeParameterInMacro, $"Cannot use type parameter 'T' with '{fnName}::<T>()' macro.");
    }

    [Theory]
    [InlineData("is_a", "IsA")]
    [InlineData("find_first_child_of_class", "FindFirstChildOfClass")]
    [InlineData("find_first_child_which_is_a", "FindFirstChildWhichIsA")]
    [InlineData("find_first_ancestor_of_class", "FindFirstAncestorOfClass")]
    [InlineData("find_first_ancestor_which_is_a", "FindFirstAncestorWhichIsA")]
    public void Generates_InstanceQuery(string methodName, string luauMethodName)
    {
        var source = $"let x = get_service::<Workspace>().{methodName}::<Part>()";
        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.Last());
        var call = Assert.IsType<Call>(variable.Initializer);
        Assert.True(call.IsMethod);
        var callee = Assert.IsType<PropertyAccess>(call.Callee);
        Assert.Equal(luauMethodName, Assert.Single(callee.Names));
        Assert.Equal("Part", Assert.IsType<StringLiteral>(Assert.Single(call.Arguments)).Value);
    }

    [Fact]
    public void Generates_InvocationMacroReference_AsFunctionArgument_Ok()
    {
        const string source = """
            declare fn consume<T, E>(callback: fn(value: T): Result<T, E>): void;
            consume(Result::ok);
            """;

        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));

        var statement = Assert.IsType<ExpressionStatement>(luauTree.Statements.Last());
        var consumeCall = Assert.IsType<Call>(statement.Expression);
        var anonymousFunction = Assert.IsType<AnonymousFunction>(Assert.Single(consumeCall.Arguments));
        var returnStatement = Assert.IsType<Return>(Assert.Single(anonymousFunction.Body.Statements));
        var table = Assert.IsType<Table>(returnStatement.Expression);

        var okInit = Assert.IsType<PropertyTableInitializer>(table.Initializers[0]);
        var valueInit = Assert.IsType<PropertyTableInitializer>(table.Initializers[1]);
        Assert.Equal("ok", okInit.PropertyName);
        Assert.Equal("value", valueInit.PropertyName);
        Assert.IsType<Identifier>(valueInit.Value);
    }

    [Fact]
    public void Generates_InvocationMacroReference_ViaElementAccess_Ok()
    {
        const string source = """
            declare fn consume<T, E>(callback: fn(value: T): Result<T, E>): void;
            consume(Result["ok"]);
            """;

        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));

        var statement = Assert.IsType<ExpressionStatement>(luauTree.Statements.Last());
        var consumeCall = Assert.IsType<Call>(statement.Expression);
        var anonymousFunction = Assert.IsType<AnonymousFunction>(Assert.Single(consumeCall.Arguments));
        var returnStatement = Assert.IsType<Return>(Assert.Single(anonymousFunction.Body.Statements));
        var table = Assert.IsType<Table>(returnStatement.Expression);

        var okInit = Assert.IsType<PropertyTableInitializer>(table.Initializers[0]);
        Assert.Equal("ok", okInit.PropertyName);
    }

    [Fact]
    public void Generates_InvocationMacroReference_ToStringMacroOnlyMember_WrapsCorrectly()
    {
        const string source = """
            declare fn consumeStr(cb: fn(): string): void;
            consumeStr("abc".upper);
            """;

        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));

        var statement = Assert.IsType<ExpressionStatement>(luauTree.Statements.Last());
        var consumeCall = Assert.IsType<Call>(statement.Expression);
        var anonymousFunction = Assert.IsType<AnonymousFunction>(Assert.Single(consumeCall.Arguments));
        Assert.Empty(anonymousFunction.Parameters);

        var returnStatement = Assert.IsType<Return>(Assert.Single(anonymousFunction.Body.Statements));
        var upperCall = Assert.IsType<Call>(returnStatement.Expression);
        var callee = Assert.IsType<PropertyAccess>(upperCall.Callee);
        Assert.Equal(["upper"], callee.Names);
        Assert.Equal("string", Assert.IsType<Identifier>(callee.Target).Name);
        Assert.Equal("abc", Assert.IsType<StringLiteral>(Assert.Single(upperCall.Arguments)).Value);
    }

    [Fact]
    public void Generates_ArrayInvocation_UnrecognizedMember_FallsThroughUnexpanded()
    {
        const string source = """
            let arr: number[] = [1, 2, 3];
            arr.bogus();
            """;

        var luauTree = Utility.GetLuauAST(source);
        var statement = Assert.IsType<ExpressionStatement>(luauTree.Statements.Last());
        var call = Assert.IsType<Call>(statement.Expression);
        var callee = Assert.IsType<PropertyAccess>(call.Callee);
        Assert.Equal(["bogus"], callee.Names);
    }

    [Fact]
    public void Generates_RangeInvocation_UnrecognizedMember_FallsThroughUnexpanded()
    {
        const string source = """
            let rng = 1..10;
            rng.bogus();
            """;

        var luauTree = Utility.GetLuauAST(source);
        var statement = Assert.IsType<ExpressionStatement>(luauTree.Statements.Last());
        var call = Assert.IsType<Call>(statement.Expression);
        var callee = Assert.IsType<PropertyAccess>(call.Callee);
        Assert.Equal(["bogus"], callee.Names);
    }

    [Fact]
    public void Generates_ResultStaticInvocation_UnrecognizedMember_FallsThroughUnexpanded()
    {
        const string source = """
            declare fn consume<T, E>(callback: fn(value: T): Result<T, E>): void;
            Result.bogus();
            """;

        var luauTree = Utility.GetLuauAST(source, true);
        var statement = Assert.IsType<ExpressionStatement>(luauTree.Statements.Last());
        var call = Assert.IsType<Call>(statement.Expression);
        var callee = Assert.IsType<PropertyAccess>(call.Callee);
        Assert.Equal(["bogus"], callee.Names);
    }

    [Fact]
    public void Generates_StringInvocation_UnrecognizedMember_FallsThroughUnexpanded()
    {
        const string source = """
            let s = "abc";
            s.bogus();
            """;

        var luauTree = Utility.GetLuauAST(source);
        var statement = Assert.IsType<ExpressionStatement>(luauTree.Statements.Last());
        var call = Assert.IsType<Call>(statement.Expression);
        var callee = Assert.IsType<PropertyAccess>(call.Callee);
        Assert.Equal(["bogus"], callee.Names);
    }

    /// <summary>
    ///     A macro that is <em>called</em> inside another call's arguments is a call, not a reference. It
    ///     used to be both: the reference context test only asks whether some ancestor is an argument list,
    ///     which the callee of the inner call satisfies through the outer one, so the callee became a lambda
    ///     and the invocation macro was then expanded on top of it - emitting
    ///     <c>table.find(function(argument0) ... end, 2)</c>.
    /// </summary>
    [Fact]
    public void Generates_InvocationMacro_CalledInsideAnotherCallsArguments_AsACall()
    {
        const string source = """
            declare fn consume(value: bool): void;
            let xs = [1, 2, 3];
            consume(xs.has(2));
            """;

        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));

        var statement = Assert.IsType<ExpressionStatement>(luauTree.Statements.Last());
        var consumeCall = Assert.IsType<Call>(statement.Expression);
        var comparison = Assert.IsType<BinaryOperator>(Assert.Single(consumeCall.Arguments));
        var find = Assert.IsType<Call>(comparison.Left);

        Assert.Equal("xs", Assert.IsType<Identifier>(find.Arguments[0]).Name);
        Assert.DoesNotContain(find.Arguments, argument => argument is AnonymousFunction);
    }

    /// <summary>
    ///     The wrapper's parameter is a concrete function type rather than a bare type parameter, unlike an
    ///     earlier version of this test that wrapped <c>Result::ok</c> in a generic <c>id&lt;T&gt;</c> - passing
    ///     a still-generic macro reference through a second, unrelated generic function's own inference is a
    ///     type-checker gap (contextual typing does not propagate a type parameter's binding through a nested
    ///     generic call - see rbx-loom/loom, the <c>consume(id(Result::ok))</c> case), not something this test
    ///     is about. What it is about - a macro reference nested inside another call's own argument list still
    ///     being classified as a call and expanded, rather than becoming a lambda the outer call then calls a
    ///     second time (rbx-loom/loom#25) - only needs one level of nesting with a concrete callee type.
    /// </summary>
    [Fact]
    public void Generates_InvocationMacroReference_NestedInArgument()
    {
        const string source = """
            fn wrap(value: fn(value: number): Result<number, string>): fn(value: number): Result<number, string> -> value;
            declare fn consume(callback: fn(value: number): Result<number, string>): void;
            consume(wrap(Result::ok));
            """;

        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));

        var statement = Assert.IsType<ExpressionStatement>(luauTree.Statements.Last());
        var consumeCall = Assert.IsType<Call>(statement.Expression);
        var wrapCall = Assert.IsType<Call>(Assert.Single(consumeCall.Arguments));
        Assert.IsType<AnonymousFunction>(Assert.Single(wrapCall.Arguments));
    }

    [Theory]
    [InlineData("'420'", 420)]
    [InlineData("'69420'", 69420)]
    [InlineData("'1e+100'", 1e100)]
    [InlineData("'0xFF'", 255)]
    [InlineData("'  0x1A  '", 26)]
    public void Generates_GlobalInvocation_Number_FoldsLiteralString(string literal, double expected)
    {
        var source = $"number({literal})";
        var luauTree = Utility.GetLuauAST(source);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));
        Assert.Single(luauTree.Statements);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.Last());
        var numberLiteral = Assert.IsType<NumberLiteral>(variable.Initializer);
        Assert.Equal(expected, numberLiteral.Value);
    }

    [Fact]
    public void Generates_GlobalInvocation_Number_FoldsInvalidLiteralStringToNil()
    {
        const string source = "number('not a number')";
        var luauTree = Utility.GetLuauAST(source);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));
        Assert.Single(luauTree.Statements);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.Last());
        Assert.IsType<NilLiteral>(variable.Initializer);
    }

    [Fact]
    public void Generates_GlobalInvocation_Number_FallsBackForNonLiteral()
    {
        const string source = """
            let x = '420';
            number(x);
            """;

        var luauTree = Utility.GetLuauAST(source);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));

        var expressionStatement = Assert.IsType<ExpressionStatement>(luauTree.Statements.Last());
        var tonumberCall = Assert.IsType<Call>(expressionStatement.Expression);
        var identifier = Assert.IsType<Identifier>(tonumberCall.Callee);
        Assert.Equal("tonumber", identifier.Name);
        Assert.IsType<Identifier>(Assert.Single(tonumberCall.Arguments));
    }

    [Theory]
    [InlineData("69", "69")]
    [InlineData("69420", "69420")]
    [InlineData("1e100", "1e+100")]
    [InlineData("-5", "-5")]
    public void Generates_GlobalInvocation_String_FoldsLiteralNumber(string literal, string expected)
    {
        var source = $"string({literal})";
        var luauTree = Utility.GetLuauAST(source);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));
        Assert.Single(luauTree.Statements);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.Last());
        var stringLiteral = Assert.IsType<StringLiteral>(variable.Initializer);
        Assert.Equal(expected, stringLiteral.Value);
    }

    [Fact]
    public void Generates_GlobalInvocation_String_FallsBackForNonLiteral()
    {
        const string source = """
            let x = 69420;
            string(x);
            """;

        var luauTree = Utility.GetLuauAST(source);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));

        var expressionStatement = Assert.IsType<ExpressionStatement>(luauTree.Statements.Last());
        var tostringCall = Assert.IsType<Call>(expressionStatement.Expression);
        var identifier = Assert.IsType<Identifier>(tostringCall.Callee);
        Assert.Equal("tostring", identifier.Name);
        Assert.IsType<Identifier>(Assert.Single(tostringCall.Arguments));
    }

    [Fact]
    public void Generates_GlobalInvocation_NewInstance()
    {
        const string source = "new_instance::<Part>()";
        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));
        Assert.Single(luauTree.Statements);

        var expressionStatement = Assert.IsType<ExpressionStatement>(luauTree.Statements.Last());
        var call = Assert.IsType<Call>(expressionStatement.Expression);
        Assert.False(call.IsMethod);
        var callee = Assert.IsType<PropertyAccess>(call.Callee);
        Assert.Equal("Instance", Assert.IsType<Identifier>(callee.Target).Name);
        Assert.Equal("new", Assert.Single(callee.Names));
        Assert.Equal("Part", Assert.IsType<StringLiteral>(Assert.Single(call.Arguments)).Value);
    }

    [Fact]
    public void Generates_GlobalInvocation_GetService()
    {
        const string source = "get_service::<Workspace>()";
        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));
        Assert.Single(luauTree.Statements);

        var expressionStatement = Assert.IsType<ExpressionStatement>(luauTree.Statements.Last());
        var call = Assert.IsType<Call>(expressionStatement.Expression);
        Assert.True(call.IsMethod);
        var callee = Assert.IsType<PropertyAccess>(call.Callee);
        Assert.Equal("game", Assert.IsType<Identifier>(callee.Target).Name);
        Assert.Equal("GetService", Assert.Single(callee.Names));
        Assert.Equal("Workspace", Assert.IsType<StringLiteral>(Assert.Single(call.Arguments)).Value);
    }

    [Fact]
    public void Generates_ResultStatic_Ok()
    {
        const string source = "Result::ok(69)";
        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));
        Assert.Single(luauTree.Statements);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.First());
        var table = Assert.IsType<Table>(variable.Initializer);
        Assert.Equal(2, table.Initializers.Count);

        var okInit = Assert.IsType<PropertyTableInitializer>(table.Initializers[0]);
        var valueInit = Assert.IsType<PropertyTableInitializer>(table.Initializers[1]);
        Assert.Equal("ok", okInit.PropertyName);
        Assert.Equal("value", valueInit.PropertyName);

        var okValue = Assert.IsType<BooleanLiteral>(okInit.Value);
        var value = Assert.IsType<NumberLiteral>(valueInit.Value);
        Assert.True(okValue.Value);
        Assert.Equal(69, value.Value);
    }

    [Fact]
    public void Generates_ResultStatic_Err()
    {
        const string source = "Result::err('stupid program')";
        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));
        Assert.Single(luauTree.Statements);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.First());
        var table = Assert.IsType<Table>(variable.Initializer);
        Assert.Equal(2, table.Initializers.Count);

        var okInit = Assert.IsType<PropertyTableInitializer>(table.Initializers[0]);
        var errorInit = Assert.IsType<PropertyTableInitializer>(table.Initializers[1]);
        Assert.Equal("ok", okInit.PropertyName);
        Assert.Equal("error", errorInit.PropertyName);

        var okValue = Assert.IsType<BooleanLiteral>(okInit.Value);
        var errorValue = Assert.IsType<StringLiteral>(errorInit.Value);
        Assert.False(okValue.Value);
        Assert.Equal("stupid program", errorValue.Value);
    }

    [Fact]
    public void Generates_Complex_Nested_Method()
    {
        const string source = """
            interface A { a: number[]; } 
            interface B { b: A; } 
            interface C { c: B; } 
            let object = new C { c: new B { b: new A { a: [1, 2, 3] } } };
            let _ = object["c"].b.a.join()
            """;

        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));
        Assert.Equal(5, luauTree.Statements.Count);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.Last());
        var concatCall = Assert.IsType<Call>(variable.Initializer);
        var concat = Assert.IsType<PropertyAccess>(concatCall.Callee);
        var tableIdentifier = Assert.IsType<Identifier>(concat.Target);
        Assert.Equal("table", tableIdentifier.Name);
        Assert.Equal("concat", Assert.Single(concat.Names));

        var propertyAccess = Assert.IsType<PropertyAccess>(Assert.Single(concatCall.Arguments));
        Assert.IsType<PropertyAccess>(propertyAccess.Target);
        Assert.Equal(2, propertyAccess.Names.Count);
        Assert.Equal("b", propertyAccess.Names.First());
        Assert.Equal("a", propertyAccess.Names.Last());
    }

    [Fact]
    public void Generates_Complex_Nested_Property()
    {
        const string source = """
            interface A { a: number[]; } 
            interface B { b: A; } 
            interface C { c: B; } 
            let object = new C { c: new B { b: new A { a: [1, 2, 3] } } };
            let _ = object["c"].b.a.length
            """;

        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));
        Assert.Equal(5, luauTree.Statements.Count);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.Last());
        var unaryOperator = Assert.IsType<UnaryOperator>(variable.Initializer);
        var propertyAccess = Assert.IsType<PropertyAccess>(unaryOperator.Operand);
        var secondPropertyAccess = Assert.IsType<PropertyAccess>(propertyAccess.Target);
        Assert.IsType<PropertyAccess>(secondPropertyAccess.Target);
        Assert.Equal("#", unaryOperator.Operator);
        Assert.Equal("b", Assert.Single(secondPropertyAccess.Names));
        Assert.Equal("a", Assert.Single(propertyAccess.Names));
    }

    [Theory]
    [InlineData("let _ = c.a.join()")]
    [InlineData("let _ = c.a['join']()")]
    [InlineData("let _ = (c.a).join()", null)]
    [InlineData("let _ = c.a.join(', ')", ", ")]
    [InlineData("let _ = c.a['join'](', ')", ", ")]
    [InlineData("let _ = (c.a).join(', ')", ", ")]
    public void Generates_Array_Join_Nested(string source, string? separator = null)
    {
        var fullSource = $"interface C {{ a: number[]; }} let c = new C {{ a: [1, 2, 3] }}; {source}";
        var luauTree = Utility.GetLuauAST(fullSource, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(fullSource, true));
        Assert.Equal(3, luauTree.Statements.Count);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.Last());
        var concatCall = Assert.IsType<Call>(variable.Initializer);
        var concat = Assert.IsType<PropertyAccess>(concatCall.Callee);
        var tableIdentifier = Assert.IsType<Identifier>(concat.Target);
        Assert.Equal("table", tableIdentifier.Name);
        Assert.Equal("concat", Assert.Single(concat.Names));
        Assert.Equal(separator == null ? 1 : 2, concatCall.Arguments.Count);

        var access = Assert.IsType<PropertyAccess>(concatCall.Arguments.First());
        var containerIdentifier = Assert.IsType<Identifier>(access.Target);
        Assert.Equal("c", containerIdentifier.Name);
        Assert.Equal("a", Assert.Single(access.Names));

        if (separator == null) return;
        var separatorArgument = Assert.IsType<StringLiteral>(concatCall.Arguments.Last());
        Assert.Equal(separator, separatorArgument.Value);
    }

    [Theory]
    [InlineData("let _ = a.join()")]
    [InlineData("let _ = a['join']()")]
    [InlineData("let _ = (a).join()")]
    [InlineData("let _ = a.join(', ')", ", ")]
    [InlineData("let _ = a['join'](', ')", ", ")]
    [InlineData("let _ = (a).join(', ')", ", ")]
    public void Generates_Array_Join(string source, string? separator = null)
    {
        var fullSource = $"let a = [1, 2, 3]; {source}";
        var luauTree = Utility.GetLuauAST(fullSource, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(fullSource, true));
        Assert.Equal(2, luauTree.Statements.Count);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.Last());
        var concatCall = Assert.IsType<Call>(variable.Initializer);
        var concat = Assert.IsType<PropertyAccess>(concatCall.Callee);
        var tableIdentifier = Assert.IsType<Identifier>(concat.Target);
        Assert.Equal("table", tableIdentifier.Name);
        Assert.Equal("concat", Assert.Single(concat.Names));
        Assert.Equal(separator == null ? 1 : 2, concatCall.Arguments.Count);

        var arrayIdentifier = Assert.IsType<Identifier>(concatCall.Arguments.First());
        Assert.Equal("a", arrayIdentifier.Name);

        if (separator == null) return;
        var separatorArgument = Assert.IsType<StringLiteral>(concatCall.Arguments.Last());
        Assert.Equal(separator, separatorArgument.Value);
    }

    [Theory]
    [InlineData("c.a.length")]
    [InlineData("c.a['length']")]
    [InlineData("let _ = (c.a).length", true)]
    public void Generates_Array_Length_Nested(string source, bool parenthesized = false)
    {
        var fullSource = $"interface C {{ a: number[]; }} let c = new C {{ a: [1, 2, 3] }}; {source}";
        var luauTree = Utility.GetLuauAST(fullSource, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(fullSource, true));
        Assert.Equal(3, luauTree.Statements.Count);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.Last());
        var unaryOperator = Assert.IsType<UnaryOperator>(variable.Initializer);
        var operand = unaryOperator.Operand;
        if (parenthesized)
            operand = Assert.IsType<Parenthesized>(operand).Expression;

        var access = Assert.IsType<PropertyAccess>(operand);
        Assert.IsType<Identifier>(access.Target);
        Assert.Single(access.Names);
        Assert.Equal("#", unaryOperator.Operator);
    }

    [Theory]
    [InlineData("a.push(4)", "insert", 2)]
    [InlineData("a['push'](4)", "insert", 2)]
    [InlineData("a.insert(1, 4)", "insert", 3)]
    [InlineData("a.pop()", "remove", 1)]
    [InlineData("a.remove(1)", "remove", 2)]
    [InlineData("a.index_of(2)", "find", 2)]
    public void Generates_Array_Mutation_And_Search(string call, string luauFunction, int argumentCount)
    {
        var source = $"let a = mut [1, 2, 3]; {call}";
        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));
        Assert.Equal(2, luauTree.Statements.Count);

        var statement = Assert.IsType<ExpressionStatement>(luauTree.Statements.Last());
        var tableCall = Assert.IsType<Call>(statement.Expression);
        var callee = Assert.IsType<PropertyAccess>(tableCall.Callee);
        var tableIdentifier = Assert.IsType<Identifier>(callee.Target);
        Assert.Equal("table", tableIdentifier.Name);
        Assert.Equal(luauFunction, Assert.Single(callee.Names));
        Assert.Equal(argumentCount, tableCall.Arguments.Count);
        Assert.Equal("a", Assert.IsType<Identifier>(tableCall.Arguments.First()).Name);
    }

    [Theory]
    [InlineData("a.has(2)")]
    [InlineData("a['has'](2)")]
    public void Generates_Array_Has(string call)
    {
        var source = $"let a = [1, 2, 3]; {call}";
        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));
        Assert.Equal(2, luauTree.Statements.Count);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.Last());
        var binaryOperator = Assert.IsType<BinaryOperator>(variable.Initializer);
        Assert.Equal("~=", binaryOperator.Operator);
        Assert.IsType<NilLiteral>(binaryOperator.Right);

        var tableCall = Assert.IsType<Call>(binaryOperator.Left);
        var callee = Assert.IsType<PropertyAccess>(tableCall.Callee);
        Assert.Equal("table", Assert.IsType<Identifier>(callee.Target).Name);
        Assert.Equal("find", Assert.Single(callee.Names));
        Assert.Equal("a", Assert.IsType<Identifier>(tableCall.Arguments.First()).Name);
    }

    [Theory]
    [InlineData("let a = mut [1, 2, 3]; a.clear()", "clear", 1)]
    [InlineData("let a = [1, 2, 3]; let b = a.clone();", "clone", 1)]
    public void Generates_Array_SingleTableCall(string source, string luauFunction, int argumentCount)
    {
        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));

        var tableCall = luauTree.Statements.Last() switch
        {
            ExpressionStatement statement => Assert.IsType<Call>(statement.Expression),
            ConstVariable variable => Assert.IsType<Call>(variable.Initializer),
            var other => throw new Xunit.Sdk.XunitException($"Unexpected statement kind: {other}")
        };

        var callee = Assert.IsType<PropertyAccess>(tableCall.Callee);
        Assert.Equal("table", Assert.IsType<Identifier>(callee.Target).Name);
        Assert.Equal(luauFunction, Assert.Single(callee.Names));
        Assert.Equal(argumentCount, tableCall.Arguments.Count);
        Assert.Equal("a", Assert.IsType<Identifier>(tableCall.Arguments.First()).Name);
    }

    /// <summary>
    ///     Used as a bare statement, the lookup and the removal are a prereq and a postreq around the
    ///     macro's own returned identifier - the elided-placeholder pass in <c>LuauGenerator</c> then
    ///     drops the trailing binding, so no third statement carrying the found index survives.
    /// </summary>
    [Fact]
    public void Generates_Array_RemoveValue_AsABareStatement()
    {
        const string source = "let a = mut [1, 2, 3]; a.remove_value(2)";
        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));
        Assert.Equal(3, luauTree.Statements.Count);

        var lookup = Assert.IsType<ConstVariable>(luauTree.Statements[1]);
        var find = Assert.IsType<Call>(lookup.Initializer);
        Assert.Equal("find", Assert.Single(Assert.IsType<PropertyAccess>(find.Callee).Names));

        var guard = Assert.IsType<IfStatement>(luauTree.Statements[2]);
        var removal = Assert.IsType<ExpressionStatement>(Assert.Single(guard.ThenBranch.Statements));
        var remove = Assert.IsType<Call>(removal.Expression);
        Assert.Equal("remove", Assert.Single(Assert.IsType<PropertyAccess>(remove.Callee).Names));
    }

    [Fact]
    public void ImmutableArray_DoesNotSupport_Mutation()
    {
        const string source = "let a = [1, 2, 3]; a.push(4)";
        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Assert.NotEmpty(diagnostics.Set);
    }

    [Theory]
    [InlineData("a.length")]
    [InlineData("a['length']")]
    [InlineData("let _ = (a).length", true)]
    public void Generates_Array_Length(string source, bool parenthesized = false)
    {
        var fullSource = $"let a = [1, 2, 3]; {source}";
        var luauTree = Utility.GetLuauAST(fullSource, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(fullSource, true));
        Assert.Equal(2, luauTree.Statements.Count);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.Last());
        var unaryOperator = Assert.IsType<UnaryOperator>(variable.Initializer);
        var operand = unaryOperator.Operand;
        if (parenthesized)
            operand = Assert.IsType<Parenthesized>(operand).Expression;

        Assert.Equal("a", Assert.IsType<Identifier>(operand).Name);
        Assert.Equal("#", unaryOperator.Operator);
    }

    [Fact]
    public void Generates_Array_Length_ThroughOptionalChain()
    {
        const string source = "let a: number[]? = [1, 2, 3]; a?.length";
        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));
        Assert.Equal(4, luauTree.Statements.Count);

        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[2]);
        var thenAssignment = Assert.IsType<BinaryOperator>(Assert.IsType<ExpressionStatement>(ifStatement.ThenBranch.Statements[0]).Expression);
        var unaryOperator = Assert.IsType<UnaryOperator>(thenAssignment.Right);
        Assert.Equal("#", unaryOperator.Operator);
        Assert.Equal("a", Assert.IsType<Identifier>(unaryOperator.Operand).Name);
    }

    [Fact]
    public void Generates_Array_Push_ThroughOptionalChain()
    {
        const string source = "let a: number[mut]? = mut [1, 2, 3]; a?.push(4)";
        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));
        Assert.Equal(4, luauTree.Statements.Count);

        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[2]);
        var thenAssignment = Assert.IsType<BinaryOperator>(Assert.IsType<ExpressionStatement>(ifStatement.ThenBranch.Statements[0]).Expression);
        var tableCall = Assert.IsType<Call>(thenAssignment.Right);
        var callee = Assert.IsType<PropertyAccess>(tableCall.Callee);
        Assert.Equal("table", Assert.IsType<Identifier>(callee.Target).Name);
        Assert.Equal("insert", Assert.Single(callee.Names));
        Assert.Equal("a", Assert.IsType<Identifier>(tableCall.Arguments.First()).Name);
    }

    [Fact]
    public void Generates_Array_Length_ThroughNestedOptionalChain()
    {
        const string source = """
            interface Foo {
                mut bar: number[]?;
            }
            let foo: Foo? = none as never as Foo?;
            foo?.bar?.length
            """;

        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));

        var outerIf = Assert.IsType<IfStatement>(luauTree.Statements[^2]);
        
        var cachedLink = Assert.IsType<ConstVariable>(outerIf.ThenBranch.Statements[0]);
        Assert.Equal("_target", cachedLink.Name);
        var cachedLinkAccess = Assert.IsType<PropertyAccess>(cachedLink.Initializer);
        Assert.Equal("bar", Assert.Single(cachedLinkAccess.Names));
        Assert.Equal("foo", Assert.IsType<Identifier>(cachedLinkAccess.Target).Name);

        var innerIf = Assert.IsType<IfStatement>(outerIf.ThenBranch.Statements[1]);
        var innerThenAssignment = Assert.IsType<BinaryOperator>(Assert.IsType<ExpressionStatement>(innerIf.ThenBranch.Statements[0]).Expression);
        var unaryOperator = Assert.IsType<UnaryOperator>(innerThenAssignment.Right);
        Assert.Equal("#", unaryOperator.Operator);
        Assert.Equal("_target", Assert.IsType<Identifier>(unaryOperator.Operand).Name);
    }

    [Fact]
    public void Generates_Array_Length_ThroughOptionalChain_MixedWithPlainAccess()
    {
        const string source = """
            interface Foo {
                mut bar: number[];
            }
            let foo: Foo? = none as never as Foo?;
            foo?.bar.length
            """;

        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));

        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[^2]);
        var thenAssignment = Assert.IsType<BinaryOperator>(Assert.IsType<ExpressionStatement>(ifStatement.ThenBranch.Statements[0]).Expression);
        var unaryOperator = Assert.IsType<UnaryOperator>(thenAssignment.Right);
        Assert.Equal("#", unaryOperator.Operator);

        var operand = Assert.IsType<PropertyAccess>(unaryOperator.Operand);
        Assert.Equal("bar", Assert.Single(operand.Names));
        Assert.Equal("foo", Assert.IsType<Identifier>(operand.Target).Name);
    }

    [Fact]
    public void Generates_PropertyAccess_ThroughOptionalChain_WithoutMacro_UnaffectedByMacroLookup()
    {
        const string source = """
            interface Baz {
                mut name: string;
            }
            let b: Baz? = none as never as Baz?;
            b?.name
            """;

        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));

        var ifStatement = Assert.IsType<IfStatement>(luauTree.Statements[^2]);
        var thenAssignment = Assert.IsType<BinaryOperator>(Assert.IsType<ExpressionStatement>(ifStatement.ThenBranch.Statements[0]).Expression);
        var access = Assert.IsType<PropertyAccess>(thenAssignment.Right);
        Assert.Equal("name", Assert.Single(access.Names));
        Assert.Equal("b", Assert.IsType<Identifier>(access.Target).Name);
    }

    [Theory]
    [InlineData("(1..10).length")]
    [InlineData("(1..10)['length']")]
    public void Generates_Range_Length_Literal(string source)
    {
        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));
        Assert.Single(luauTree.Statements);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.First());
        var value = Assert.IsType<NumberLiteral>(variable.Initializer);
        Assert.Equal(10d, value.Value);
    }

    [Fact]
    public void Generates_Range_Clamp()
    {
        const string source = "let r = 1..10; r.clamp(69)";
        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));
        Assert.Equal(2, luauTree.Statements.Count);

        var expressionStatement = Assert.IsType<ExpressionStatement>(luauTree.Statements.Last());
        var clampCall = Assert.IsType<Call>(expressionStatement.Expression);
        var value = Assert.IsType<NumberLiteral>(clampCall.Arguments[0]);
        var minimum = Assert.IsType<PropertyAccess>(clampCall.Arguments[1]);
        var maximum = Assert.IsType<PropertyAccess>(clampCall.Arguments[2]);
        var clamp = Assert.IsType<PropertyAccess>(clampCall.Callee);
        var mathIdentifier = Assert.IsType<Identifier>(clamp.Target);
        Assert.Equal(3, clampCall.Arguments.Count);
        Assert.Single(clamp.Names);
        Assert.Equal("math", mathIdentifier.Name);
        Assert.Equal("clamp", clamp.Names.First());
        Assert.Equal(69d, value.Value);
        Assert.Single(minimum.Names);
        Assert.Single(maximum.Names);

        var rangeIdentifier = Assert.IsType<Identifier>(minimum.Target);
        var rangeIdentifier2 = Assert.IsType<Identifier>(maximum.Target);
        Assert.Equal("r", rangeIdentifier.Name);
        Assert.Equal("r", rangeIdentifier2.Name);
        Assert.Equal("minimum", minimum.Names.First());
        Assert.Equal("maximum", maximum.Names.First());
    }

    [Theory]
    [InlineData("69", 10)]
    [InlineData("5", 5)]
    [InlineData("0", 1)]
    [InlineData("-10", 1)]
    [InlineData("2 + 6 - 4", 4)]
    [InlineData("3.5 * 2.5", 8.75)]
    [InlineData("11 // 2", 5)]
    [InlineData("11 / 2", 5.5)]
    [InlineData("3 ^ 2", 9)]
    [InlineData("12 % 3", 1)]
    public void Generates_Range_Clamp_Literal(string toClamp, double expected)
    {
        var accessKinds = new List<string> { ".clamp", "['clamp']" };
        foreach (var access in accessKinds)
        {
            var source = $"(1..10){access}({toClamp})";
            var luauTree = Utility.GetLuauAST(source, true);
            Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));
            Assert.Single(luauTree.Statements);

            var variable = Assert.IsType<ConstVariable>(luauTree.Statements.First());
            var value = Assert.IsType<NumberLiteral>(variable.Initializer);
            Assert.Equal(expected, value.Value);
        }
    }

    [Theory]
    [InlineData("1", 2)]
    [InlineData("6", 5)]
    [InlineData("3", 3)]
    public void Generates_Range_Clamp_Literal_DescendingRange(string toClamp, double expected)
    {
        // a descending range literal ('5..2') folds to minimum > maximum, which used to make the constant
        // folder call Math.Clamp with its arguments out of order and throw - clamp still has to answer with
        // the same bounds regardless of which order the range was written in.
        var source = $"(5..2).clamp({toClamp})";
        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));
        Assert.Single(luauTree.Statements);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.First());
        var value = Assert.IsType<NumberLiteral>(variable.Initializer);
        Assert.Equal(expected, value.Value);
    }

    [Fact]
    public void Generates_Range_Length()
    {
        const string source = "let r = 1..10; r.length";
        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));
        Assert.Equal(2, luauTree.Statements.Count);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.Last());
        var binaryOperator = Assert.IsType<BinaryOperator>(variable.Initializer);
        var one = Assert.IsType<NumberLiteral>(binaryOperator.Left);
        var absCall = Assert.IsType<Call>(binaryOperator.Right);
        Assert.Single(absCall.Arguments);
        Assert.Equal("+", binaryOperator.Operator);

        var subtractionBinary = Assert.IsType<BinaryOperator>(absCall.Arguments.First());
        var maximumAccess = Assert.IsType<PropertyAccess>(subtractionBinary.Left);
        var minimumAccess = Assert.IsType<PropertyAccess>(subtractionBinary.Right);
        var rangeIdentifier = Assert.IsType<Identifier>(maximumAccess.Target);
        var rangeIdentifier2 = Assert.IsType<Identifier>(minimumAccess.Target);
        Assert.Equal("-", subtractionBinary.Operator);
        Assert.Equal("r", rangeIdentifier.Name);
        Assert.Equal("r", rangeIdentifier2.Name);
        Assert.Single(maximumAccess.Names);
        Assert.Single(minimumAccess.Names);
        Assert.Equal("maximum", maximumAccess.Names.First());
        Assert.Equal("minimum", minimumAccess.Names.First());

        var abs = Assert.IsType<PropertyAccess>(absCall.Callee);
        var mathIdentifier = Assert.IsType<Identifier>(abs.Target);
        Assert.Single(abs.Names);
        Assert.Equal("math", mathIdentifier.Name);
        Assert.Equal("abs", abs.Names.First());
        Assert.Equal(1d, one.Value);
    }

    [Fact]
    public void Generates_ArraySlice_RangeLiteral()
    {
        var luauTree = Utility.GetLuauAST("let arr = [1,2,3]; arr[1..2]", true);
        Assert.Equal(3, luauTree.Statements.Count);

        var arrayVariable = Assert.IsType<ConstVariable>(luauTree.Statements[0]);
        Assert.IsType<Table>(arrayVariable.Initializer);

        var lengthVariable = Assert.IsType<ConstVariable>(luauTree.Statements[1]);
        Assert.Equal("_length", lengthVariable.Name);
        Assert.IsType<UnaryOperator>(lengthVariable.Initializer);

        var result = Assert.IsType<ExpressionStatement>(luauTree.Statements[2]);
        var call = Assert.IsType<Call>(result.Expression);
        var propertyAccess = Assert.IsType<PropertyAccess>(call.Callee);
        var target = Assert.IsType<Identifier>(propertyAccess.Target);
        Assert.Equal("table", target.Name);
        Assert.Single(propertyAccess.Names);
        Assert.Equal("move", propertyAccess.Names[0]);

        Assert.Equal(5, call.Arguments.Count);
        Assert.IsType<Identifier>(call.Arguments[0]);
        var start = Assert.IsType<Call>(call.Arguments[1]);
        var end = Assert.IsType<Call>(call.Arguments[2]);
        Assert.Equal(3, start.Arguments.Count);
        Assert.Equal(3, end.Arguments.Count);
        Assert.IsType<NumberLiteral>(start.Arguments.First());
        Assert.IsType<NumberLiteral>(end.Arguments.First());

        var startCall = Assert.IsType<PropertyAccess>(start.Callee);
        var startTarget = Assert.IsType<Identifier>(startCall.Target);
        Assert.Equal("math", startTarget.Name);
        Assert.Equal("clamp", startCall.Names[0]);
        Assert.Equal(3, start.Arguments.Count);
        Assert.IsType<NumberLiteral>(start.Arguments[0]);
        Assert.IsType<NumberLiteral>(start.Arguments[1]);
        Assert.IsType<Identifier>(start.Arguments[2]);

        Assert.IsType<NumberLiteral>(call.Arguments[3]);
        Assert.IsType<Table>(call.Arguments[4]);
    }

    [Fact]
    public void Generates_StringSlice_RangeLiteral()
    {
        var luauTree = Utility.GetLuauAST("let s = 'abc'; s[1..2]", true);
        Assert.Equal(3, luauTree.Statements.Count);

        var stringVariable = Assert.IsType<ConstVariable>(luauTree.Statements[0]);
        Assert.IsType<StringLiteral>(stringVariable.Initializer);

        var lengthVariable = Assert.IsType<ConstVariable>(luauTree.Statements[1]);
        Assert.Equal("_length", lengthVariable.Name);
        Assert.IsType<UnaryOperator>(lengthVariable.Initializer);

        var result = Assert.IsType<ExpressionStatement>(luauTree.Statements[2]);
        var call = Assert.IsType<Call>(result.Expression);
        var propertyAccess = Assert.IsType<PropertyAccess>(call.Callee);
        var target = Assert.IsType<Identifier>(propertyAccess.Target);
        Assert.Equal("string", target.Name);
        Assert.Single(propertyAccess.Names);
        Assert.Equal("sub", propertyAccess.Names[0]);

        Assert.Equal(3, call.Arguments.Count);
        Assert.IsType<Identifier>(call.Arguments[0]);
        var start = Assert.IsType<Call>(call.Arguments[1]);
        var end = Assert.IsType<Call>(call.Arguments[2]);
        Assert.Equal(3, start.Arguments.Count);
        Assert.Equal(3, end.Arguments.Count);
        Assert.IsType<NumberLiteral>(start.Arguments.First());
        Assert.IsType<NumberLiteral>(end.Arguments.First());

        var startCall = Assert.IsType<PropertyAccess>(start.Callee);
        var startTarget = Assert.IsType<Identifier>(startCall.Target);
        Assert.Equal("math", startTarget.Name);
        Assert.Equal("clamp", startCall.Names[0]);
        Assert.Equal(3, start.Arguments.Count);
        Assert.IsType<NumberLiteral>(start.Arguments[0]);
        Assert.IsType<NumberLiteral>(start.Arguments[1]);
        Assert.IsType<Identifier>(start.Arguments[2]);
    }

    [Fact]
    public void Generates_ArraySlice_RangeVariable()
    {
        var luauTree = Utility.GetLuauAST("let r = 1..5; let arr = [1,2,3,4,5]; arr[r]", true);
        Assert.Equal(4, luauTree.Statements.Count);

        var rangeVariable = Assert.IsType<ConstVariable>(luauTree.Statements[0]);
        var arrayVariable = Assert.IsType<ConstVariable>(luauTree.Statements[1]);
        var lengthVariable = Assert.IsType<ConstVariable>(luauTree.Statements[2]);
        var result = Assert.IsType<ExpressionStatement>(luauTree.Statements[3]);
        Assert.IsType<Table>(rangeVariable.Initializer);
        Assert.IsType<Table>(arrayVariable.Initializer);

        var lengthOp = Assert.IsType<UnaryOperator>(lengthVariable.Initializer);
        Assert.Equal("#", lengthOp.Operator);

        var call = Assert.IsType<Call>(result.Expression);
        var propertyAccess = Assert.IsType<PropertyAccess>(call.Callee);
        var accessTarget = Assert.IsType<Identifier>(propertyAccess.Target);
        Assert.Equal("table", accessTarget.Name);
        Assert.Equal("move", propertyAccess.Names[0]);

        var start = Assert.IsType<Call>(call.Arguments[1]);
        var end = Assert.IsType<Call>(call.Arguments[2]);
        var startCallee = Assert.IsType<PropertyAccess>(start.Callee);
        var endCallee = Assert.IsType<PropertyAccess>(end.Callee);
        var startTarget = Assert.IsType<Identifier>(startCallee.Target);
        var endTarget = Assert.IsType<Identifier>(endCallee.Target);
        Assert.Equal("math", startTarget.Name);
        Assert.Equal("math", endTarget.Name);
        Assert.Equal("clamp", startCallee.Names[0]);
        Assert.Equal("clamp", endCallee.Names[0]);

        var minAccess = Assert.IsType<PropertyAccess>(start.Arguments[0]);
        var maxAccess = Assert.IsType<PropertyAccess>(end.Arguments[0]);
        var minTarget = Assert.IsType<Identifier>(minAccess.Target);
        var maxTarget = Assert.IsType<Identifier>(maxAccess.Target);
        Assert.Equal("r", minTarget.Name);
        Assert.Equal("r", maxTarget.Name);
        Assert.Equal("minimum", minAccess.Names[0]);
        Assert.Equal("maximum", maxAccess.Names[0]);
    }

    [Fact]
    public void Generates_StringSlice_RangeVariable()
    {
        var luauTree = Utility.GetLuauAST("let r = 1..5; let s = 'abcdef'; s[r]", true);
        Assert.Equal(4, luauTree.Statements.Count);

        var rangeVariable = Assert.IsType<ConstVariable>(luauTree.Statements[0]);
        var stringVariable = Assert.IsType<ConstVariable>(luauTree.Statements[1]);
        var lengthVariable = Assert.IsType<ConstVariable>(luauTree.Statements[2]);
        var result = Assert.IsType<ExpressionStatement>(luauTree.Statements[3]);
        Assert.IsType<Table>(rangeVariable.Initializer);
        Assert.IsType<StringLiteral>(stringVariable.Initializer);

        var lengthOp = Assert.IsType<UnaryOperator>(lengthVariable.Initializer);
        Assert.Equal("#", lengthOp.Operator);

        var call = Assert.IsType<Call>(result.Expression);
        var propertyAccess = Assert.IsType<PropertyAccess>(call.Callee);
        var accessTarget = Assert.IsType<Identifier>(propertyAccess.Target);
        Assert.Equal("string", accessTarget.Name);
        Assert.Equal("sub", propertyAccess.Names[0]);

        var start = Assert.IsType<Call>(call.Arguments[1]);
        var end = Assert.IsType<Call>(call.Arguments[2]);
        var startCallee = Assert.IsType<PropertyAccess>(start.Callee);
        var endCallee = Assert.IsType<PropertyAccess>(end.Callee);
        var startTarget = Assert.IsType<Identifier>(startCallee.Target);
        var endTarget = Assert.IsType<Identifier>(endCallee.Target);
        Assert.Equal("math", startTarget.Name);
        Assert.Equal("math", endTarget.Name);
        Assert.Equal("clamp", startCallee.Names[0]);
        Assert.Equal("clamp", endCallee.Names[0]);

        var minAccess = Assert.IsType<PropertyAccess>(start.Arguments[0]);
        var maxAccess = Assert.IsType<PropertyAccess>(end.Arguments[0]);
        var minTarget = Assert.IsType<Identifier>(minAccess.Target);
        var maxTarget = Assert.IsType<Identifier>(maxAccess.Target);
        Assert.Equal("r", minTarget.Name);
        Assert.Equal("r", maxTarget.Name);
        Assert.Equal("minimum", minAccess.Names[0]);
        Assert.Equal("maximum", maxAccess.Names[0]);
    }

    [Fact]
    public void Generates_StringSlice_Character()
    {
        var luauTree = Utility.GetLuauAST("let s = 'abc'; s[1]", true);
        Assert.Equal(2, luauTree.Statements.Count);

        var stringVariable = Assert.IsType<ConstVariable>(luauTree.Statements[0]);
        Assert.IsType<StringLiteral>(stringVariable.Initializer);

        var result = Assert.IsType<ExpressionStatement>(luauTree.Statements[1]);
        var call = Assert.IsType<Call>(result.Expression);
        var propertyAccess = Assert.IsType<PropertyAccess>(call.Callee);
        var target = Assert.IsType<Identifier>(propertyAccess.Target);
        Assert.Equal("string", target.Name);
        Assert.Single(propertyAccess.Names);
        Assert.Equal("sub", propertyAccess.Names[0]);
        Assert.Equal(3, call.Arguments.Count);
        Assert.IsType<Identifier>(call.Arguments[0]);

        var start = Assert.IsType<NumberLiteral>(call.Arguments[1]);
        var end = Assert.IsType<NumberLiteral>(call.Arguments[2]);
        Assert.Equal(1, start.Value);
        Assert.Equal(1, end.Value);
    }

    [Theory]
    [InlineData("s.length")]
    [InlineData("s['length']")]
    public void Generates_String_Length(string access)
    {
        var source = $"let s = 'abc'; {access}";
        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));
        Assert.Equal(2, luauTree.Statements.Count);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.Last());
        var unaryOperator = Assert.IsType<UnaryOperator>(variable.Initializer);
        Assert.Equal("#", unaryOperator.Operator);
        Assert.Equal("s", Assert.IsType<Identifier>(unaryOperator.Operand).Name);
    }

    [Theory]
    [InlineData("upper")]
    [InlineData("lower")]
    [InlineData("reverse")]
    public void Generates_String_UnaryLibraryCall(string methodName)
    {
        var source = $"let s = 'abc'; s.{methodName}()";
        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));
        Assert.Equal(2, luauTree.Statements.Count);

        var statement = Assert.IsType<ExpressionStatement>(luauTree.Statements.Last());
        var call = Assert.IsType<Call>(statement.Expression);
        var callee = Assert.IsType<PropertyAccess>(call.Callee);
        Assert.Equal("string", Assert.IsType<Identifier>(callee.Target).Name);
        Assert.Equal(methodName, Assert.Single(callee.Names));
        Assert.Equal("s", Assert.IsType<Identifier>(Assert.Single(call.Arguments)).Name);
    }

    [Theory]
    [InlineData("let _ = s.split()", 1)]
    [InlineData("let _ = s.split(',')", 2)]
    public void Generates_String_Split(string statementSource, int argumentCount)
    {
        var source = $"let s = 'a,b,c'; {statementSource}";
        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.Last());
        var call = Assert.IsType<Call>(variable.Initializer);
        var callee = Assert.IsType<PropertyAccess>(call.Callee);
        Assert.Equal("string", Assert.IsType<Identifier>(callee.Target).Name);
        Assert.Equal("split", Assert.Single(callee.Names));
        Assert.Equal(argumentCount, call.Arguments.Count);
        Assert.Equal("s", Assert.IsType<Identifier>(call.Arguments.First()).Name);
    }

    [Fact]
    public void Generates_String_Repeat()
    {
        const string source = "let s = 'ab'; s.repeat(3)";
        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));

        var statement = Assert.IsType<ExpressionStatement>(luauTree.Statements.Last());
        var call = Assert.IsType<Call>(statement.Expression);
        var callee = Assert.IsType<PropertyAccess>(call.Callee);
        Assert.Equal("string", Assert.IsType<Identifier>(callee.Target).Name);
        Assert.Equal("rep", Assert.Single(callee.Names));
        Assert.Equal(2, call.Arguments.Count);
        Assert.Equal(3, Assert.IsType<NumberLiteral>(call.Arguments.Last()).Value);
    }

    [Fact]
    public void Generates_String_Byte()
    {
        const string source = "let s = 'ab'; s.byte()";
        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));

        var statement = Assert.IsType<ExpressionStatement>(luauTree.Statements.Last());
        var call = Assert.IsType<Call>(statement.Expression);
        var callee = Assert.IsType<PropertyAccess>(call.Callee);
        Assert.Equal("string", Assert.IsType<Identifier>(callee.Target).Name);
        Assert.Equal("byte", Assert.Single(callee.Names));
        Assert.Equal("s", Assert.IsType<Identifier>(Assert.Single(call.Arguments)).Name);
    }

    [Fact]
    public void Generates_String_Byte_WithIndexArgument()
    {
        const string source = "let s = 'ab'; s.byte(2)";
        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));

        var statement = Assert.IsType<ExpressionStatement>(luauTree.Statements.Last());
        var call = Assert.IsType<Call>(statement.Expression);
        var callee = Assert.IsType<PropertyAccess>(call.Callee);
        Assert.Equal("string", Assert.IsType<Identifier>(callee.Target).Name);
        Assert.Equal("byte", Assert.Single(callee.Names));
        Assert.Equal(2, call.Arguments.Count);
        Assert.Equal("s", Assert.IsType<Identifier>(call.Arguments[0]).Name);
        Assert.Equal(2, Assert.IsType<NumberLiteral>(call.Arguments[1]).Value);
    }

    [Fact]
    public void Generates_String_Trim()
    {
        const string source = "let s = '  ab  '; s.trim()";
        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.Last());
        var parenthesized = Assert.IsType<Parenthesized>(variable.Initializer);
        var call = Assert.IsType<Call>(parenthesized.Expression);
        var callee = Assert.IsType<PropertyAccess>(call.Callee);
        Assert.Equal("string", Assert.IsType<Identifier>(callee.Target).Name);
        Assert.Equal("gsub", Assert.Single(callee.Names));
        Assert.Equal(3, call.Arguments.Count);
        Assert.Equal("s", Assert.IsType<Identifier>(call.Arguments[0]).Name);
    }

    [Fact]
    public void Generates_String_Replace()
    {
        const string source = "let s = 'abc'; s.replace('b', 'x')";
        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.Last());
        var parenthesized = Assert.IsType<Parenthesized>(variable.Initializer);
        var call = Assert.IsType<Call>(parenthesized.Expression);
        var callee = Assert.IsType<PropertyAccess>(call.Callee);
        Assert.Equal("string", Assert.IsType<Identifier>(callee.Target).Name);
        Assert.Equal("gsub", Assert.Single(callee.Names));
        Assert.Equal(3, call.Arguments.Count);
        Assert.Equal("s", Assert.IsType<Identifier>(call.Arguments[0]).Name);
        Assert.Equal("b", Assert.IsType<StringLiteral>(call.Arguments[1]).Value);
        Assert.Equal("x", Assert.IsType<StringLiteral>(call.Arguments[2]).Value);
    }

    [Fact]
    public void Generates_String_Has()
    {
        const string source = "let s = 'abc'; s.has('b')";
        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.Last());
        var binaryOperator = Assert.IsType<BinaryOperator>(variable.Initializer);
        Assert.Equal("~=", binaryOperator.Operator);
        Assert.IsType<NilLiteral>(binaryOperator.Right);

        var call = Assert.IsType<Call>(binaryOperator.Left);
        var callee = Assert.IsType<PropertyAccess>(call.Callee);
        Assert.Equal("string", Assert.IsType<Identifier>(callee.Target).Name);
        Assert.Equal("find", Assert.Single(callee.Names));
        Assert.Equal(4, call.Arguments.Count);
        Assert.Equal("s", Assert.IsType<Identifier>(call.Arguments[0]).Name);
        Assert.Equal("b", Assert.IsType<StringLiteral>(call.Arguments[1]).Value);
        Assert.True(Assert.IsType<BooleanLiteral>(call.Arguments[3]).Value);
    }

    [Fact]
    public void Generates_String_StartsWith()
    {
        const string source = "let s = 'abc'; s.starts_with('ab')";
        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));

        // A literal prefix is read where it stands and measured here, so nothing is hoisted for it.
        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.Last());
        var binaryOperator = Assert.IsType<BinaryOperator>(variable.Initializer);
        Assert.Equal("==", binaryOperator.Operator);
        Assert.Equal("ab", Assert.IsType<StringLiteral>(binaryOperator.Right).Value);

        var call = Assert.IsType<Call>(binaryOperator.Left);
        var callee = Assert.IsType<PropertyAccess>(call.Callee);
        Assert.Equal("string", Assert.IsType<Identifier>(callee.Target).Name);
        Assert.Equal("sub", Assert.Single(callee.Names));
        Assert.Equal(3, call.Arguments.Count);
        Assert.Equal("s", Assert.IsType<Identifier>(call.Arguments[0]).Name);
        Assert.Equal(1, Assert.IsType<NumberLiteral>(call.Arguments[1]).Value);
        Assert.Equal(2, Assert.IsType<NumberLiteral>(call.Arguments[2]).Value);
    }

    [Fact]
    public void Generates_String_StartsWith_NamingAPrefixThatDoesSomething()
    {
        const string source = "fn p(): string { return 'ab'; } let s = 'abc'; s.starts_with(p())";
        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));

        // 'sub' reads the prefix for its length and the comparison reads it again, so a call is named once.
        var prefixVariable = Assert.IsType<ConstVariable>(luauTree.Statements[^2]);
        Assert.IsType<Call>(prefixVariable.Initializer);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.Last());
        var binaryOperator = Assert.IsType<BinaryOperator>(variable.Initializer);
        Assert.Equal(prefixVariable.Name, Assert.IsType<Identifier>(binaryOperator.Right).Name);

        var call = Assert.IsType<Call>(binaryOperator.Left);
        var lengthOperator = Assert.IsType<UnaryOperator>(call.Arguments[2]);
        Assert.Equal("#", lengthOperator.Operator);
        Assert.Equal(prefixVariable.Name, Assert.IsType<Identifier>(lengthOperator.Operand).Name);
    }

    [Fact]
    public void Generates_String_EndsWith()
    {
        const string source = "let s = 'abc'; s.ends_with('bc')";
        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));

        // Both operands are already readable where they stand, so neither is hoisted.
        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.Last());
        var binaryOperator = Assert.IsType<BinaryOperator>(variable.Initializer);
        Assert.Equal("==", binaryOperator.Operator);
        Assert.Equal("bc", Assert.IsType<StringLiteral>(binaryOperator.Right).Value);

        var call = Assert.IsType<Call>(binaryOperator.Left);
        var callee = Assert.IsType<PropertyAccess>(call.Callee);
        Assert.Equal("string", Assert.IsType<Identifier>(callee.Target).Name);
        Assert.Equal("sub", Assert.Single(callee.Names));
        Assert.Equal("s", Assert.IsType<Identifier>(call.Arguments[0]).Name);

        // A known suffix length folds '#s - 2 + 1' down to the one subtraction it amounts to.
        var start = Assert.IsType<BinaryOperator>(call.Arguments[1]);
        Assert.Equal("-", start.Operator);
        Assert.Equal(1, Assert.IsType<NumberLiteral>(start.Right).Value);
        Assert.Equal("#", Assert.IsType<UnaryOperator>(start.Left).Operator);
    }

    [Fact]
    public void Generates_String_EndsWith_WithASingleCharacterSuffix()
    {
        const string source = "let s = 'abc'; s.ends_with('c')";
        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.Last());
        var binaryOperator = Assert.IsType<BinaryOperator>(variable.Initializer);
        var call = Assert.IsType<Call>(binaryOperator.Left);

        // '#s - 1 + 1' is '#s', so no arithmetic survives at all.
        var start = Assert.IsType<UnaryOperator>(call.Arguments[1]);
        Assert.Equal("#", start.Operator);
        Assert.Equal("s", Assert.IsType<Identifier>(start.Operand).Name);
    }

    [Theory]
    [InlineData("pi", "math.pi")]
    [InlineData("huge", "math.huge")]
    public void Generates_Math_Property(string propertyName, string source)
    {
        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.Last());
        var access = Assert.IsType<PropertyAccess>(variable.Initializer);
        Assert.Equal("math", Assert.IsType<Identifier>(access.Target).Name);
        Assert.Equal(propertyName, Assert.Single(access.Names));
    }

    [Theory]
    [InlineData("floor", "math.floor(1.5)")]
    [InlineData("sqrt", "math.sqrt(4)")]
    [InlineData("random", "math.random()")]
    [InlineData("random", "math.random(1, 6)")]
    [InlineData("abs", "math.abs(-1)")]
    [InlineData("acos", "math.acos(1)")]
    [InlineData("asin", "math.asin(1)")]
    [InlineData("atan", "math.atan(1)")]
    [InlineData("atan", "math.atan(1, 2)")]
    [InlineData("atan2", "math.atan2(1, 2)")]
    [InlineData("ceil", "math.ceil(1.2)")]
    [InlineData("clamp", "math.clamp(5, 0, 10)")]
    [InlineData("cos", "math.cos(1)")]
    [InlineData("cosh", "math.cosh(1)")]
    [InlineData("deg", "math.deg(1)")]
    [InlineData("exp", "math.exp(1)")]
    [InlineData("fmod", "math.fmod(5, 2)")]
    [InlineData("frexp", "math.frexp(8)")]
    [InlineData("ldexp", "math.ldexp(1, 2)")]
    [InlineData("log", "math.log(8)")]
    [InlineData("log", "math.log(8, 2)")]
    [InlineData("log10", "math.log10(100)")]
    [InlineData("max", "math.max(1, 2, 3)")]
    [InlineData("min", "math.min(1, 2, 3)")]
    [InlineData("modf", "math.modf(3.5)")]
    [InlineData("noise", "math.noise(1)")]
    [InlineData("noise", "math.noise(1, 2, 3)")]
    [InlineData("pow", "math.pow(2, 3)")]
    [InlineData("rad", "math.rad(180)")]
    [InlineData("randomseed", "math.randomseed(1)")]
    [InlineData("round", "math.round(1.5)")]
    [InlineData("sign", "math.sign(-5)")]
    [InlineData("sin", "math.sin(1)")]
    [InlineData("sinh", "math.sinh(1)")]
    [InlineData("tan", "math.tan(1)")]
    [InlineData("tanh", "math.tanh(1)")]
    public void Generates_Math_Invocation(string functionName, string source)
    {
        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));

        var statement = Assert.IsType<ExpressionStatement>(luauTree.Statements.Last());
        var call = Assert.IsType<Call>(statement.Expression);
        var callee = Assert.IsType<PropertyAccess>(call.Callee);
        Assert.Equal("math", Assert.IsType<Identifier>(callee.Target).Name);
        Assert.Equal(functionName, Assert.Single(callee.Names));
    }

    /// <summary>
    ///     A provider is written against the arity its intrinsic declares - <c>Range.clamp</c> takes exactly
    ///     one argument - and <c>CheckArity</c> reports exactly that when a call site gets it wrong. But
    ///     generation still runs after a type error (nothing in <c>Compiler.Analyze</c> stops it), so a
    ///     provider that assumed the type checker had already enforced arity - <c>Arguments.Single()</c> -
    ///     used to throw out of generation instead of leaving the arity diagnostic as the only complaint.
    /// </summary>
    [Theory]
    [InlineData("let r = 1..10; r.clamp();")]
    [InlineData("let r = 1..10; r.clamp(1, 2);")]
    [InlineData("Result::ok();")]
    [InlineData("Result::ok(1, 2);")]
    [InlineData("let s = [1, 2, 3].to_set(); s.has();")]
    [InlineData("let s = [1, 2, 3].to_set(); s.has(1, 2);")]
    public void Generates_AMacroBackedCall_GivenTheWrongArity_WithoutThrowing(string source)
    {
        var diagnostics = Utility.GetGeneratorDiagnostics(source, true);

        Assert.Contains(diagnostics.Set, d => d.Code == InternalCodes.InvocationArity);
        Assert.DoesNotContain(diagnostics.Set, d => d.Code == InternalCodes.CompilerError);
    }

    /// <summary>
    ///     Bug #231-10: FutureStaticMacroProvider (and the sibling Set/MutSet/Result providers) matched a
    ///     static member's receiver by bare InterfaceType.Name alone. Since the resolver deliberately lets a
    ///     module shadow an ambient name (see CLAUDE.md), a user's own 'interface Future' with a matching
    ///     'static Future { ... }' block was silently hijacked - 'Future::resolved(...)' emitted the runtime
    ///     macro's call instead of the user's own implementation. Requiring IsIntrinsic alongside the name
    ///     match means only the compiler's own Future is ever routed through the macro.
    /// </summary>
    [Fact]
    public void Generates_UserDeclaredFutureStatic_WithoutMacroHijacking()
    {
        var luau = Utility.GetLuauAST(
            """
            interface Future {
                value: number
                static resolved: Future
            }

            static Future {
                resolved = new Future { value: 5 };
            }

            let f = Future::resolved;
            """,
            true
        ).Render();

        Assert.Contains("Future.resolved", luau);
        Assert.DoesNotContain("future_resolved", luau);
    }
}