namespace Loom.Core.Text;

public static class OperatorTrie
{
    private const int AlphabetSize = 128;

    private static readonly Node _root = new();

    static OperatorTrie()
    {
        foreach (var (pattern, kind) in SyntaxFacts.OperatorMap)
        {
            var node = _root;
            foreach (var character in pattern)
            {
                node.Children ??= new Node?[AlphabetSize];
                node = node.Children[character] ??= new Node();
            }

            node.Kind = kind;
        }
    }

    public static bool TryMatch(string text, int initialPosition, out SyntaxKind kind, out int length)
    {
        var node = _root;
        SyntaxKind? bestKind = null;
        var bestLength = 0;
        var position = initialPosition;

        while (position < text.Length)
        {
            var character = text[position];
            if (character >= AlphabetSize || node.Children is not { } children || children[character] is not { } next)
                break;

            node = next;
            position++;

            if (node.Kind is not { } k) continue;
            bestKind = k;
            bestLength = position - initialPosition;
        }

        if (bestKind is { } result)
        {
            kind = result;
            length = bestLength;
            return true;
        }

        kind = default;
        length = 0;
        return false;
    }

    private sealed class Node
    {
        public Node?[]? Children;
        public SyntaxKind? Kind;
    }
}