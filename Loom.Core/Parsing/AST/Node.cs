using System.Diagnostics.CodeAnalysis;
using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public abstract class Node
{
    private static int _nextId;

    protected Node(IEnumerable<Token?> theseTokens, IEnumerable<Node?> children)
    {
        Id = new NodeId(Interlocked.Increment(ref _nextId));

        Children = InSourceOrder(children, static child => child.Span.Position);
        Tokens = InSourceOrder(theseTokens, static token => token.Span.Position);
        Span = DeriveSpan();
        foreach (var child in Children)
            child.Parent = this;
    }

    /// <summary>
    ///     <paramref name="items" />, nulls dropped, ordered by where each one starts.
    /// </summary>
    /// <remarks>
    ///     A parser builds a node out of tokens it just read, so they arrive in source order already and the
    ///     sort is a formality - but it was being paid for on every node, and <c>OrderBy</c> buffers the
    ///     sequence and allocates a key array and an index map before it will admit a one-element list is
    ///     sorted. Checking is linear and answers yes almost every time. When it answers no the work goes
    ///     back to <c>OrderBy</c> rather than <c>Array.Sort</c>, which is unstable: two nodes can start at
    ///     the same position - a missing token synthesized by error recovery has no width - and the order
    ///     they were given in is what decides which one <see cref="DeriveSpan" /> reads.
    /// </remarks>
    private static T[] InSourceOrder<T>(IEnumerable<T?> items, Func<T, int> positionOf)
        where T : class
    {
        var ordered = items is IReadOnlyList<T?> list ? WithoutNulls(list) : items.OfType<T>().ToArray();
        for (var i = 1; i < ordered.Length; i++)
            if (positionOf(ordered[i - 1]) > positionOf(ordered[i]))
                return ordered.OrderBy(positionOf).ToArray();

        return ordered;
    }

    private static T[] WithoutNulls<T>(IReadOnlyList<T?> items)
        where T : class
    {
        var count = 0;
        for (var i = 0; i < items.Count; i++)
            if (items[i] != null)
                count++;

        if (count == 0)
            return [];

        var result = new T[count];
        var next = 0;
        for (var i = 0; i < items.Count; i++)
            if (items[i] is { } item)
                result[next++] = item;

        return result;
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

            // 'declare' sits above the signature that names the symbol, so a doc comment written above the
            // statement documents a token belonging to the wrapper rather than to the signature itself
            if (Parent is Declare declare && declare.Signature == this && documentation.At(declare.Span.Position) is { } aboveTheDeclareKeyword)
                return aboveTheDeclareKeyword;

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
    public IReadOnlyList<T> GetDescendants<T>() where T : Node => EnumerateDescendants<T>().ToArray();

    /// <inheritdoc cref="EnumerateDescendants" />
    public IEnumerable<T> EnumerateDescendants<T>() where T : Node => EnumerateDescendants().OfType<T>();

    /// <summary>Every node below this one, materialized. Use <see cref="EnumerateDescendants" /> for a walk that only passes through once.</summary>
    public IReadOnlyList<Node> GetDescendants() => EnumerateDescendants().ToArray();

    /// <summary>
    ///     Every node below this one, in breadth-first order, produced as it is asked for. A caller that walks
    ///     the tree once - looking for one kind of node, or stopping at the first match - pays for the walk
    ///     either way, but not for a list of the whole tree it never reads twice.
    /// </summary>
    public IEnumerable<Node> EnumerateDescendants()
    {
        var queue = new Queue<Node>(Children);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            yield return node;
            foreach (var child in node.Children)
                queue.Enqueue(child);
        }
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