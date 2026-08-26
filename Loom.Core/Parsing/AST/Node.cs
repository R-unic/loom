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
        var ordered = items is IReadOnlyList<T?> list ? WithoutNulls(list) : [.. items.OfType<T>()];
        for (var i = 1; i < ordered.Length; i++)
            if (positionOf(ordered[i - 1]) > positionOf(ordered[i]))
                return [.. ordered.OrderBy(positionOf)];

        return ordered;
    }

    private static T[] WithoutNulls<T>(IReadOnlyList<T?> items)
        where T : class
    {
        var count = items.OfType<T>().Count();
        if (count == 0)
            return [];

        var result = new T[count];
        var next = 0;
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var i = 0; i < items.Count; i++)
            if (items[i] is { } item)
                result[next++] = item;

        return result;
    }

    public NodeId Id { get; }
    public IReadOnlyList<Node> Children { get; }

    /// <summary>
    ///     The tokens this node owns directly — its keywords, punctuation and name. A child's tokens belong
    ///     to the child; use <see cref="AllTokens" /> for everything under this node.
    /// </summary>
    public IReadOnlyList<Token> Tokens { get; }
    public TextSpan Span { get; }
    public LocationSpan LocationSpan => new(new Location(File, Span.Position), new Location(File, Span.End));
    /// <remarks>A node with no tokens of its own - a wrapper around a single child - takes the file from below it.</remarks>
    public SourceFile File
    {
        get
        {
            if (field != null)
                return field;

            if (Tokens.Count > 0)
                return field = Tokens[0].File;

            foreach (var child in Children)
                if (!ReferenceEquals(child.File, SourceFile.Empty))
                    return field = child.File;

            return field = SourceFile.Empty;
        }
    }

    /// <summary>
    ///     Every token under this node, its own included, in source order. Walks the subtree on each call
    ///     rather than storing it - the few callers that need this are asking a question about a small node,
    ///     and keeping the answer on every node is what made the tree quadratic to build.
    /// </summary>
    public IReadOnlyList<Token> AllTokens()
    {
        var tokens = new List<Token>();
        CollectTokens(tokens);
        tokens.Sort(static (left, right) => left.Span.Position.CompareTo(right.Span.Position));

        return tokens;
    }

    /// <summary>The last token this node covers, or null when it covers none.</summary>
    public Token? LastToken()
    {
        Token? last = null;
        var pending = new Stack<Node>();
        pending.Push(this);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            foreach (var token in node.Tokens)
                if (last == null || token.Span.End > last.Span.End)
                    last = token;

            foreach (var child in node.Children)
                pending.Push(child);
        }

        return last;
    }

    private void CollectTokens(List<Token> into)
    {
        into.AddRange(Tokens);
        foreach (var child in Children)
            child.CollectTokens(into);
    }
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

    /// <summary>
    ///     This node and everything below it that is a <typeparamref name="T" />, for a walk whose root may
    ///     itself be one - a type pattern that is nothing but a <c>let</c> binder, say.
    /// </summary>
    public IEnumerable<T> EnumerateSelfAndDescendants<T>() where T : Node =>
        this is T self ? EnumerateDescendants<T>().Prepend(self) : EnumerateDescendants<T>();

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

    /// <summary>
    ///     The text this node covers: its own tokens and everything below it.
    /// </summary>
    /// <remarks>
    ///     Read off the children's spans rather than off a token list containing theirs. A node used to be
    ///     handed every token beneath it, which made the span a matter of reading the ends of one list - and
    ///     made a token at depth <c>d</c> stored <c>d</c> times over. Children are already built by the time
    ///     this runs, so their spans answer the same question without the copying. A child with no tokens at
    ///     all has no position to contribute and is skipped; taking its empty span literally would drag the
    ///     start back to 0.
    /// </remarks>
    private TextSpan DeriveSpan()
    {
        var start = int.MaxValue;
        var end = int.MinValue;

        foreach (var token in Tokens)
        {
            start = Math.Min(start, token.Span.Position);
            end = Math.Max(end, token.Span.End);
        }

        foreach (var child in Children)
        {
            if (child.Tokens.Count == 0 && child.Children.Count == 0)
                continue;

            start = Math.Min(start, child.Span.Position);
            end = Math.Max(end, child.Span.End);
        }

        return end < start ? TextSpan.Empty : TextSpan.FromStartEnd(start, end);
    }
}