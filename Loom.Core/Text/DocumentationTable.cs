namespace Loom.Core.Text;

/// <summary>
///     A file's <c>###</c> doc comments, keyed by the position of the token each block documents. The lexer
///     builds one per file: doc comments are trivia, so the parser never sees them, yet everything downstream
///     that talks about a declaration - hover, completion, signature help - wants the prose written above it.
/// </summary>
/// <remarks>
///     Blocks are keyed by what follows them rather than by their own position so a lookup needs only the
///     documented node, and a run of consecutive <c>###</c> lines is one block: the run reads as one paragraph
///     in the source, and splitting it would surface only its last line.
/// </remarks>
public sealed class DocumentationTable(Dictionary<int, string> blocksByDocumentedPosition)
{
    public static readonly DocumentationTable Empty = new([]);

    /// <summary>The doc comment written directly above the token at <paramref name="position" />, or null when there is none.</summary>
    public string? At(int position) => blocksByDocumentedPosition.GetValueOrDefault(position);

    public bool IsEmpty => blocksByDocumentedPosition.Count == 0;

    /// <summary>
    ///     Strips the <c>###</c> marker and one following space from a doc comment line. The single space is
    ///     the separator between marker and prose, not indentation, so keeping it would push every doc block
    ///     one column in - and four spaces of it into a markdown code block.
    /// </summary>
    public static string StripMarker(string line)
    {
        var text = line.TrimStart();
        if (!text.StartsWith(DocMarker, StringComparison.Ordinal))
            return text;

        text = text[DocMarker.Length..];
        return text.StartsWith(' ') ? text[1..] : text;
    }

    public const string DocMarker = "###";
}
