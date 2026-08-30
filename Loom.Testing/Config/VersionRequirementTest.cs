using Loom.Config;
using Version = Loom.Config.Version;
using Loom.Testing;

namespace Loom.Testing.Config;

public class VersionRequirementTest
{
    private static readonly string[] AgreeingRequirementStrings = ["^1.2", ">=1.4, <2", "<1.9"];
    private static readonly string[] DisagreeingRequirementStrings = ["^1.2", ">=1.4, <2", "<1.3"];
    private static readonly string[] PublishedVersionStrings = ["1.0.0", "1.2.0", "1.4.0", "1.4.1-beta.1", "1.9.3", "2.0.0"];

    [Theory]
    [InlineData("*", null, null)]
    [InlineData("^1.2.3", ">=1.2.3", "<2.0.0")]
    [InlineData("^1.2", ">=1.2.0", "<2.0.0")]
    [InlineData("^1", ">=1.0.0", "<2.0.0")]
    [InlineData("1.2", ">=1.2.0", "<2.0.0")]
    [InlineData("^0.2.3", ">=0.2.3", "<0.3.0")]
    [InlineData("^0.0.3", ">=0.0.3", "<0.0.4")]
    [InlineData("^0.0", ">=0.0.0", "<0.1.0")]
    [InlineData("^0", ">=0.0.0", "<1.0.0")]
    [InlineData("~1.2.3", ">=1.2.3", "<1.3.0")]
    [InlineData("~1.2", ">=1.2.0", "<1.3.0")]
    [InlineData("~1", ">=1.0.0", "<2.0.0")]
    [InlineData("=1.2.3", ">=1.2.3", "<=1.2.3")]
    [InlineData("=1.2", ">=1.2.0", "<1.3.0")]
    [InlineData(">=1.4", ">=1.4.0", null)]
    [InlineData(">1.4.0", ">1.4.0", null)]
    [InlineData("<2", null, "<2.0.0")]
    [InlineData("<=2.0.0", null, "<=2.0.0")]
    [InlineData(">=1.4, <2", ">=1.4.0", "<2.0.0")]
    public void Parse_DesugarsAClauseIntoAnInterval(string requirement, string? lower, string? upper)
    {
        var parsed = VersionRequirement.Parse(requirement);

        Assert.Equal(lower, Describe(parsed.Lower, ">=", ">"));
        Assert.Equal(upper, Describe(parsed.Upper, "<=", "<"));
        return;

        static string? Describe(VersionRequirement.Bound? bound, string inclusive, string exclusive) =>
            bound is { } present ? $"{(present.IsInclusive ? inclusive : exclusive)}{present.Version}" : null;
    }

    [Theory]
    [InlineData("^1.2", "1.2.0", true)]
    [InlineData("^1.2", "1.4.0", true)]
    [InlineData("^1.2", "1.99.99", true)]
    [InlineData("^1.2", "1.1.9", false)]
    [InlineData("^1.2", "2.0.0", false)]
    [InlineData("~1.2.3", "1.2.3", true)]
    [InlineData("~1.2.3", "1.2.9", true)]
    [InlineData("~1.2.3", "1.2.2", false)]
    [InlineData("~1.2.3", "1.3.0", false)]
    [InlineData("^0.2.3", "0.2.9", true)]
    [InlineData("^0.2.3", "0.3.0", false)]
    [InlineData("=1.2.3", "1.2.3", true)]
    [InlineData("=1.2.3", "1.2.4", false)]
    [InlineData("=1.2", "1.2.7", true)]
    [InlineData("=1.2", "1.3.0", false)]
    [InlineData("*", "0.0.1", true)]
    [InlineData("*", "99.0.0", true)]
    [InlineData(">1.4.0", "1.4.0", false)]
    [InlineData(">1.4.0", "1.4.1", true)]
    [InlineData("<=2.0.0", "2.0.0", true)]
    [InlineData(">=1.4, <2", "1.4.0", true)]
    [InlineData(">=1.4, <2", "1.9.9", true)]
    [InlineData(">=1.4, <2", "1.3.9", false)]
    [InlineData(">=1.4, <2", "2.0.0", false)]
    [InlineData("1.2.3", "1.2.3+build.5", true)]
    public void Satisfies_AnswersOverTheInterval(string requirement, string version, bool expected) =>
        Assert.Equal(expected, VersionRequirement.Parse(requirement).Satisfies(Version.Parse(version)));

