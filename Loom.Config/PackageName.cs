using System.Diagnostics.CodeAnalysis;

namespace Loom.Config;

/// <summary>
///     Identity of a Loom package: an optional scope and a name, written <c>scope/name</c> or <c>name</c>.
///     Comparison is case-insensitive, since a package name doubles as a path segment on disk.
/// </summary>
public sealed class PackageName : IEquatable<PackageName>, IComparable<PackageName>
{
    public const int MaximumSegmentLength = 64;

    private PackageName(string? scope, string name)
    {
        Scope = scope;
        Name = name;
    }

    /// <summary>The owner segment before the <c>/</c>, or <see langword="null" /> for an unscoped package.</summary>
    public string? Scope { get; }

    /// <summary>The segment after the <c>/</c>, or the whole name for an unscoped package.</summary>
    public string Name { get; }

    public bool IsScoped => Scope != null;

    public static PackageName Parse(string? text) =>
        TryParse(text, out var packageName, out var error) ? packageName : throw new FormatException(error);

    public static bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out PackageName? packageName) =>
        TryParse(text, out packageName, out _);

    public static bool TryParse(
        [NotNullWhen(true)] string? text,
        [NotNullWhen(true)] out PackageName? packageName,
        [NotNullWhen(false)] out string? error
    )
    {
        packageName = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "package name cannot be empty.";
            return false;
        }

        var separator = text.IndexOf('/');
        if (separator >= 0 && text.IndexOf('/', separator + 1) >= 0)
        {
            error = $"package name '{text}' may contain at most one '/', separating the scope from the name.";
            return false;
        }

        var scope = separator < 0 ? null : text[..separator];
        var name = separator < 0 ? text : text[(separator + 1)..];
        if (scope != null && !ValidateSegment(scope, "scope", out error))
            return false;

        if (!ValidateSegment(name, "name", out error))
            return false;

        packageName = new PackageName(scope, name);
        error = null;
        return true;
    }

    /// <summary>
    ///     Orders names the way they are compared, so a file listing packages — a lock file — can be written in one
    ///     order whoever writes it. An unscoped name sorts before every scoped one.
    /// </summary>
    public int CompareTo(PackageName? other)
    {
        if (other == null)
            return 1;

        var scope = string.Compare(Scope, other.Scope, StringComparison.OrdinalIgnoreCase);
        return scope != 0 ? scope : string.Compare(Name, other.Name, StringComparison.OrdinalIgnoreCase);
    }

    public bool Equals(PackageName? other) =>
        other != null
        && string.Equals(Scope, other.Scope, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => obj is PackageName other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(
            Scope == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Scope),
            StringComparer.OrdinalIgnoreCase.GetHashCode(Name)
        );

    public static bool operator ==(PackageName? left, PackageName? right) => left?.Equals(right) ?? right is null;
    public static bool operator !=(PackageName? left, PackageName? right) => !(left == right);

    public override string ToString() => Scope == null ? Name : $"{Scope}/{Name}";

    private static bool ValidateSegment(string segment, string kind, [NotNullWhen(false)] out string? error)
    {
        if (segment.Length == 0)
        {
            error = $"package {kind} cannot be empty.";
            return false;
        }

        if (segment.Length > MaximumSegmentLength)
        {
            error = $"package {kind} '{segment}' may be at most {MaximumSegmentLength} characters.";
            return false;
        }

        if (!char.IsAsciiLetter(segment[0]))
        {
            error = $"package {kind} '{segment}' must start with a letter.";
            return false;
        }

        foreach (var character in segment)
        {
            if (char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
                continue;

            error = $"package {kind} '{segment}' may only contain letters, digits, '-' and '_'.";
            return false;
        }

        error = null;
        return true;
    }
}
