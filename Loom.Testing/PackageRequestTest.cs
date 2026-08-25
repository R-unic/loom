using Loom.Config;
using Loom.Packages;

namespace Loom.Testing;

/// <summary>
///     What a command line can say about a package to add: a name, and optionally the versions of it that will do.
/// </summary>
[Collection("Assembly")]
public class PackageRequestTest
{
    [Fact]
    public void TryParse_ReadsANameWithNoRequirement()
    {
        Assert.True(PackageRequest.TryParse("scope/math", false, out var request, out var error));

        Assert.Null(error);
        Assert.Equal(PackageName.Parse("scope/math"), request.Name);
        Assert.Null(request.Requirement);
    }

    [Fact]
    public void TryParse_ReadsTheRequirementAfterTheName()
    {
        Assert.True(PackageRequest.TryParse("math@>=1.4, <2", true, out var request, out _));

        Assert.Equal(VersionRequirement.Parse(">=1.4, <2"), request.Requirement);
        Assert.True(request.IsDevelopmentOnly);
    }

    /// <remarks>A bare version is a requirement like any other, and means in a request what it means in a manifest.</remarks>
    [Fact]
    public void TryParse_ReadsABareVersion_AsTheRequirementItIs()
    {
        Assert.True(PackageRequest.TryParse("math@1.2.3", false, out var request, out _));

        Assert.Equal(VersionRequirement.Parse("^1.2.3"), request.Requirement);
    }

    [Fact]
    public void TryParse_ReportsANameThatIsNotOne()
    {
        Assert.False(PackageRequest.TryParse("1math", false, out var request, out var error));

        Assert.Null(request);
        Assert.Contains("must start with a letter", error);
    }

    [Fact]
    public void TryParse_ReportsARequirementThatIsNotOne()
    {
        Assert.False(PackageRequest.TryParse("math@>=2, <1", false, out _, out var error));

        Assert.Contains("cannot be satisfied", error);
    }

    [Fact]
    public void TryParse_ReportsNothingAtAll()
    {
        Assert.False(PackageRequest.TryParse("  ", false, out _, out var error));

        Assert.Contains("expected a package", error);
    }
}