    [Theory]
    [InlineData("*", "1.0.0-beta", false)]
    [InlineData("^1.2", "1.3.0-beta.1", false)]
    [InlineData("^1.2", "2.0.0-beta.1", false)]
    [InlineData(">=1.2.0", "1.3.0-beta.1", false)]
    [InlineData(">=1.2.0-alpha", "1.2.0-beta", true)]
    [InlineData(">=1.2.0-alpha", "1.2.0-alpha", true)]
    [InlineData(">=1.2.0-beta", "1.2.0-alpha", false)]
    [InlineData(">=1.2.0-alpha", "1.5.0-beta", false)]
    [InlineData(">=1.2.0-alpha", "1.5.0", true)]
    [InlineData("<2.0.0-beta.2", "2.0.0-beta.1", true)]
    [InlineData("^0.0.3-beta", "0.0.3-rc", true)]
    [InlineData("=1.2.3-beta", "1.2.3-beta", true)]
    public void Satisfies_OnlyAcceptsAPrereleaseABoundNames(string requirement, string version, bool expected) =>
        Assert.Equal(expected, VersionRequirement.Parse(requirement).Satisfies(Version.Parse(version)));

    [Theory]
    [InlineData(null, "cannot be empty")]
    [InlineData("", "cannot be empty")]
    [InlineData("   ", "cannot be empty")]
    [InlineData("^1.2,", "clause cannot be empty")]
    [InlineData("not-a-version", "invalid major component")]
    [InlineData("^", "must name a version")]
    [InlineData(">=", "must name a version")]
    [InlineData("1.2.3.4", "at most three components")]
    [InlineData("1.x", "invalid minor component")]
    [InlineData("01.2", "invalid major component")]
    [InlineData("1.2.3+build", "cannot name build metadata")]
    [InlineData("1.2.3-01", "leading zeroes")]
    [InlineData(">=2, <1", "cannot be satisfied by any version")]
    [InlineData(">=2.0.0, <2.0.0", "cannot be satisfied by any version")]
    [InlineData("^1.2, ^2.0", "cannot be satisfied by any version")]
    public void TryParse_InvalidForms_ReportAReason(string? requirement, string expectedReason)
    {
        Assert.False(VersionRequirement.TryParse(requirement, out var parsed, out var error));
        Assert.Null(parsed);
        Assert.NotNull(error);
        Assert.Contains(expectedReason, error);
    }

    [Fact]
    public void Parse_InvalidForm_Throws() => Assert.Throws<FormatException>(() => VersionRequirement.Parse(">=2, <1"));

    [Theory]
    [InlineData("*")]
    [InlineData("^1.2")]
    [InlineData(">=1.4, <2")]
    [InlineData(" ^1.2 ")]
    public void ToString_AnswersWithTheWrittenForm(string requirement) =>
        Assert.Equal(requirement.Trim(), VersionRequirement.Parse(requirement).ToString());

    [Theory]
    [InlineData("*", "*")]
    [InlineData("^1.2", ">=1.2.0, <2.0.0")]
    [InlineData("=1.2.3", "=1.2.3")]
    [InlineData(">1.4.0", ">1.4.0")]
    [InlineData("<=2.0.0", "<=2.0.0")]
    public void ToComparatorString_SpellsTheInterval(string requirement, string expected) =>
        Assert.Equal(expected, VersionRequirement.Parse(requirement).ToComparatorString());

