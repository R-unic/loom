using Version = Loom.Config.Version;

namespace Loom.Testing.Config;

public class VersionTest
{
    private static readonly string[] _ascendingVersionStrings = ["0.9.9", "1.0.0-alpha", "1.0.0"];

    [Fact]
    public void Parse_ReadsEveryComponent()
    {
        var version = Version.Parse("1.2.3-alpha.1+build.5");
        Assert.Equal(1, version.Major);
        Assert.Equal(2, version.Minor);
        Assert.Equal(3, version.Patch);
        Assert.Equal("alpha.1", version.Prerelease);
        Assert.Equal("build.5", version.BuildMetadata);
        Assert.True(version.IsPrerelease);
    }

    [Fact]
    public void Parse_StableRelease_HasNoPrereleaseOrBuildMetadata()
    {
        var version = Version.Parse("0.3.1");
        Assert.Null(version.Prerelease);
        Assert.Null(version.BuildMetadata);
        Assert.False(version.IsPrerelease);
    }

    [Theory]
    [InlineData("0.0.0")]
    [InlineData("0.3.1")]
    [InlineData("10.20.30")]
    [InlineData("1.0.0-alpha")]
    [InlineData("1.0.0-alpha.1")]
    [InlineData("1.0.0-0.3.7")]
    [InlineData("1.0.0-x-y-z")]
    [InlineData("1.0.0+build")]
    [InlineData("1.0.0-beta+exp.sha.5114f85")]
    public void ToString_RoundTripsTheWrittenForm(string text) => Assert.Equal(text, Version.Parse(text).ToString());

    [Theory]
    [InlineData(null, "cannot be empty")]
    [InlineData("", "cannot be empty")]
    [InlineData("1", "exactly three components")]
    [InlineData("1.2", "exactly three components")]
    [InlineData("1.2.3.4", "exactly three components")]
    [InlineData("x.2.3", "invalid major component")]
    [InlineData("1.x.3", "invalid minor component")]
    [InlineData("1.2.x", "invalid patch component")]
    [InlineData("01.2.3", "invalid major component")]
    [InlineData("1.2.3-", "cannot be empty")]
    [InlineData("1.2.3-alpha..1", "cannot be empty")]
    [InlineData("1.2.3-al$pha", "may only contain letters")]
    [InlineData("1.2.3-01", "leading zeroes")]
    [InlineData("1.2.3+bu$ild", "may only contain letters")]
    public void TryParse_InvalidForms_ReportAReason(string? text, string expectedReason)
    {
        Assert.False(Version.TryParse(text, out var version, out var error));
        Assert.Null(version);
        Assert.NotNull(error);
        Assert.Contains(expectedReason, error);
    }

    [Fact]
    public void Parse_InvalidForm_Throws()
    {
        var exception = Assert.Throws<FormatException>(() => Version.Parse("1.2"));
        Assert.Contains("exactly three components", exception.Message);
    }

    [Fact]
    public void Constructor_RejectsNegativeComponentsAndMalformedIdentifiers()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Version(1, -1, 0));
        Assert.Throws<FormatException>(() => new Version(1, 0, 0, "al pha"));
        Assert.Throws<FormatException>(() => new Version(1, 0, 0, buildMetadata: "bu ild"));
    }

    [Theory]
    [InlineData("1.0.0", "2.0.0")]
    [InlineData("1.0.0", "1.1.0")]
    [InlineData("1.0.0", "1.0.1")]
    [InlineData("1.0.0-alpha", "1.0.0")]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta")]
    [InlineData("1.0.0-alpha.beta", "1.0.0-beta")]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.11")]
    [InlineData("1.0.0-rc.1", "1.0.0")]
    public void CompareTo_OrdersByPrecedence(string lower, string higher)
    {
        var left = Version.Parse(lower);
        var right = Version.Parse(higher);

        Assert.True(left.CompareTo(right) < 0);
        Assert.True(right.CompareTo(left) > 0);
        Assert.True(left < right);
        Assert.True(right > left);
        Assert.True(left <= right);
        Assert.True(right >= left);
    }

    [Fact]
    public void CompareTo_BuildMetadata_DoesNotAffectPrecedence()
    {
        var withBuild = Version.Parse("1.0.0+build.1");
        var withoutBuild = Version.Parse("1.0.0");

        Assert.Equal(0, withBuild.CompareTo(withoutBuild));
        Assert.True(withBuild.Equals(withoutBuild));
        Assert.Equal(withBuild.GetHashCode(), withoutBuild.GetHashCode());
        Assert.True(withBuild <= withoutBuild);
        Assert.True(withBuild >= withoutBuild);
    }

    [Fact]
    public void CompareTo_Null_SortsFirst()
    {
        var version = Version.Parse("1.0.0");

        Assert.True(version.CompareTo(null) > 0);
        Assert.True(version.CompareTo((object?)null) > 0);
        Assert.True(version > null);
        Assert.True(null < version);
    }

    [Fact]
    public void CompareTo_NonVersion_Throws() => Assert.Throws<ArgumentException>(() => Version.Parse("1.0.0").CompareTo("1.0.0"));

    [Fact]
    public void CompareTo_BoxedVersion_OrdersByPrecedence() =>
        Assert.True(Version.Parse("2.0.0").CompareTo((object)Version.Parse("1.0.0")) > 0);

    [Fact]
    public void Sort_OrdersAscendingByPrecedence()
    {
        var versions = new List<Version> { Version.Parse("1.0.0"), Version.Parse("1.0.0-alpha"), Version.Parse("0.9.9") };
        versions.Sort();

        Assert.Equal(_ascendingVersionStrings, versions.ConvertAll(version => version.ToString()));
    }

    [Fact]
    public void Equals_ComparesPrecedenceComponents()
    {
        var version = Version.Parse("1.2.3-alpha");

        Assert.True(version.Equals(Version.Parse("1.2.3-alpha")));
        Assert.True(version == Version.Parse("1.2.3-alpha"));
        Assert.True(version != Version.Parse("1.2.3"));
        Assert.False(version.Equals(Version.Parse("1.2.4-alpha")));
        Assert.Equal(Version.Parse("1.2.3-alpha").GetHashCode(), version.GetHashCode());
    }

    [Fact]
    public void Equals_HandlesNullAndOtherTypes()
    {
        var version = Version.Parse("1.0.0");

        Assert.False(version.Equals(null));
        // ReSharper disable once SuspiciousTypeConversion.Global
        Assert.False(version.Equals("1.0.0"));
        Assert.NotNull(version);
        // ReSharper disable once RedundantCast
        Assert.Null((Version?)null);
    }
}
