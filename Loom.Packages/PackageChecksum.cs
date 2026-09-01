using System.Security.Cryptography;

namespace Loom.Packages;

/// <summary>
///     Integrity of a package's bytes as an index states it: an algorithm, a colon, and lowercase hex. The one
///     form <see cref="PublishedPackage.Checksum" /> and <see cref="Loom.Config.LockedPackage.Checksum" /> are
///     written in, so what a registry publishes and what a lock records are the same string.
/// </summary>
/// <remarks>
///     Only SHA-256 is produced. Reading is deliberately no laxer: a checksum naming an algorithm this does not
///     know is not something to shrug at and install anyway, so <see cref="Same" /> answers false for it rather
///     than trying to be forward-compatible with a hash it cannot compute.
/// </remarks>
public static class PackageChecksum
{
    /// <summary>What every checksum this writes begins with.</summary>
    public const string Prefix = "sha256:";

    /// <summary>The checksum of <paramref name="content" />, in the form an index states one.</summary>
    public static string Of(ReadOnlySpan<byte> content) => Prefix + Convert.ToHexStringLower(SHA256.HashData(content));

    /// <summary>
    ///     Whether two stated checksums are the same one. Hex is compared case-insensitively — the case is a
    ///     spelling of the digest and not part of it — while the algorithm has to match exactly.
    /// </summary>
    public static bool Same(string? left, string? right) =>
        left != null
        && right != null
        && left.StartsWith(Prefix, StringComparison.Ordinal)
        && right.StartsWith(Prefix, StringComparison.Ordinal)
        && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
