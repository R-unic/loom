using Loom.Core.Parsing.AST;
using Loom.Core.Text;

namespace Loom.Testing.Parsing;

public partial class ParserTest
{
    [Fact]
    public void Parses_RestParameter()
    {
        var result = Utility.AssertNoErrors(Utility.Parse("fn foo(..data: unknown[]) { }"));
        var functionDeclaration = Assert.IsType<FunctionDeclaration>(result.Tree.Statements.Single());
        var parameter = Assert.Single(functionDeclaration.Parameters!.ParameterList);
        Assert.NotNull(parameter.DotDot);
        Assert.Equal(SyntaxKind.DotDot, parameter.DotDot!.Kind);
        Assert.Equal("data", parameter.Name.Text);
    }

    [Fact]
    public void Parses_RestParameter_AfterRequiredParameters()
    {
        var result = Utility.AssertNoErrors(Utility.Parse("fn foo(a: number, ..rest: number[]) { }"));
        var functionDeclaration = Assert.IsType<FunctionDeclaration>(result.Tree.Statements.Single());
        Assert.Equal(2, functionDeclaration.Parameters!.ParameterList.Count);
        Assert.Null(functionDeclaration.Parameters.ParameterList[0].DotDot);
        Assert.NotNull(functionDeclaration.Parameters.ParameterList[1].DotDot);
    }

    [Fact]
    public void Parses_NonRestParameter_HasNullDotDot()
    {
        var result = Utility.AssertNoErrors(Utility.Parse("fn foo(a: number) { }"));
        var functionDeclaration = Assert.IsType<FunctionDeclaration>(result.Tree.Statements.Single());
        Assert.Null(functionDeclaration.Parameters!.ParameterList.Single().DotDot);
    }
}
