using Loom.Core.Resolving.Symbols;

namespace Loom.Core.TypeChecking.Serialization;

/// <summary>
///     The compile-time wire format of a <c>[serializable]</c> interface. Nothing of this survives into the
///     output: the generator consumes it to emit straight-line code with constant offsets, so there is no
///     runtime schema to walk.
/// </summary>
public sealed record SerializationSchema(InterfaceSymbol Interface, IReadOnlyList<SerializationField> Fields)
{
    /// <summary>Bits in the header written at offset zero: bools, optional-presence bits, union tags, ranged numbers, sentinels.</summary>
    public int HeaderBits { get; } = Fields.Sum(f => f.HeaderBits);

    /// <summary>Body width when every field is fixed-width, otherwise <c>null</c>.</summary>
    public int? BodyBytes { get; } = Fields.Aggregate((int?)0, (total, f) => total + f.BodyBytes);

    public bool HasBlobs { get; } = Fields.Any(HasBlobsIn);

    public int HeaderBytes => BitWidth.ToByteCount(HeaderBits);

    /// <summary>
    ///     A fixed-size type skips the measuring pass entirely and allocates a compile-time constant, which
    ///     is the fast path <c>[packed]</c> gives up in exchange for sentinels.
    /// </summary>
    public bool IsFixedSize => BodyBytes != null;

    /// <summary>Total width when <see cref="IsFixedSize" />, otherwise <c>null</c>.</summary>
    public int? FixedByteCount => IsFixedSize ? HeaderBytes + BodyBytes : null;

    /// <summary>An all-zero-width type sends no buffer at all; <c>Serialized.buffer</c> is nil in that case.</summary>
    public bool IsEmpty => FixedByteCount == 0;

    private static bool HasBlobsIn(SerializationField serializationField) =>
        serializationField.ProducesBlob || serializationField.Children.Any(HasBlobsIn);
}
