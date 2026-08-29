using Loom.Config;
using Version = Loom.Config.Version;

namespace Loom.Testing;

/// <summary>
///     Runs <c>Conformance/semver.json</c>, the corpus <c>rbx-loom/loom-pm</c> executes against its Go port of
///     <see cref="Version" /> and <see cref="VersionRequirement" />. Everything here is also asserted by
///     <see cref="VersionTest" /> and <see cref="VersionRequirementTest" /> in the forms this codebase cares about;
///     what this adds is that the *other* implementation is being held to the same cases, so the two cannot drift
///     on what <c>^1.2</c> means without a failure on one side or the other.
/// </summary>
public class SemverConformanceTest
{
    public static IEnumerable<TheoryDataRow<string, bool>> ParseVersionCases =>
        ConformanceCorpus.Section(ConformanceCorpus.Semver, "parse_version")
            .Select(
                test => new TheoryDataRow<string, bool>(test.String("text"), test.Bool("valid"))
                {
                    TestDisplayName = test.Describe($"'{test.String("text")}'")
                }
            );

    public static IEnumerable<TheoryDataRow<string, string, int>> CompareCases =>
        ConformanceCorpus.Section(ConformanceCorpus.Semver, "compare")
            .Select(
                test => new TheoryDataRow<string, string, int>(
                    test.String("left"),
                    test.String("right"),
                    test.GetProperty("ordering").GetInt32()
                )
                {
                    TestDisplayName = test.Describe($"{test.String("left")} vs {test.String("right")}")
                }
            );

    public static IEnumerable<TheoryDataRow<string[], string[]>> SortCases =>
        ConformanceCorpus.Section(ConformanceCorpus.Semver, "sort")
            .Select(
                test => new TheoryDataRow<string[], string[]>(test.Strings("input"), test.Strings("expected"))
                {
                    TestDisplayName = test.Describe("ascending")
                }
            );

    public static IEnumerable<TheoryDataRow<string, bool>> ParseRequirementCases =>
        ConformanceCorpus.Section(ConformanceCorpus.Semver, "parse_requirement")
            .Select(
                test => new TheoryDataRow<string, bool>(test.String("text"), test.Bool("valid"))
                {
                    TestDisplayName = test.Describe($"'{test.String("text")}'")
                }
            );

    public static IEnumerable<TheoryDataRow<string, string, bool>> SatisfiesCases =>
        ConformanceCorpus.Section(ConformanceCorpus.Semver, "satisfies")
            .Select(
                test => new TheoryDataRow<string, string, bool>(
                    test.String("requirement"),
                    test.String("version"),
                    test.Bool("satisfied")
                )
                {
                    TestDisplayName = test.Describe($"'{test.String("requirement")}' and {test.String("version")}")
                }
            );

    public static IEnumerable<TheoryDataRow<string[], bool, string>> IntersectCases =>
        ConformanceCorpus.Section(ConformanceCorpus.Semver, "intersect")
            .Select(
                test => new TheoryDataRow<string[], bool, string>(
                    test.Strings("requirements"),
                    test.Bool("satisfiable"),
                    test.TryGetProperty("comparator", out var comparator) ? comparator.GetString()! : string.Empty
                )
                {
                    TestDisplayName = test.Describe($"[{string.Join("] [", test.Strings("requirements"))}]")
                }
            );

    [Theory]
    [MemberData(nameof(ParseVersionCases))]
    public void ParseVersion(string text, bool valid) => Assert.Equal(valid, Version.TryParse(text, out _, out _));

    [Theory]
    [MemberData(nameof(CompareCases))]
    public void Compare(string left, string right, int ordering)
    {
        var (first, second) = (Version.Parse(left), Version.Parse(right));
        Assert.Equal(ordering, Math.Sign(first.CompareTo(second)));
        Assert.Equal(-ordering, Math.Sign(second.CompareTo(first)));
        Assert.Equal(ordering == 0, first.Equals(second));
    }

    [Theory]
    [MemberData(nameof(SortCases))]
    public void Sort(string[] input, string[] expected) =>
        Assert.Equal(expected, input.Select(Version.Parse).Order().Select(version => version.ToString()));

    [Theory]
    [MemberData(nameof(ParseRequirementCases))]
    public void ParseRequirement(string text, bool valid) => Assert.Equal(valid, VersionRequirement.TryParse(text, out _, out _));

    [Theory]
    [MemberData(nameof(SatisfiesCases))]
    public void Satisfies(string requirement, string version, bool satisfied) =>
        Assert.Equal(satisfied, VersionRequirement.Parse(requirement).Satisfies(Version.Parse(version)));

    [Theory]
    [MemberData(nameof(IntersectCases))]
    public void Intersect(string[] requirements, bool satisfiable, string comparator)
    {
        var intersection = VersionRequirement.Intersect(requirements.Select(VersionRequirement.Parse));
        if (!satisfiable)
        {
            Assert.Null(intersection);
            return;
        }

        Assert.NotNull(intersection);
        Assert.Equal(comparator, intersection.ToComparatorString());
    }
}
