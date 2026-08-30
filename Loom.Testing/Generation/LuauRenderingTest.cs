using Loom.Luau;
using Loom.Luau.AST;
using BinaryOperator = Loom.Luau.AST.BinaryOperator;
using Identifier = Loom.Luau.AST.Identifier;
using PrimitiveType = Loom.Luau.AST.PrimitiveType;
using UnaryOperator = Loom.Luau.AST.UnaryOperator;

namespace Loom.Testing.Generation;

[Collection("Assembly")]
public class LuauRenderingTest
{
    [Fact]
    public void Renders_BlockComment() => Assert.Equal("[[\nhello!\n]]", new Comment("\nhello!\n").Render());

    [Fact]
    public void Renders_Comment() => Assert.Equal("-- hello!", new Comment(" hello!").Render());

    [Fact]
    public void Renders_NoOpStatement_AsEmptyString() => Assert.Equal("", new NoOpStatement().Render());

    [Fact]
    public void Renders_Simple_IfStatement_Break()
    {
        var chunk = new Chunk([new Break()]);
        var statement = new IfStatement(
            new Identifier("a"),
            chunk,
            [new ElseIfBranch(new Identifier("b"), chunk)],
            chunk
        );

        Assert.Equal(
            "if a then break elseif b then break else break end",
            statement.Render()
        );
    }

    [Fact]
    public void Renders_Simple_IfStatement_Continue()
    {
        var chunk = new Chunk([new Continue()]);
        var statement = new IfStatement(
            new Identifier("a"),
            chunk,
            [new ElseIfBranch(new Identifier("b"), chunk)],
            chunk
        );

        Assert.Equal(
            "if a then continue elseif b then continue else continue end",
            statement.Render()
        );
    }

    [Fact]
    public void Renders_Simple_IfStatement_Return()
    {
        var chunk = new Chunk([new Return()]);
        var statement = new IfStatement(
            new Identifier("a"),
            chunk,
            [new ElseIfBranch(new Identifier("b"), chunk)],
            chunk
        );

        Assert.Equal(
            "if a then return elseif b then return else return end",
            statement.Render()
        );
    }

    [Fact]
    public void Renders_Do() =>
        Assert.Equal(
            "do\n  print(\"hi\")\nend",
            new Do(new Chunk([new ExpressionStatement(new Call(new Identifier("print"), [new StringLiteral("hi")]))])).Render()
        );

    [Fact]
    public void Renders_Empty_Do() =>
        Assert.Equal(
            "do\nend",
            new Do(new Chunk([])).Render()
        );

    [Fact]
    public void Renders_TypeCast()
    {
        var typeCast = new TypeCast(new Identifier("x"), PrimitiveType.Number);
        Assert.Equal("x :: number", typeCast.Render());
    }

    [Fact]
    public void Renders_Return() => Assert.Equal("return 69", new Return(new NumberLiteral(69)).Render());

    [Fact]
    public void Renders_Return_Empty() => Assert.Equal("return", new Return().Render());

    [Fact]
    public void Renders_Continue() => Assert.Equal("continue", new Continue().Render());

    [Fact]
    public void Renders_Break() => Assert.Equal("break", new Break().Render());

    [Fact]
    public void Renders_WhileStatement() =>
        Assert.Equal(
            "while a do\n  x += 1\nend",
            new WhileStatement(
                new Identifier("a"),
                new Chunk([new ExpressionStatement(new BinaryOperator(new Identifier("x"), "+=", new NumberLiteral(1)))])
            ).Render()
        );

    [Fact]
    public void Renders_IfStatement()
    {
        Assert.Equal(
            "if a then\n  x = 1\nelseif b then\n  x = 2\nelseif c then\n  x = 3\nelse\n  x = 69\nend",
            new IfStatement(
                new Identifier("a"),
                new Chunk([setX(1)]),
                [new ElseIfBranch(new Identifier("b"), new Chunk([setX(2)])), new ElseIfBranch(new Identifier("c"), new Chunk([setX(3)]))],
                new Chunk([setX(69)])
            ).Render()
        );

        return;
        static ExpressionStatement setX(int x) => new(new BinaryOperator(new Identifier("x"), "=", new NumberLiteral(x)));
    }

