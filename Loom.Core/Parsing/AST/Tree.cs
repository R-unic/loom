using Loom.Core.Lexing;

namespace Loom.Core.Parsing.AST;

public class Tree(LexerResult lexerResult, List<Statement> statements)
    : Node(lexerResult.TokensWithTrivia, statements)
{
    public List<Statement> Statements { get; } = statements;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitTree(this);

    public Node? FindAt(int position) => FindAt<Node>(this, position);
    public T? FindAt<T>(int position) where T : Node => FindAt<T>(this, position);

    private static T? FindAt<T>(Node node, int position) where T : Node
    {
        if (!node.Span.Contains(position))
            return null;

        foreach (var child in node.Children)
        {
            if (!child.Span.Contains(position))
                continue;

            var result = FindAt<T>(child, position);
            if (result is not null)
                return result;
        }

        return node as T;
    }
}