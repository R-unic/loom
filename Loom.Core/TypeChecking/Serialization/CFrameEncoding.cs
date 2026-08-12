namespace Loom.Core.TypeChecking.Serialization;

/// <summary>
///     Mirrors the intrinsic <c>CFrameType</c> enum in <c>Intrinsic/loom.loom</c>; member order matches the
///     Loom declaration because attribute arguments arrive as ordinals.
/// </summary>
public enum CFrameEncoding : byte
{
    /// <summary>
    ///     Position plus a smallest-three quaternion: the largest-magnitude component is dropped and
    ///     recovered from the unit-length constraint, leaving 2 index bits and 3 components at 10 bits
    ///     each - exactly 32 bits of rotation. Measured worst-case angular error is about 0.24 degrees
    ///     over 200k random rotations.
    /// </summary>
    Compressed,

    /// <summary>Position plus all four quaternion components at the field's number type.</summary>
    Precise
}

public static class CFrameEncodingExtensions
{
    /// <summary>2 bits for the dropped component index plus 10 bits for each of the three kept components.</summary>
    public const int CompressedRotationBits = 2 + CompressedComponentBits * 3;

    public const int CompressedComponentBits = 10;
}