    [Fact]
    public void Renders_IfExpression() =>
        Assert.Equal(
            "if a then 1 elseif b then 2 elseif c then 3 else 69",
            new IfExpression(
                new Identifier("a"),
                new NumberLiteral(1),
                [new ElseIfExpressionBranch(new Identifier("b"), new NumberLiteral(2)), new ElseIfExpressionBranch(new Identifier("c"), new NumberLiteral(3))],
                new NumberLiteral(69)
            ).Render()
        );

    [Fact]
    public void Renders_TypeAlias_GenericWithDefault()
    {
        var typeParameters = new TypeParameters([new TypeParameter("T", PrimitiveType.Number)]);
        Assert.Equal("type Id<T = number> = T", new TypeAlias("Id", typeParameters, new TypeName("T")).Render());
    }

    [Fact]
    public void Renders_TypeAlias_Generic()
    {
        var typeParameters = new TypeParameters([new TypeParameter("T")]);
        Assert.Equal("type Id<T> = T", new TypeAlias("Id", typeParameters, new TypeName("T")).Render());
    }

    [Fact]
    public void Renders_TypeAlias() => Assert.Equal("type A = boolean", new TypeAlias("A", new TypeParameters(), PrimitiveType.Boolean).Render());

    [Fact]
    public void Renders_TypeAlias_Exported() =>
        Assert.Equal(
            "export type A = boolean",
            new TypeAlias("A", new TypeParameters(), PrimitiveType.Boolean) { IsExported = true }.Render()
        );

    [Fact]
    public void Renders_TypeAlias_ExportedGeneric()
    {
        var typeParameters = new TypeParameters([new TypeParameter("T")]);
        Assert.Equal("export type Id<T> = T", new TypeAlias("Id", typeParameters, new TypeName("T")) { IsExported = true }.Render());
    }

    [Fact]
    public void Renders_ForStatement()
    {
        var statement = new ForStatement(
            ["k", "v"],
            new Call(new Identifier("pairs"), [new Identifier("t")]),
            new Chunk([new ExpressionStatement(new Call(new Identifier("print"), [new Identifier("k"), new Identifier("v")]))])
        );

        Assert.Equal(
            "for k, v in pairs(t) do\n  print(k, v)\nend",
            statement.Render()
        );
    }

    [Fact]
    public void Renders_NumericForStatement_WithStep()
    {
        var statement = new NumericForStatement(
            "i",
            new NumberLiteral(1),
            new NumberLiteral(10),
            new NumberLiteral(2),
            new Chunk([new ExpressionStatement(new Call(new Identifier("print"), [new Identifier("i")]))])
        );

        Assert.Equal(
            "for i = 1, 10, 2 do\n  print(i)\nend",
            statement.Render()
        );
    }

    [Fact]
    public void Renders_NumericForStatement_WithoutStep()
    {
        var statement = new NumericForStatement(
            "i",
            new NumberLiteral(1),
            new NumberLiteral(5),
            null,
            new Chunk([new ExpressionStatement(new Call(new Identifier("print"), [new Identifier("i")]))])
        );

        Assert.Equal(
            "for i = 1, 5 do\n  print(i)\nend",
            statement.Render()
        );
    }

    [Fact]
    public void Renders_AnonymousFunction()
    {
        var parameters = new List<Parameter> { new("a", PrimitiveType.Number), new("b", new TypeName("T")) };
        var typeParameter = new TypeParameter("T", PrimitiveType.Number) { OfFunction = true };
        var fn = new AnonymousFunction(
            new TypeParameters([typeParameter]),
            parameters,
            PrimitiveType.Number,
            new Chunk([new Return(new BinaryOperator(new Identifier("a"), "+", new Identifier("b")))])
        );

        Assert.Equal("function<T>(a: number, b: T): number\n  return a + b\nend", fn.Render());
    }

    [Fact]
    public void Renders_AnonymousFunction_Basic()
    {
        var parameters = new List<Parameter>();
        var fn = new AnonymousFunction(null, parameters, null, new Chunk([new Return(new BinaryOperator(new Identifier("a"), "+", new Identifier("b")))]));
        Assert.Equal("function()\n  return a + b\nend", fn.Render());
    }

    [Fact]
    public void Renders_Function()
    {
        var parameters = new List<Parameter> { new("a", PrimitiveType.Number), new("b", new TypeName("T")) };
        var typeParameter = new TypeParameter("T", PrimitiveType.Number) { OfFunction = true };
        var fn = new Function(
            "add",
            new TypeParameters([typeParameter]),
            parameters,
            PrimitiveType.Number,
            new Chunk([new Return(new BinaryOperator(new Identifier("a"), "+", new Identifier("b")))])
        );

        Assert.Equal("const function add<T>(a: number, b: T): number\n  return a + b\nend", fn.Render());
    }

