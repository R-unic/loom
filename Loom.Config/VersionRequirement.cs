using System.Diagnostics.CodeAnalysis;

namespace Loom.Config;

/// <summary>
///     A version requirement as written in a manifest (<c>^1.2</c>, <c>~1.2.3</c>, <c>&gt;=1.4, &lt;2</c>), read as
///     the set of versions it accepts. Every clause form names an interval and comma-separated clauses intersect, so
///     a requirement is exactly one interval — which is what makes <see cref="Intersect(VersionRequirement)" />
///     closed, and lets a build reduce everything asking for a package to the single version it must land on.
/// </summary>
/// <remarks>
///     Equality is over the versions accepted, not the spelling: <c>^1.2</c> equals <c>&gt;=1.2.0, &lt;2.0.0</c>.
///     <see cref="ToString" /> still answers with the written form, since that is what a manifest author will
///     recognise in a diagnostic.
/// </remarks>
public sealed class VersionRequirement : IEquatable<VersionRequirement>
{
    private readonly string _text;

    private VersionRequirement(string text, Bound? lower, Bound? upper)
    {
        _text = text;
        Lower = lower;
        Upper = upper;
    }

    /// <summary>The requirement accepting every release version, written <c>*</c>.</summary>
    public static VersionRequirement Any { get; } = new("*", null, null);

    /// <summary>The lowest version accepted, or <see langword="null" /> when nothing bounds the requirement below.</summary>
    public Bound? Lower { get; }

    /// <summary>The highest version accepted, or <see langword="null" /> when nothing bounds the requirement above.</summary>
    public Bound? Upper { get; }

    public bool IsAny => Lower == null && Upper == null;

    public static VersionRequirement Parse(string? text) =>
        TryParse(text, out var requirement, out var error) ? requirement : throw new FormatException(error);

