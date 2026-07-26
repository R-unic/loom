namespace Loom.Core.Text;

/// <summary>
///     A lightweight span over source text: just a start position and a length.
///     Carries no file reference and computes no line/character info, so it is cheap
///     to produce for every token. Convert to <see cref="LocationSpan" /> (via
///     <see cref="Token.GetLocation" />) only when a diagnostic needs line/character detail.
/// </summary>
public readonly struct TextSpan(int position, int length) : IEquatable<TextSpan>
{
    public static TextSpan Empty => new(0, 0);

    public int Position { get; } = position;
    public int Length { get; } = length;
    public int End => Position + Length;

    public static TextSpan FromStartEnd(int startPosition, int endPosition) => new(startPosition, endPosition - startPosition);
    public static bool operator ==(TextSpan left, TextSpan right) => left.Equals(right);
    public static bool operator !=(TextSpan left, TextSpan right) => !(left == right);

    public bool Equals(TextSpan other) => Position == other.Position && Length == other.Length;
    public override bool Equals(object? obj) => obj is TextSpan other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Position, Length);
    public override string ToString() => $"[{Position}..{End}]";
}