    [Fact]
    public void Renders_Long_ComputedProperty_Table()
    {
        var table = new Table(
            [
                new ComputedPropertyTableInitializer(new StringLiteral("a"), new NumberLiteral(1)),
                new ComputedPropertyTableInitializer(new StringLiteral("b"), new NumberLiteral(2)),
                new ComputedPropertyTableInitializer(new StringLiteral("c"), new NumberLiteral(3)),
                new ComputedPropertyTableInitializer(new StringLiteral("d"), new NumberLiteral(4)),
                new ComputedPropertyTableInitializer(new StringLiteral("e"), new NumberLiteral(5)),
                new ComputedPropertyTableInitializer(new StringLiteral("f"), new NumberLiteral(6))
            ]
        );

        Assert.Equal("{\n  [\"a\"] = 1,\n  [\"b\"] = 2,\n  [\"c\"] = 3,\n  [\"d\"] = 4,\n  [\"e\"] = 5,\n  [\"f\"] = 6,\n}", table.Render());
    }

    [Fact]
    public void Renders_Short_ComputedProperty_Table()
    {
        var table = new Table(
            [
                new ComputedPropertyTableInitializer(new StringLiteral("foo"), new NumberLiteral(69)),
                new ComputedPropertyTableInitializer(new StringLiteral("bar"), new NumberLiteral(420))
            ]
        );

        Assert.Equal("{ [\"foo\"] = 69, [\"bar\"] = 420 }", table.Render());
    }

    [Fact]
    public void Renders_Long_Property_Table()
    {
        var table = new Table(
            [
                new PropertyTableInitializer("a", new NumberLiteral(1)),
                new PropertyTableInitializer("b", new NumberLiteral(2)),
                new PropertyTableInitializer("c", new NumberLiteral(3)),
                new PropertyTableInitializer("d", new NumberLiteral(4)),
                new PropertyTableInitializer("e", new NumberLiteral(5)),
                new PropertyTableInitializer("f", new NumberLiteral(6))
            ]
        );

        Assert.Equal("{\n  a = 1,\n  b = 2,\n  c = 3,\n  d = 4,\n  e = 5,\n  f = 6,\n}", table.Render());
    }

    [Fact]
    public void Renders_Short_Property_Table()
    {
        var table = new Table([new PropertyTableInitializer("foo", new NumberLiteral(69)), new PropertyTableInitializer("bar", new NumberLiteral(420))]);
        Assert.Equal("{ foo = 69, bar = 420 }", table.Render());
    }

    [Fact]
    public void Renders_Nested_Long_Array_Table()
    {
        var innerTable = new Table(
            new List<int>
            {
                1,
                2,
                3,
                4,
                5,
                6
            }.ConvertAll(i => new TableInitializer(new NumberLiteral(i)))
        );

        var table = new Table(
            [..innerTable.Initializers, new TableInitializer(new Table([.. innerTable.Initializers.Take(3)])), new TableInitializer(innerTable)]
        );

        Assert.Equal("{\n  1,\n  2,\n  3,\n  4,\n  5,\n  6,\n  {1, 2, 3},\n  {\n    1,\n    2,\n    3,\n    4,\n    5,\n    6,\n  },\n}", table.Render());
    }

    [Fact]
    public void Renders_Long_Array_Table()
    {
        var table = new Table(
            new List<int>
            {
                1,
                2,
                3,
                4,
                5,
                6
            }.ConvertAll(i => new TableInitializer(new NumberLiteral(i)))
        );

        Assert.Equal("{\n  1,\n  2,\n  3,\n  4,\n  5,\n  6,\n}", table.Render());
    }

    [Fact]
    public void Renders_Short_Array_Table()
    {
        var table = new Table(new List<int> { 1, 2, 3 }.ConvertAll(i => new TableInitializer(new NumberLiteral(i))));
        Assert.Equal("{1, 2, 3}", table.Render());
    }

    [Fact]
    public void Renders_Empty_Table() => Assert.Equal("{}", Table.Empty.Render());

    [Fact]
    public void Renders_Call_MultipleArguments()
    {
        var call = new Call(
            new Identifier("f"),
            [new NumberLiteral(1), new StringLiteral("a"), new BooleanLiteral(true)]
        );

        Assert.Equal("f(1, \"a\", true)", call.Render());
    }

