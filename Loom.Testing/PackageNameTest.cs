using Loom.Config;

namespace Loom.Testing;

public class PackageNameTest
{
    [Fact]
    public void Parse_UnscopedName_HasNoScope()
    {
        var packageName = PackageName.Parse("tether");
        Assert.Null(packageName.Scope);
        Assert.Equal("tether", packageName.Name);
        Assert.False(packageName.IsScoped);
    }

    [Fact]
    public void Parse_ScopedName_SplitsOnSlash()
    {
        var packageName = PackageName.Parse("alternativelua/tether");
        Assert.Equal("alternativelua", packageName.Scope);
        Assert.Equal("tether", packageName.Name);
        Assert.True(packageName.IsScoped);
    }

    [Theory]
    [InlineData("tether")]
    [InlineData("alternativelua/tether")]
    [InlineData("a")]
    [InlineData("net-serio_2")]
    [InlineData("Tether")]
    public void ToString_RoundTripsTheWrittenForm(string text) => Assert.Equal(text, PackageName.Parse(text).ToString());

    [Theory]
    [InlineData(null, "cannot be empty")]
    [InlineData("", "cannot be empty")]
    [InlineData("   ", "cannot be empty")]
    [InlineData("scope/name/extra", "at most one '/'")]
    [InlineData("/tether", "scope cannot be empty")]
    [InlineData("scope/", "name cannot be empty")]
    [InlineData("1tether", "must start with a letter")]
    [InlineData("-tether", "must start with a letter")]
    [InlineData("te ther", "letters, digits")]
    [InlineData("te.ther", "letters, digits")]
    [InlineData("scope/te$ther", "letters, digits")]
    public void TryParse_InvalidForms_ReportAReason(string? text, string expectedReason)
    {
        Assert.False(PackageName.TryParse(text, out var packageName, out var error));
        Assert.Null(packageName);
        Assert.NotNull(error);
        Assert.Contains(expectedReason, error);
    }

    [Fact]
    public void TryParse_SegmentLongerThanTheMaximum_IsRejected()
    {
        var longest = "a" + new string('b', PackageName.MaximumSegmentLength - 1);
        Assert.True(PackageName.TryParse(longest, out _, out _));

        Assert.False(PackageName.TryParse(longest + "b", out _, out var error));
        Assert.NotNull(error);
        Assert.Contains($"at most {PackageName.MaximumSegmentLength} characters", error);
    }

    [Fact]
    public void Parse_InvalidForm_Throws()
    {
        var exception = Assert.Throws<FormatException>(() => PackageName.Parse("1tether"));
        Assert.Contains("must start with a letter", exception.Message);
    }

    [Fact]
    public void Equals_IgnoresCase()
    {
        var lower = PackageName.Parse("alternativelua/tether");
        var upper = PackageName.Parse("AlternativeLua/Tether");

        Assert.True(lower.Equals(upper));
        Assert.True(lower == upper);
        Assert.False(lower != upper);
        Assert.Equal(lower.GetHashCode(), upper.GetHashCode());
    }

    [Fact]
    public void Equals_DistinguishesScopeFromName()
    {
        var scoped = PackageName.Parse("serio/tether");
        var unscoped = PackageName.Parse("tether");

        Assert.False(scoped.Equals(unscoped));
        Assert.False(unscoped.Equals(scoped));
        Assert.NotEqual(scoped, unscoped);
    }

    [Fact]
    public void Equals_DifferentNames_AreNotEqual()
    {
        var tether = PackageName.Parse("tether");
        var serio = PackageName.Parse("serio");

        Assert.False(tether.Equals(serio));
        Assert.True(tether != serio);
    }

    [Fact]
    public void Equals_HandlesNullAndOtherTypes()
    {
        var packageName = PackageName.Parse("tether");

        Assert.False(packageName.Equals(null));
        // ReSharper disable once SuspiciousTypeConversion.Global
        Assert.False(packageName.Equals("tether"));
        Assert.False(packageName == null);
        Assert.True(packageName != null);
        Assert.False(null == packageName);
        Assert.True((PackageName?)null == null);
    }

    [Fact]
    public void Equals_WorksAsADictionaryKey()
    {
        var names = new Dictionary<PackageName, int> { [PackageName.Parse("alternativelua/tether")] = 1 };
        Assert.Equal(1, names[PackageName.Parse("AlternativeLua/tether")]);
    }

    /// <remarks>
    ///     Ordering exists so a file listing packages - a lock file - is written the same way whoever writes it.
    /// </remarks>
    [Fact]
    public void CompareTo_OrdersUnscopedNamesBeforeScopedOnes()
    {
        var names = new[]
        {
            PackageName.Parse("alternativelua/tether"),
            PackageName.Parse("serio"),
            PackageName.Parse("AlternativeLua/serio"),
            PackageName.Parse("runit")
        };

        Assert.Equal(
            ["runit", "serio", "AlternativeLua/serio", "alternativelua/tether"],
            names.Order().Select(name => name.ToString())
        );
    }

    [Fact]
    public void CompareTo_IgnoresCaseAndOrdersEqualNamesTogether()
    {
        Assert.Equal(0, PackageName.Parse("AlternativeLua/Tether").CompareTo(PackageName.Parse("alternativelua/tether")));
        Assert.True(PackageName.Parse("runit").CompareTo(PackageName.Parse("Serio")) < 0);
        Assert.True(PackageName.Parse("serio").CompareTo(null) > 0);
    }
}
