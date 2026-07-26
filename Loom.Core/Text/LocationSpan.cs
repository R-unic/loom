namespace Loom.Core.Text;

public readonly struct LocationSpan
    : IEquatable<LocationSpan>
{
    public LocationSpan(Location start, Location end)
    {
        Start = start;
        End = end;
    }

    public LocationSpan(Location start, int length)
    {
        Start = start;
        End = start + length;
    }

    public SourceFile File => Start.File;
    public int Length => End.Position - Start.Position;
    public Location Start { get; }
    public Location End { get; }

    public static LocationSpan Empty(SourceFile? file = null) => new(Location.Empty(file ?? SourceFile.Empty), Location.Empty(file ?? SourceFile.Empty));
    public static bool operator ==(LocationSpan left, LocationSpan right) => left.Equals(right);
    public static bool operator !=(LocationSpan left, LocationSpan right) => !(left == right);

    public ReadOnlySpan<char> GetText() => File.SourceText.AsSpan(Start.Position, Length);

    public bool Equals(LocationSpan other) =>
        File.Equals(other.File)
        && Start.Equals(other.Start)
        && End.Equals(other.End)
        && Length == other.Length;

    public override bool Equals(object? obj) => obj is LocationSpan other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(File, Start, End, Length);
    public override string ToString() => $"{File.Name} @ {Start.Line}:{Start.Character} - {End.Line}:{End.Character}";
}