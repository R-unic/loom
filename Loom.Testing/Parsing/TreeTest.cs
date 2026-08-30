using Loom.Core.Parsing.AST;
using Loom.Testing;

namespace Loom.Testing.Parsing;

[Collection("Assembly")]
public class TreeTest
{
    [Fact]
    public void FindAt_ReturnsNodeContainingPosition()
    {
        var tree = Utility.GetAST("let foo = 42");
        var node = tree.FindAt(4);
        Assert.NotNull(node);
    }

    [Fact]
    public void FindAt_ReturnsDeepestContainingNode()
    {
        var tree = Utility.GetAST("let foo = 42");
        var node = tree.FindAt(10);
        Assert.NotNull(node);
        Assert.DoesNotContain(node.Children, child => child.Span.Contains(10));
    }

    [Fact]
    public void FindAt_ReturnsNullOutsideTree()
    {
        var tree = Utility.GetAST("let foo = 42");
        Assert.Null(tree.FindAt(-1));
        Assert.Null(tree.FindAt(tree.Span.End + 1));
    }

    [Fact]
    public void FindAt_DoesNotReturnTree()
    {
        var tree = Utility.GetAST("let foo = 42");
        var node = tree.FindAt(0);
        Assert.NotNull(node);
        Assert.IsNotType<Tree>(node);
        Assert.Equal(tree.ToString(), node.ToString());
    }

    [Fact]
    public void FindAt_Generic_ReturnsRequestedNodeType()
    {
        var tree = Utility.GetAST("let foo = 42");
        var node = tree.FindAt<VariableDeclaration>(4);
        Assert.NotNull(node);
    }

    [Fact]
    public void FindAt_Generic_ReturnsNullWhenTypeIsNotFound()
    {
        var tree = Utility.GetAST("let foo = 42");
        var node = tree.FindAt<FunctionDeclaration>(4);
        Assert.Null(node);
    }

    [Fact]
    public void FindAt_Generic_ReturnsDeepestMatchingNode()
    {
        var tree = Utility.GetAST("let foo = 42");
        var node = tree.FindAt<VariableDeclaration>(10);
        Assert.NotNull(node);
        Assert.True(node.Span.Contains(10));
    }
}