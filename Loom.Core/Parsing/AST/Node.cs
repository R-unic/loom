using System.Diagnostics.CodeAnalysis;
using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public abstract class Node
{
    private static int _nextId;

    protected Node(IEnumerable<Token?> theseTokens, IEnumerable<Node?> children)
    {
        Id = new NodeId(Interlocked.Increment(ref _nextId));

        Children = children.OfType<Node>().OrderBy(n => n.Span.Position).ToArray();
        Tokens = theseTokens.OfType<Token>().OrderBy(t => t.Span.Position).ToArray();
        Span = DeriveSpan();
        foreach (var child in Children)
            child.Parent = this;
    }

    public NodeId Id { get; }
    public IReadOnlyList<Node> Children { get; }
    public IReadOnlyList<Token> Tokens { get; }
    public TextSpan Span { get; }
    public LocationSpan LocationSpan => new(new Location(File, Span.Position), new Location(File, Span.End));
    public SourceFile File => field ??= Tokens.Count == 0 ? SourceFile.Empty : Tokens[0].File;
    [MaybeNull] public Node Parent { get; private set; }

    /// <summary>
    ///     The <c>###</c> doc comment written above this node, or null when it has none. Attributes are part of
    ///     the declaration they annotate, so a doc comment is looked for both above them and between them and
    ///     the keyword - the two places an author would reasonably write one.
    /// </summary>
    public string? Documentation
    {
        get
        {
            var documentation = File.Documentation;
            if (documentation.IsEmpty || Tokens.Count == 0)
                return null;

            if (documentation.At(Span.Position) is { } aboveTheDeclaration)
                return aboveTheDeclaration;

            // an attributed declaration begins at its '[', so a doc comment written under the attributes
            // documents the keyword the list ends before rather than the declaration's own first token
            if ((this as IWithAttributes)?.Attributes is not { } attributes)
                return null;

            var afterAttributes = Tokens.FirstOrDefault(token => token.Span.Position >= attributes.Span.End);
            return afterAttributes == null ? null : documentation.At(afterAttributes.Span.Position);
        }
    }

    public abstract T Accept<T>(Visitor<T> visitor);
    public override string ToString() => LocationSpan.GetText().ToString();
    public IReadOnlyList<T> GetDescendants<T>() where T : Node => GetDescendants().OfType<T>().ToArray();

    public IReadOnlyList<Node> GetDescendants()
    {
        var result = new List<Node>();
        var queue = new Queue<Node>(Children);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            result.Add(node);
            foreach (var child in node.Children)
                queue.Enqueue(child);
        }

        return result;
    }

    public bool IsDescendantOf<T>() where T : Node => FirstAncestorOfType<T>() != null;

    public T? FirstAncestorOfType<T>()
        where T : Node
    {
        for (var node = Parent; node != null; node = node.Parent)
            if (node is T typed)
                return typed;

        return null;
    }

    public Node? FirstAncestorImplementing<T>()
        where T : class
    {
        for (var node = Parent; node != null; node = node.Parent)
            if (node is T)
                return node;

        return null;
    }

    private TextSpan DeriveSpan() =>
        Tokens.Count == 0
            ? TextSpan.Empty
            : TextSpan.FromStartEnd(Tokens[0].Span.Position, Tokens[^1].Span.End);
}