    [Fact]
    public void Renders_Call_MethodFlag_WithNonPropertyAccess()
    {
        var call = new Call(new Identifier("f"), [], true);

        Assert.Equal("f()", call.Render());
    }

    [Fact]
    public void Renders_Call()
    {
        var emptyCall = new Call(new Identifier("abc"), []);
        var call = new Call(new Identifier("abc"), [new StringLiteral("foo")]);
        Assert.Equal("abc()", emptyCall.Render());
        Assert.Equal("abc(\"foo\")", call.Render());
    }

    [Fact]
    public void Renders_PropertyAccess_MultipleNames_WithParenthesizedTarget()
    {
        var access = new PropertyAccess(
            new BinaryOperator(
                new Identifier("a"),
                "+",
                new Identifier("b")
            ),
            ["foo", "bar"]
        );

        Assert.Equal("(a + b).foo.bar", access.Render());
    }

    [Fact]
    public void Renders_PropertyAccess_SingleName_DefaultDot()
    {
        var access = new PropertyAccess(new Identifier("obj"), ["prop"]);
        Assert.Equal("obj.prop", access.Render());
    }

    [Fact]
    public void Renders_PropertyAccess_MultipleNames()
    {
        var access = new PropertyAccess(
            new Identifier("obj"),
            ["a", "b", "c"]
        );

        Assert.Equal("obj.a.b.c", access.Render());
    }

    [Fact]
    public void Renders_PropertyAccess_TwoNames()
    {
        var access = new PropertyAccess(
            new Identifier("obj"),
            ["foo", "bar"]
        );

        Assert.Equal("obj.foo.bar", access.Render());
    }

    [Fact]
    public void Renders_PropertyAccess_TwoNames_ColonOperator()
    {
        var access = new PropertyAccess(
            new Identifier("obj"),
            ["foo", "bar"]
        ) { Operator = ':' };

        Assert.Equal("obj.foo:bar", access.Render());
    }

    [Fact]
    public void Renders_PropertyAccess_SingleName_ColonOperator()
    {
        var access = new PropertyAccess(new Identifier("obj"), ["method"]) { Operator = ':' };
        Assert.Equal("obj:method", access.Render());
    }

    [Fact]
    public void Renders_PropertyAccess_MultipleNames_ColonOperator()
    {
        var access = new PropertyAccess(
            new Identifier("abc"),
            ["foo", "bar", "baz"]
        ) { Operator = ':' };

        Assert.Equal("abc.foo.bar:baz", access.Render());
    }

    [Fact]
    public void Renders_PropertyAccess_KeywordName_AsBracketAccess()
    {
        var access = new PropertyAccess(new Identifier("obj"), ["end"]);
        Assert.Equal("obj[\"end\"]", access.Render());
    }

    [Fact]
    public void Renders_PropertyAccess_KeywordName_InMiddleOfChain()
    {
        var access = new PropertyAccess(new Identifier("obj"), ["a", "repeat", "c"]);
        Assert.Equal("obj.a[\"repeat\"].c", access.Render());
    }

    [Fact]
    public void Renders_PropertyAccess_NonKeywordName_Unaffected()
    {
        var access = new PropertyAccess(new Identifier("obj"), ["endpoint"]);
        Assert.Equal("obj.endpoint", access.Render());
    }

    [Fact]
    public void Renders_PropertyAccess_ParenthesizesTarget_WhenNeeded()
    {
        var target = new BinaryOperator(
            new Identifier("a"),
            "+",
            new Identifier("b")
        );

        var access = new PropertyAccess(target, ["length"]);
        Assert.Equal("(a + b).length", access.Render());
    }

    [Fact]
    public void Renders_PropertyAccess_Chained_OnPropertyAccess()
    {
        var inner = new PropertyAccess(new Identifier("a"), ["b", "c"]);
        var outer = new PropertyAccess(inner, ["d"]);
        Assert.Equal("a.b.c.d", outer.Render());
    }

    [Fact]
    public void Renders_PropertyAccess_MethodCall()
    {
        var target = new Identifier("abc");
        var access = new PropertyAccess(target, ["foo", "bar"]);
        var call = new Call(access, [], true);
        Assert.Equal("abc.foo:bar()", call.Render());
    }

