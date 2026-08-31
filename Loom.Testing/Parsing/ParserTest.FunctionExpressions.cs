using Loom.Core.Parsing.AST;
using PrimitiveTypeKind = Loom.Core.TypeChecking.Types.PrimitiveTypeKind;

namespace Loom.Testing.Parsing;

public partial class ParserTest
{
    [Fact]
    public void Parses_FunctionExpression_BlockBody()
    {
        var tree = Utility.GetAST("let f = fn(a: number, b: number): number { return a + b; };");
        var declaration = Assert.IsType<VariableDeclaration>(Assert.Single(tree.Statements));
        var functionExpression = Assert.IsType<FunctionExpression>(declaration.EqualsValueClause!.Value);

        Assert.Null(functionExpression.TypeParameters);
        Assert.Equal(2, functionExpression.Parameters!.ParameterList.Count);
        Assert.Equal(PrimitiveTypeKind.Number, Assert.IsType<PrimitiveType>(functionExpression.ReturnType!.Type).Kind);
        Assert.IsType<Block>(functionExpression.Body);
    }

    [Fact]
    public void Parses_FunctionExpression_ArrowBody()
    {
        var tree = Utility.GetAST("let f = fn(x: number) -> x + 1;");
        var declaration = Assert.IsType<VariableDeclaration>(Assert.Single(tree.Statements));
        var functionExpression = Assert.IsType<FunctionExpression>(declaration.EqualsValueClause!.Value);

        Assert.Null(functionExpression.ReturnType);
        var body = Assert.IsType<ExpressionBody>(functionExpression.Body);
        Assert.IsType<BinaryOperator>(body.Expression);
    }

    [Fact]
    public void Parses_FunctionExpression_WithTypeParameters()
    {
        var tree = Utility.GetAST("let f = fn<T>(x: T): T { return x; };");
        var declaration = Assert.IsType<VariableDeclaration>(Assert.Single(tree.Statements));
        var functionExpression = Assert.IsType<FunctionExpression>(declaration.EqualsValueClause!.Value);

        Assert.NotNull(functionExpression.TypeParameters);
        Assert.Equal("T", Assert.Single(functionExpression.TypeParameters.ParameterList).Name.Text);
    }

    [Fact]
    public void Parses_FunctionExpression_NoParameters()
    {
        var tree = Utility.GetAST("let f = fn -> 1;");
        var declaration = Assert.IsType<VariableDeclaration>(Assert.Single(tree.Statements));
        var functionExpression = Assert.IsType<FunctionExpression>(declaration.EqualsValueClause!.Value);
        Assert.Null(functionExpression.Parameters);
    }

    [Fact]
    public void Parses_FunctionExpression_ImmediatelyInvoked()
    {
        var tree = Utility.GetAST("(fn(): number { return 1; })();");
        var expressionStatement = Assert.IsType<ExpressionStatement>(Assert.Single(tree.Statements));
        var invocation = Assert.IsType<Invocation>(expressionStatement.Expression);
        var parenthesized = Assert.IsType<Parenthesized>(invocation.Expression);
        Assert.IsType<FunctionExpression>(parenthesized.Expression);
    }

    [Fact]
    public void Parses_FunctionExpression_ImmediatelyInvoked_InsideExpressionPosition()
    {
        var tree = Utility.GetAST("let x = fn(): number { return 1; }();");
        var declaration = Assert.IsType<VariableDeclaration>(Assert.Single(tree.Statements));
        var invocation = Assert.IsType<Invocation>(declaration.EqualsValueClause!.Value);
        Assert.IsType<FunctionExpression>(invocation.Expression);
    }

    [Fact]
    public void Parses_FunctionExpression_ReturnedFromFunctionDeclaration()
    {
        var tree = Utility.GetAST("fn make_adder(x: number) -> fn(y: number): number { return x + y; };");
        var declaration = Assert.IsType<FunctionDeclaration>(Assert.Single(tree.Statements));
        var body = Assert.IsType<ExpressionBody>(declaration.Body);
        Assert.IsType<FunctionExpression>(body.Expression);
    }
}
