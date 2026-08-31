using Loom.LanguageServer;
using Loom.Core.Parsing.AST;

namespace Loom.Testing.Parsing;

[Collection("Assembly")]
public class NodeFinderTest
{
    [Fact]
    public void FindAt_ReturnsInnermostNodeContainingPosition()
    {
        var tree = Utility.GetAST("let x = 1 + 2;");
        var binary = Assert.IsType<BinaryOperator>(Assert.IsType<VariableDeclaration>(tree.Statements[0]).EqualsValueClause!.Value);

        var found = NodeFinder.FindAt(tree, binary.Left.Span.Position);

        Assert.Same(binary.Left, found);
    }

    [Fact]
    public void FindAt_ReturnsRootStatement_WhenPositionHasNoNarrowerNode()
    {
        var tree = Utility.GetAST("let doubled = 1;");

        var found = NodeFinder.FindAt(tree, 4);

        Assert.IsType<VariableDeclaration>(found);
    }

    [Fact]
    public void FindAt_ReturnsNull_WhenPositionIsOutOfRange()
    {
        var tree = Utility.GetAST("let x = 1;");

        Assert.Null(NodeFinder.FindAt(tree, 1000));
    }
}