    [Fact]
    public void Renders_PropertyAccess()
    {
        var target = new Identifier("abc");
        var access = new PropertyAccess(target, ["foo"]);
        var bigAccess = new PropertyAccess(target, ["foo", "bar"]);
        Assert.Equal("abc.foo", access.Render());
        Assert.Equal("abc.foo.bar", bigAccess.Render());
    }

    [Fact]
    public void Renders_ElementAccess()
    {
        var target = new Identifier("abc");
        var access = new ElementAccess(target, new StringLiteral("foo"));
        Assert.Equal("abc[\"foo\"]", access.Render());
    }

    [Fact]
    public void Renders_ConstVariable_Annotated()
    {
        var initializer = new NumberLiteral(1);
        var variable = new ConstVariable("abc", PrimitiveType.Number, initializer);
        Assert.Equal("const abc: number = 1", variable.Render());
    }

    [Fact]
    public void Renders_ConstVariable_Unannotated()
    {
        var initializer = new NumberLiteral(1);
        var variable = new ConstVariable("abc", null, initializer);
        Assert.Equal("const abc = 1", variable.Render());
    }

    [Fact]
    public void Renders_LocalVariable_Annotated()
    {
        var initializer = new NumberLiteral(1);
        var variable = new LocalVariable("abc", PrimitiveType.Number, initializer);
        Assert.Equal("local abc: number = 1", variable.Render());
    }

    [Fact]
    public void Renders_LocalVariable_Annotated_NoInitializer()
    {
        var type = new OptionalType(PrimitiveType.Number);
        var variable = new LocalVariable("abc", type, null);
        Assert.Equal("local abc: number?", variable.Render());
    }

    [Fact]
    public void Renders_LocalVariable_Unannotated()
    {
        var initializer = new NumberLiteral(1);
        var variable = new LocalVariable("abc", null, initializer);
        Assert.Equal("local abc = 1", variable.Render());
    }

    [Fact]
    public void Renders_LocalVariable_Unannotated_NoInitializer()
    {
        var variable = new LocalVariable("abc", null, null);
        Assert.Equal("local abc", variable.Render());
    }

    [Fact]
    public void Renders_ExpressionStatement() => Assert.Equal("1", new ExpressionStatement(new NumberLiteral(1)).Render());

    [Fact]
    public void Renders_OptionalType_RequiresParens() =>
        Assert.Equal("(string | boolean)?", new OptionalType(new UnionType([PrimitiveType.String, PrimitiveType.Boolean])).Render());

    [Fact]
    public void Renders_OptionalType() => Assert.Equal("number?", new OptionalType(PrimitiveType.Number).Render());

    [Fact]
    public void Renders_FunctionType_WithTypeParameters() =>
        Assert.Equal(
            "<T>(number, T) -> T",
            new FunctionType(new TypeParameters([new TypeParameter("T")]), [PrimitiveType.Number, new TypeName("T")], new TypeName("T")).Render()
        );

    [Fact]
    public void Renders_FunctionType() =>
        Assert.Equal(
            "(number, boolean?) -> ()",
            new FunctionType(new TypeParameters(), [PrimitiveType.Number, new OptionalType(PrimitiveType.Boolean)], new UnitType()).Render()
        );

    [Fact]
    public void Renders_UnionType_WithRequiredParens() =>
        Assert.Equal("number | (() -> ())", new UnionType([PrimitiveType.Number, new FunctionType(new TypeParameters(), [], new UnitType())]).Render());

    [Fact]
    public void Renders_UnionType() =>
        Assert.Equal("number | string | boolean", new UnionType([PrimitiveType.Number, PrimitiveType.String, PrimitiveType.Boolean]).Render());

    [Fact]
    public void Renders_IntersectionType() =>
        Assert.Equal("number & string & boolean", new IntersectionType([PrimitiveType.Number, PrimitiveType.String, PrimitiveType.Boolean]).Render());

    [Theory]
    [InlineData(PrimitiveTypeKind.Number)]
    [InlineData(PrimitiveTypeKind.String)]
    [InlineData(PrimitiveTypeKind.Boolean)]
    [InlineData(PrimitiveTypeKind.Never)]
    [InlineData(PrimitiveTypeKind.Unknown)]
    [InlineData(PrimitiveTypeKind.Any)]
    [InlineData(PrimitiveTypeKind.Nil)]
    public void Renders_PrimitiveType(PrimitiveTypeKind kind) => Assert.Equal(kind.ToString().ToLower(), new PrimitiveType(kind).Render());

