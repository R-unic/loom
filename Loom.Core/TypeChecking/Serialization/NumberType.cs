namespace Loom.Core.TypeChecking.Serialization;

/// <summary>Mirrors the intrinsic <c>NumberType</c> enum declared in <c>Intrinsic/loom.loom</c>.</summary>
public enum NumberType : byte
{
    I8,
    U8,
    I16,
    U16,
    I32,
    U32,
    F32,
    F64
}

public static class NumberTypeExtensions
{
    /// <summary>Width on the wire. Loom numbers are Luau doubles, so <see cref="NumberType.F32" /> is the lossy default.</summary>
    public static int ByteCount(this NumberType numberType) =>
        numberType switch
        {
            NumberType.I8 or NumberType.U8 => 1,
            NumberType.I16 or NumberType.U16 => 2,
            NumberType.I32 or NumberType.U32 or NumberType.F32 => 4,
            _ => 8
        };

    public static bool IsUnsigned(this NumberType numberType) =>
        numberType is NumberType.U8 or NumberType.U16 or NumberType.U32;

    /// <summary>Name of the <c>buffer</c> library suffix, e.g. <c>u8</c> for <c>buffer.writeu8</c>.</summary>
    public static string BufferSuffix(this NumberType numberType) => numberType.ToString().ToLowerInvariant();

    public static (double Minimum, double Maximum) Range(this NumberType numberType) =>
        numberType switch
        {
            NumberType.I8 => (sbyte.MinValue, sbyte.MaxValue),
            NumberType.U8 => (byte.MinValue, byte.MaxValue),
            NumberType.I16 => (short.MinValue, short.MaxValue),
            NumberType.U16 => (ushort.MinValue, ushort.MaxValue),
            NumberType.I32 => (int.MinValue, int.MaxValue),
            NumberType.U32 => (uint.MinValue, uint.MaxValue),
            NumberType.F32 => (float.MinValue, float.MaxValue),
            _ => (double.MinValue, double.MaxValue)
        };
}