    [Fact]
    public void Equality_IsOverTheVersionsAccepted_NotTheSpelling()
    {
        Assert.Equal(VersionRequirement.Parse("^1.2"), VersionRequirement.Parse(">=1.2.0, <2.0.0"));
        Assert.Equal(VersionRequirement.Parse("^1.2").GetHashCode(), VersionRequirement.Parse(">=1.2.0, <2.0.0").GetHashCode());
        Assert.True(VersionRequirement.Parse("~1.2") == VersionRequirement.Parse("=1.2"));
        Assert.True(VersionRequirement.Parse("^1.2") != VersionRequirement.Parse("^1.3"));
        Assert.Equal(VersionRequirement.Any, VersionRequirement.Parse("*"));
        Assert.True(VersionRequirement.Parse("*").IsAny);
        Assert.False(VersionRequirement.Parse("^1.2").IsAny);
    }

    [Theory]
    [InlineData("^1.2", ">=1.4, <2", ">=1.4.0, <2.0.0")]
    [InlineData("^1.2", "*", "^1.2")]
    [InlineData("*", "^1.2", "^1.2")]
    [InlineData("^1.2", "^1.2", "^1.2")]
    [InlineData(">=1.0.0", ">=1.0.0", ">=1.0.0")]
    [InlineData(">=1.0.0", ">1.0.0", ">1.0.0")]
    [InlineData("<2.0.0", "<=2.0.0", "<2.0.0")]
    [InlineData("^1.2", "~1.4", ">=1.4.0, <1.5.0")]
    [InlineData(">=1.0.0", "=1.2.3", "=1.2.3")]
    public void Intersect_NarrowsToWhatBothAccept(string left, string right, string expected)
    {
        var intersection = VersionRequirement.Parse(left).Intersect(VersionRequirement.Parse(right));

        Assert.NotNull(intersection);
        Assert.Equal(VersionRequirement.Parse(expected), intersection);
        Assert.Equal(VersionRequirement.Parse(expected).ToComparatorString(), intersection.ToComparatorString());
    }

    [Theory]
    [InlineData("^1.2", "^2.0")]
    [InlineData("^1.2", ">=2")]
    [InlineData("=1.2.3", "=1.2.4")]
    [InlineData(">=1.0.0", "<1.0.0")]
    [InlineData("~1.2", "~1.3")]
    public void Intersect_DisagreeingRequirements_HaveNoAnswer(string left, string right) =>
        Assert.Null(VersionRequirement.Parse(left).Intersect(VersionRequirement.Parse(right)));

    [Fact]
    public void Intersect_IsWhatEveryDependentAccepts()
    {
        var requirements = AgreeingRequirementStrings.Select(VersionRequirement.Parse).ToArray();
        var intersection = VersionRequirement.Intersect(requirements);

        Assert.NotNull(intersection);
        Assert.Equal(">=1.4.0, <1.9.0", intersection.ToComparatorString());
        Assert.True(intersection.Satisfies(Version.Parse("1.4.0")));
        Assert.False(intersection.Satisfies(Version.Parse("1.9.0")));
        Assert.All(requirements, requirement => Assert.True(requirement.Satisfies(Version.Parse("1.5.0"))));
    }

    [Fact]
    public void Intersect_OfNothing_ConstrainsNothing() => Assert.Equal(VersionRequirement.Any, VersionRequirement.Intersect([]));

    [Fact]
    public void Intersect_OfOneRequirement_IsThatRequirement()
    {
        var requirement = VersionRequirement.Parse("^1.2");
        Assert.Same(requirement, VersionRequirement.Intersect([requirement]));
    }

    [Fact]
    public void Intersect_WhenOneDependentDisagrees_HasNoAnswer() =>
        Assert.Null(VersionRequirement.Intersect(DisagreeingRequirementStrings.Select(VersionRequirement.Parse)));

    [Fact]
    public void Satisfies_PicksTheHighestPublishedVersion()
    {
        var published = PublishedVersionStrings.Select(Version.Parse).ToArray();
        var requirement = VersionRequirement.Parse("^1.2");

        Assert.Equal(Version.Parse("1.9.3"), published.Where(requirement.Satisfies).Max());
    }
}