    [Fact]
    public void Renders_TypeName_Generic() =>
        Assert.Equal(
            "Namespace.Sub.Id<number, boolean>",
            new QualifiedTypeName(["Namespace", "Sub"], new TypeName("Id", [PrimitiveType.Number, PrimitiveType.Boolean])).Render()
        );

    [Fact]
    public void Renders_TypeName() => Assert.Equal("Hello", new TypeName("Hello").Render());

    [Fact]
    public void Renders_QualifiedTypeName() => Assert.Equal("Namespace.Sub.Hello", new QualifiedTypeName(["Namespace", "Sub"], new TypeName("Hello")).Render());

    [Fact]
    public void Renders_StringLiteralType() => Assert.Equal($"{RenderState.StringDelimiter}abc{RenderState.StringDelimiter}", new StringLiteralType("abc").Render());

    [Fact]
    public void Renders_Empty_TableType() =>
        Assert.Equal(
            "{}",
            new TableType(null, []).Render()
        );

    [Fact]
    public void Renders_TableType_WithProperties_NoIndexer() =>
        Assert.Equal(
            "{\n  read a: number,\n  b: string,\n}",
            new TableType(
                null,
                [new TableTypeProperty(LuauVisibility.Read, "a", PrimitiveType.Number), new TableTypeProperty(null, "b", PrimitiveType.String)]
            ).Render()
        );

    [Fact]
    public void Renders_TableType_WithProperties() =>
        Assert.Equal(
            "{\n  read [string]: number,\n  read a: number,\n  b: number,\n}",
            new TableType(
                new TableTypeIndexer(LuauVisibility.Read, PrimitiveType.String, PrimitiveType.Number),
                [new TableTypeProperty(LuauVisibility.Read, "a", PrimitiveType.Number), new TableTypeProperty(null, "b", PrimitiveType.Number)]
            ).Render()
        );

    [Fact]
    public void Renders_Dictionary_TableType() =>
        Assert.Equal("{ [string]: number }", new TableType(new TableTypeIndexer(null, PrimitiveType.String, PrimitiveType.Number), []).Render());

    [Fact]
    public void Renders_Array_TableType() => Assert.Equal("{ number }", TableType.Array(PrimitiveType.Number).Render());

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void Renders_BooleanLiteralType(bool value, string expected) => Assert.Equal(expected, new BooleanLiteralType(value).Render());

    [Fact]
    public void Renders_ParenthesizedType() => Assert.Equal("(number)", new ParenthesizedType(new PrimitiveType(PrimitiveTypeKind.Number)).Render());

    [Fact]
    public void Renders_UnitType() => Assert.Equal("()", new UnitType().Render());

    [Fact]
    public void Renders_Parenthesized() => Assert.Equal("(69)", new Parenthesized(new NumberLiteral(69)).Render());

    [Fact]
    public void Renders_Identifier() => Assert.Equal("abc", new Identifier("abc").Render());

    [Theory]
    [InlineData("+")]
    [InlineData("+=")]
    [InlineData("-")]
    [InlineData("-=")]
    [InlineData("=")]
    [InlineData("==")]
    [InlineData("~=")]
    [InlineData("and")]
    [InlineData("or")]
    public void Renders_BinaryOperator(string op)
    {
        var expression = new BinaryOperator(
            new NumberLiteral(1),
            op,
            new NumberLiteral(2)
        );

        Assert.Equal($"1 {op} 2", expression.Render());
    }

    [Fact]
    public void Renders_UnaryOperator()
    {
        var expression = new UnaryOperator("-", new NumberLiteral(1));
        Assert.Equal("-1", expression.Render());
    }

    [Fact]
    public void Renders_MultilineString_NoSpecialSequences()
    {
        var literal = new StringLiteral("abc\ndef");
        Assert.Equal("[[abc\ndef]]", literal.Render());
    }

    [Fact]
    public void Renders_MultilineString_ContainsClosingBrackets()
    {
        var literal = new StringLiteral("line1\nline2]]still here");
        Assert.Equal("[=[line1\nline2]]still here]=]", literal.Render());
    }

    [Fact]
    public void Renders_MultilineString_EndsWithBracket()
    {
        var literal = new StringLiteral("data\n]");
        Assert.Equal("[=[data\n]]=]", literal.Render());
    }