    public static bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out VersionRequirement? requirement) =>
        TryParse(text, out requirement, out _);

    /// <summary>
    ///     Reads a comma-separated clause list. An unsatisfiable requirement (<c>&gt;=2, &lt;1</c>) is rejected here
    ///     rather than represented, so a requirement that exists always names at least one version.
    /// </summary>
    public static bool TryParse(
        [NotNullWhen(true)] string? text,
        [NotNullWhen(true)] out VersionRequirement? requirement,
        [NotNullWhen(false)] out string? error
    )
    {
        requirement = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "version requirement cannot be empty.";
            return false;
        }

        Bound? lower = null;
        Bound? upper = null;
        foreach (var clause in text.Split(','))
        {
            if (!TryReadClause(clause, out var clauseLower, out var clauseUpper, out error))
                return false;

            lower = TighterLower(lower, clauseLower);
            upper = TighterUpper(upper, clauseUpper);
        }

        if (IsEmpty(lower, upper))
        {
            error = $"version requirement '{text.Trim()}' cannot be satisfied by any version.";
            return false;
        }

        requirement = new VersionRequirement(text.Trim(), lower, upper);
        error = null;
        return true;
    }

    /// <summary>
    ///     Whether <paramref name="version" /> is accepted. A pre-release only ever satisfies a requirement one of
    ///     whose bounds names a pre-release of the same <c>major.minor.patch</c>: asking for <c>&gt;=1.2.0</c> is
    ///     asking for released versions, and <c>1.3.0-beta.1</c> is not one.
    /// </summary>
    public bool Satisfies(Version version)
    {
        if (Lower is { } lower)
        {
            var comparison = version.CompareTo(lower.Version);
            if (comparison < 0 || (comparison == 0 && !lower.IsInclusive))
                return false;
        }

        if (Upper is { } upper)
        {
            var comparison = version.CompareTo(upper.Version);
            if (comparison > 0 || (comparison == 0 && !upper.IsInclusive))
                return false;
        }

        return !version.IsPrerelease || NamesPrereleaseOf(Lower, version) || NamesPrereleaseOf(Upper, version);
    }

    /// <summary>
    ///     The requirement accepting exactly the versions both accept, or <see langword="null" /> when they agree on
    ///     none — which is the only place emptiness shows up, and the answer a build reports as a conflict.
    /// </summary>
    public VersionRequirement? Intersect(VersionRequirement other)
    {
        var lower = TighterLower(Lower, other.Lower);
        var upper = TighterUpper(Upper, other.Upper);
        if (lower == Lower && upper == Upper)
            return this;

        if (lower == other.Lower && upper == other.Upper)
            return other;

        return IsEmpty(lower, upper) ? null : new VersionRequirement(Describe(lower, upper), lower, upper);
    }

    /// <summary>
    ///     Intersects every requirement asking for one package, or <see langword="null" /> when they cannot all be
    ///     satisfied at once. An empty list constrains nothing, so it comes back as <see cref="Any" />.
    /// </summary>
    public static VersionRequirement? Intersect(IEnumerable<VersionRequirement> requirements)
    {
        using var enumerator = requirements.GetEnumerator();
        if (!enumerator.MoveNext())
            return Any;

        var result = enumerator.Current;
        while (enumerator.MoveNext())
        {
            var narrowed = result.Intersect(enumerator.Current);
            if (narrowed == null)
                return null;

            result = narrowed;
        }

        return result;
    }

    public bool Equals(VersionRequirement? other) => other != null && Lower == other.Lower && Upper == other.Upper;
    public override bool Equals(object? obj) => obj is VersionRequirement other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Lower, Upper);

    public static bool operator ==(VersionRequirement? left, VersionRequirement? right) => left?.Equals(right) ?? right is null;
    public static bool operator !=(VersionRequirement? left, VersionRequirement? right) => !(left == right);

    public override string ToString() => _text;

    /// <summary>The comparator form of the requirement, whatever it was written as.</summary>
    public string ToComparatorString() => Describe(Lower, Upper);

    private static bool TryReadClause(string clause, out Bound? lower, out Bound? upper, [NotNullWhen(false)] out string? error)
    {
        lower = null;
        upper = null;
        var trimmed = clause.Trim();
        if (trimmed.Length == 0)
        {
            error = "a version requirement clause cannot be empty.";
            return false;
        }

        if (trimmed == "*")
        {
            error = null;
            return true;
        }

        var comparator = ReadComparator(trimmed);
        if (!TryReadPartial(trimmed[comparator.Length..].TrimStart(), out var partial, out error))
            return false;

        var version = partial.Completed;
        switch (comparator)
        {
            case ">=":
                lower = new Bound(version, true);
                break;
            case ">":
                lower = new Bound(version, false);
                break;
            case "<=":
                upper = new Bound(version, true);
                break;
            case "<":
                upper = new Bound(version, false);
                break;
            case "~":
                lower = new Bound(version, true);
                upper = new Bound(partial.WrittenCeiling, false);
                break;
            case "=":
                lower = new Bound(version, true);
                upper = partial.IsComplete ? new Bound(version, true) : new Bound(partial.WrittenCeiling, false);
                break;
            default:
                lower = new Bound(version, true);
                upper = new Bound(partial.CaretCeiling, false);
                break;
        }

        return true;
    }

    private static string ReadComparator(string clause) =>
        clause switch
        {
            ['>', '=', ..] => ">=",
            ['<', '=', ..] => "<=",
            ['>', ..] => ">",
            ['<', ..] => "<",
            ['^', ..] => "^",
            ['~', ..] => "~",
            ['=', ..] => "=",
            _ => ""
        };

    /// <summary>
    ///     Reads the partial version a clause names — one to three components plus an optional pre-release — keeping
    ///     which of them were written, since that is what decides where a <c>^</c>, <c>~</c> or partial <c>=</c>
    ///     ceiling sits.
    /// </summary>
    private static bool TryReadPartial(string text, out Partial partial, [NotNullWhen(false)] out string? error)
    {
        partial = default;
        if (text.Length == 0)
        {
            error = "a version requirement clause must name a version.";
            return false;
        }

        if (text.Contains('+'))
        {
            error = $"version requirement '{text}' cannot name build metadata, which takes no part in comparison.";
            return false;
        }

        var prereleaseStart = text.IndexOf('-');
        var prerelease = prereleaseStart < 0 ? null : text[(prereleaseStart + 1)..];
        var components = (prereleaseStart < 0 ? text : text[..prereleaseStart]).Split('.');
        if (components.Length > 3)
        {
            error = $"version requirement '{text}' may name at most three components, written 'major.minor.patch'.";
            return false;
        }

        var completed = string.Join('.', components.Concat(Enumerable.Repeat("0", 3 - components.Length)));
        if (!Version.TryParse(prerelease == null ? completed : $"{completed}-{prerelease}", out var version, out var versionError))
        {
            error = versionError;
            return false;
        }

        partial = new Partial(version, components.Length);
        error = null;
        return true;
    }

    private static Bound? TighterLower(Bound? left, Bound? right)
    {
        if (left is not { } lower)
            return right;

        if (right is not { } other)
            return left;

        var comparison = lower.Version.CompareTo(other.Version);
        if (comparison != 0)
            return comparison > 0 ? left : right;

        return lower.IsInclusive ? right : left;
    }

    private static Bound? TighterUpper(Bound? left, Bound? right)
    {
        if (left is not { } upper)
            return right;

        if (right is not { } other)
            return left;

        var comparison = upper.Version.CompareTo(other.Version);
        if (comparison != 0)
            return comparison < 0 ? left : right;

        return upper.IsInclusive ? right : left;
    }

    private static bool IsEmpty(Bound? lower, Bound? upper)
    {
        if (lower is not { } low || upper is not { } high)
            return false;

        var comparison = low.Version.CompareTo(high.Version);
        return comparison > 0 || (comparison == 0 && !(low.IsInclusive && high.IsInclusive));
    }

    private static bool NamesPrereleaseOf(Bound? bound, Version version) =>
        bound is { } named
        && named.Version.IsPrerelease
        && named.Version.Major == version.Major
        && named.Version.Minor == version.Minor
        && named.Version.Patch == version.Patch;

    private static string Describe(Bound? lower, Bound? upper)
    {
        if (lower is not { } low)
            return upper is { } only ? $"{(only.IsInclusive ? "<=" : "<")}{only.Version}" : "*";

        var floor = $"{(low.IsInclusive ? ">=" : ">")}{low.Version}";
        if (upper is not { } high)
            return floor;

        return low.IsInclusive && high.IsInclusive && low.Version == high.Version
            ? $"={low.Version}"
            : $"{floor}, {(high.IsInclusive ? "<=" : "<")}{high.Version}";
    }

    /// <summary>One end of the interval a requirement accepts, and whether the version naming it is itself accepted.</summary>
    public readonly record struct Bound(Version Version, bool IsInclusive);

    /// <summary>
    ///     A version as a clause wrote it: the components filled out to three, plus how many were actually written.
    /// </summary>
    private readonly record struct Partial(Version Completed, int WrittenComponents)
    {
        public bool IsComplete => WrittenComponents == 3;

        /// <summary>
        ///     Where <c>^</c> stops: the leftmost written component that is non-zero, incremented. A zero major or
        ///     minor makes the next component the compatibility boundary — <c>^0.2.3</c> allows no <c>0.3.0</c> —
        ///     and when everything written is zero the last written component is the one that moves.
        /// </summary>
        public Version CaretCeiling =>
            Completed.Major != 0 ? new Version(Completed.Major + 1, 0, 0)
            : WrittenComponents == 1 ? new Version(1, 0, 0)
            : Completed.Minor != 0 ? new Version(0, Completed.Minor + 1, 0)
            : WrittenComponents == 2 ? new Version(0, 1, 0)
            : new Version(0, 0, Completed.Patch + 1);

        /// <summary>
        ///     Past everything the unwritten components could have been: the last written component incremented.
        ///     That is where <c>~</c> stops, and where a partial <c>=</c> stops as well — <c>=1.2</c> asks for a
        ///     <c>1.2.x</c>, which is the same set <c>~1.2</c> asks for.
        /// </summary>
        public Version WrittenCeiling =>
            WrittenComponents == 1 ? new Version(Completed.Major + 1, 0, 0) : new Version(Completed.Major, Completed.Minor + 1, 0);
    }
}
