namespace Loom.Core.Text;

public sealed record Token
{
    public Token(SyntaxKind kind, SourceFile file, TextSpan span, string? text = null)
    {
        Kind = kind;
        File = file;
        Span = span;
        Text = text;
    }

    // Convenience overload for synthetic tokens described by a LocationSpan (parser/tests).
    public Token(SyntaxKind kind, LocationSpan location, string? text = null)
        : this(kind, location.File, new TextSpan(location.Start.Position, location.Length), text)
    {
    }

    public SyntaxKind Kind { get; }
    public SourceFile File { get; }
    public TextSpan Span { get; }

    public string Text => field ??= File.SourceText.AsSpan(Span.Position, Span.Length).ToString();

    /// <summary>Resolves the token's location with line/character info. Only needed for diagnostics.</summary>
    public LocationSpan GetLocation() => new(new Location(File, Span.Position), Span.Length);
}