    [Fact]
    public void Renders_MultilineString_ContainsEqualsBracketAndEndsWithBracket()
    {
        var literal = new StringLiteral("a\n]=]b]");
        Assert.Equal("[==[a\n]=]b]]==]", literal.Render());
    }

    [Fact]
    public void Renders_MultilineString_OnlyClosingBracket()
    {
        var literal = new StringLiteral("]\n");
        Assert.Equal("[[]\n]]", literal.Render());
    }

    [Fact]
    public void Renders_MultilineString_ComplexNestedBrackets()
    {
        var literal = new StringLiteral("start\n]==]middle]==]]\nend");
        Assert.Equal("[=[start\n]==]middle]==]]\nend]=]", literal.Render());
    }

    [Fact]
    public void Renders_StringLiteral() => Assert.Equal($"{RenderState.StringDelimiter}abc{RenderState.StringDelimiter}", new StringLiteral("abc").Render());

    [Fact]
    public void Renders_StringLiteral_EscapesBackslash() => Assert.Equal("\"a\\\\b\"", new StringLiteral("a\\b").Render());

    [Fact]
    public void Renders_StringLiteral_EscapesDelimiter() => Assert.Equal("\"say \\\"hi\\\"\"", new StringLiteral("say \"hi\"").Render());

    [Fact]
    public void Renders_MultilineString_LeadingNewlineIsPreserved()
    {
        var literal = new StringLiteral("\nabc");
        Assert.Equal("[[\n\nabc]]", literal.Render());
    }

    [Fact]
    public void Renders_InterpolatedString()
    {
        var interpolated = new InterpolatedString(
            [new InterpolatedStringTextSegment("Welcome, "), new InterpolatedStringExpressionSegment(new Identifier("name")), new InterpolatedStringTextSegment("!")]
        );

        Assert.Equal("`Welcome, {name}!`", interpolated.Render());
    }

    [Fact]
    public void Renders_InterpolatedString_Empty() => Assert.Equal("``", new InterpolatedString([]).Render());

    [Fact]
    public void Renders_InterpolatedString_EscapesBacktick() =>
        Assert.Equal(@"`say \`hi\``", new InterpolatedString([new InterpolatedStringTextSegment("say `hi`")]).Render());

    [Fact]
    public void Renders_InterpolatedString_ParenthesizesAmbiguousHoleExpression()
    {
        var interpolated = new InterpolatedString([new InterpolatedStringExpressionSegment(new BinaryOperator(new NumberLiteral(1), "+", new NumberLiteral(2)))]);

        Assert.Equal("`{(1 + 2)}`", interpolated.Render());
    }

    [Theory]
    [InlineData(12, "12")]
    [InlineData(69.420, "69.42")]
    [InlineData(1e3, "1000")]
    [InlineData(0xFF, "255")]
    [InlineData(1e-10, "1e-10")]
    [InlineData(1.23e20, "1.23e20")]
    [InlineData(-1.5, "-1.5")]
    public void Renders_NumberLiteral(double value, string expected) => Assert.Equal(expected, new NumberLiteral(value).Render());

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void Renders_BooleanLiteral(bool value, string expected) => Assert.Equal(expected, new BooleanLiteral(value).Render());

    [Fact]
    public void Renders_NilLiteral() => Assert.Equal("nil", new NilLiteral().Render());

    [Fact]
    public void Renders_Spread_AsTableUnpack() => Assert.Equal("table.unpack(xs)", new Spread(new Identifier("xs")).Render());

    [Fact]
    public void Renders_Spread_InFinalArgumentPosition() =>
        Assert.Equal(
            "f(1, table.unpack(xs))",
            new Call(new Identifier("f"), [new NumberLiteral(1), new Spread(new Identifier("xs"))]).Render()
        );

    [Fact]
    public void Renders_Spread_OfANonName() =>
        Assert.Equal(
            "table.unpack(table.clone(xs))",
            new Spread(LuauFactory.TableCall("clone", [new Identifier("xs")])).Render()
        );

    [Fact]
    public void Renders_VariadicTypePack() => Assert.Equal("...number", new VariadicTypePack(PrimitiveType.Number).Render());

    [Fact]
    public void Renders_VariadicTypePack_AsATrailingTypeArgument() =>
        Assert.Equal(
            "Event<string, ...number>",
            new TypeName("Event", [PrimitiveType.String, new VariadicTypePack(PrimitiveType.Number)]).Render()
        );
}