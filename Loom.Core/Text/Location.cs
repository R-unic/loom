namespace Loom.Core.Text;

public struct Location(SourceFile file, int position) : IEquatable<Location>
{
    public static Location Empty(SourceFile file) => new(file, 0);

    public static Location operator +(Location location, int n) => new(location.File, location.Position + n);

    public SourceFile File { get; } = file;
    public int Position { get; } = position;
    public int Character => _character ??= File.GetCharacterFromPosition(Position);
    public int Line => _line ??= File.GetLineFromPosition(Position);

    /// <summary>
    ///     <see cref="Character" /> as a reader's editor counts it. Characters stay 0-based inside the
    ///     compiler because that is what the LSP wants, but lines are 1-based, so anything a user reads has
    ///     to convert - a header pairing a 1-based line with a 0-based column points one character short of
    ///     what the underline marks.
    /// </summary>
    public int Column => Character + 1;

    private int? _character;
    private int? _line;

    public static bool operator ==(Location left, Location right) => left.Equals(right);
    public static bool operator !=(Location left, Location right) => !(left == right);

    public bool Equals(Location other) => File.Equals(other.File) && Position == other.Position;
    public override bool Equals(object? obj) => obj is Location other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(File, Position);
    public override string ToString() => $"{File.Name}:{Line}:{Column}";
}