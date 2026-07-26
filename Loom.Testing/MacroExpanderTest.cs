using Loom.Core.Diagnostics;
using Loom.Luau.AST;

namespace Loom.Testing;

[Collection("Assembly")]
public class MacroExpanderTest
{
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
    [InlineData("is_a")]
    [InlineData("find_first_child_of_class")]
    [InlineData("find_first_child_which_is_a")]
    [InlineData("find_first_ancestor_of_class")]
    [InlineData("find_first_ancestor_which_is_a")]
    public void Generates_InstanceQuery(string methodName)
    {
        var source = $"let x = get_service::<Workspace>().{methodName}::<Part>()";
        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.Last());
        var call = Assert.IsType<Call>(variable.Initializer);
        Assert.Equal("Part", Assert.IsType<StringLiteral>(Assert.Single(call.Arguments)).Value);
    }

    [Fact]
    public void Generates_InvocationMacroReference_AsFunctionArgument_Ok()
    {
        const string source = """
            declare fn consume<T, E>(callback: fn(value: T): Result<T, E>): void;
            consume(Result.ok);
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
    public void Generates_InvocationMacroReference_NestedInArgument()
    {
        const string source = """
            fn id<T>(value: T): T -> value;
            declare fn consume<T, E>(callback: fn(value: T): Result<T, E>): void;
            consume(id(Result.ok));
            """;

        var luauTree = Utility.GetLuauAST(source, true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));

        var statement = Assert.IsType<ExpressionStatement>(luauTree.Statements.Last());
        var consumeCall = Assert.IsType<Call>(statement.Expression);
        var idCall = Assert.IsType<Call>(Assert.Single(consumeCall.Arguments));
        Assert.IsType<AnonymousFunction>(Assert.Single(idCall.Arguments));
    }

    [Fact]
    public void Generates_GlobalInvocation_Number_WithRadix()
    {
        const string source = "number('420', 16)";
        var luauTree = Utility.GetLuauAST(source);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));
        Assert.Single(luauTree.Statements);

        var expressionStatement = Assert.IsType<ExpressionStatement>(luauTree.Statements.Last());
        var tonumberCall = Assert.IsType<Call>(expressionStatement.Expression);
        var identifier = Assert.IsType<Identifier>(tonumberCall.Callee);
        Assert.Equal("tonumber", identifier.Name);

        Assert.Equal(2, tonumberCall.Arguments.Count);
        Assert.IsType<StringLiteral>(tonumberCall.Arguments.First());
        Assert.IsType<NumberLiteral>(tonumberCall.Arguments.Last());
    }

    [Fact]
    public void Generates_GlobalInvocation_Number()
    {
        const string source = "number('420')";
        var luauTree = Utility.GetLuauAST(source);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));
        Assert.Single(luauTree.Statements);

        var expressionStatement = Assert.IsType<ExpressionStatement>(luauTree.Statements.Last());
        var tonumberCall = Assert.IsType<Call>(expressionStatement.Expression);
        var identifier = Assert.IsType<Identifier>(tonumberCall.Callee);
        Assert.Equal("tonumber", identifier.Name);
        Assert.IsType<StringLiteral>(Assert.Single(tonumberCall.Arguments));
    }

    [Fact]
    public void Generates_GlobalInvocation_String()
    {
        const string source = "string(69)";
        var luauTree = Utility.GetLuauAST(source);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));
        Assert.Single(luauTree.Statements);

        var expressionStatement = Assert.IsType<ExpressionStatement>(luauTree.Statements.Last());
        var tostringCall = Assert.IsType<Call>(expressionStatement.Expression);
        var identifier = Assert.IsType<Identifier>(tostringCall.Callee);
        Assert.Equal("tostring", identifier.Name);
        Assert.IsType<NumberLiteral>(Assert.Single(tostringCall.Arguments));
    }

    [Fact]
    public void Generates_ResultStatic_Ok()
    {
        const string source = "Result.ok(69)";
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
        const string source = "Result.err('stupid program')";
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
        var luauTree = Utility.GetLuauAST($"interface C {{ a: number[]; }} let c = new C {{ a: [1, 2, 3] }}; {source}", true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));
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
        var luauTree = Utility.GetLuauAST($"let a = [1, 2, 3]; {source}", true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));
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
        var luauTree = Utility.GetLuauAST($"interface C {{ a: number[]; }} let c = new C {{ a: [1, 2, 3] }}; {source}", true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));
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
        var luauTree = Utility.GetLuauAST($"let a = [1, 2, 3]; {source}", true);
        Utility.AssertNoErrors(Utility.GetGeneratorDiagnostics(source, true));
        Assert.Equal(2, luauTree.Statements.Count);

        var variable = Assert.IsType<ConstVariable>(luauTree.Statements.Last());
        var unaryOperator = Assert.IsType<UnaryOperator>(variable.Initializer);
        var operand = unaryOperator.Operand;
        if (parenthesized)
            operand = Assert.IsType<Parenthesized>(operand).Expression;

        Assert.Equal("a", Assert.IsType<Identifier>(operand).Name);
        Assert.Equal("#", unaryOperator.Operator);
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
}