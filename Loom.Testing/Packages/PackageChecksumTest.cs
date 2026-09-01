using Loom.Packages;

namespace Loom.Testing.Packages;

/// <summary>
///     Integrity of a package's bytes as an index states it, in the one form both a registry's index endpoint and a
///     lock file are written in.
/// </summary>
[Collection("Assembly")]
public class PackageChecksumTest
{
    [Fact]
    public void Of_IsTheDigest_InTheFormAnIndexStatesOne()
    {
        // the SHA-256 of the empty input, which is the one digest that can be written down without computing it
        Assert.Equal("sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", PackageChecksum.Of([]));
        Assert.StartsWith(PackageChecksum.Prefix, PackageChecksum.Of("a package"u8));
    }

    [Fact]
    public void Of_TheSameBytes_IsTheSameChecksum()
    {
        Assert.Equal(PackageChecksum.Of("a package"u8), PackageChecksum.Of("a package"u8));
        Assert.NotEqual(PackageChecksum.Of("a package"u8), PackageChecksum.Of("another package"u8));
    }

    /// <remarks>The case hex is written in is a spelling of the digest rather than part of it.</remarks>
    [Fact]
    public void Same_ComparesTheDigest_WithoutRegardToItsCase() =>
        Assert.True(PackageChecksum.Same("sha256:AABBCC", "sha256:aabbcc"));

    /// <remarks>
    ///     A checksum naming an algorithm this cannot compute is not something to shrug at and install anyway, so it
    ///     is answered false rather than treated as forward compatibility with a hash nothing here can check.
    /// </remarks>
    [Fact]
    public void Same_RefusesAnAlgorithmItDoesNotKnow()
    {
        Assert.False(PackageChecksum.Same("sha512:aabbcc", "sha512:aabbcc"));
        Assert.False(PackageChecksum.Same("aabbcc", "aabbcc"));
    }

    [Fact]
    public void Same_RefusesAChecksumThatIsNotStated()
    {
        Assert.False(PackageChecksum.Same(null, "sha256:aabbcc"));
        Assert.False(PackageChecksum.Same("sha256:aabbcc", null));
        Assert.False(PackageChecksum.Same(null, null));
    }
}
