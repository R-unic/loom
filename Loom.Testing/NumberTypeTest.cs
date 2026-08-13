using Loom.Core.TypeChecking.Serialization;
using Loom.Core.TypeChecking.Types;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.Testing;

/// <summary>
///     What each wire type is worth: its width, its signedness, and the range a value has to sit inside.
/// </summary>
/// <remarks>
///     A wrong range here is the quietest bug the serializer can have - the checker accepts a value that
///     does not fit, and the buffer write truncates it into a different number at runtime.
/// </remarks>
[Collection("Assembly")]
public class NumberTypeTest
{
    [Theory]
    [InlineData(NumberType.I8, 1)]
    [InlineData(NumberType.U8, 1)]
    [InlineData(NumberType.I16, 2)]
    [InlineData(NumberType.U16, 2)]
    [InlineData(NumberType.I32, 4)]
    [InlineData(NumberType.U32, 4)]
    [InlineData(NumberType.F32, 4)]
    [InlineData(NumberType.F64, 8)]
    public void ByteCount_IsTheWidthOnTheWire(NumberType numberType, int expected) => Assert.Equal(expected, numberType.ByteCount());

    [Theory]
    [InlineData(NumberType.U8, true)]
    [InlineData(NumberType.U16, true)]
    [InlineData(NumberType.U32, true)]
    [InlineData(NumberType.I8, false)]
    [InlineData(NumberType.F32, false)]
    [InlineData(NumberType.F64, false)]
    public void IsUnsigned_MarksOnlyTheUnsignedIntegers(NumberType numberType, bool expected) => Assert.Equal(expected, numberType.IsUnsigned());

    [Theory]
    [InlineData(NumberType.I8, sbyte.MinValue, sbyte.MaxValue)]
    [InlineData(NumberType.U8, byte.MinValue, byte.MaxValue)]
    [InlineData(NumberType.I16, short.MinValue, short.MaxValue)]
    [InlineData(NumberType.U16, ushort.MinValue, ushort.MaxValue)]
    [InlineData(NumberType.I32, int.MinValue, int.MaxValue)]
    [InlineData(NumberType.U32, uint.MinValue, uint.MaxValue)]
    public void Range_IsTheIntegerRangeItCanHold(NumberType numberType, double minimum, double maximum)
    {
        var (actualMinimum, actualMaximum) = numberType.Range();

        Assert.Equal(minimum, actualMinimum);
        Assert.Equal(maximum, actualMaximum);
    }

    [Fact]
    public void Range_OfTheFloatingTypes_IsTheirOwn()
    {
        Assert.Equal((float.MinValue, float.MaxValue), NumberType.F32.Range());
        Assert.Equal((double.MinValue, double.MaxValue), NumberType.F64.Range());
    }

    [Theory]
    [InlineData(NumberType.U8, "u8")]
    [InlineData(NumberType.F64, "f64")]
    public void BufferSuffix_NamesTheBufferLibraryCall(NumberType numberType, string expected) => Assert.Equal(expected, numberType.BufferSuffix());

    /// <remarks>
    ///     The length prefix is part of the type: two sized strings that write their length differently are
    ///     different wire formats, and treating them as one would decode a buffer against the wrong header.
    /// </remarks>
    [Fact]
    public void SizedString_IsDistinguishedByItsLengthType()
    {
        Type u8 = new SizedStringType(NumberType.U8);
        Type alsoU8 = new SizedStringType(NumberType.U8);
        Type u16 = new SizedStringType(NumberType.U16);

        Assert.True(u8.Equals(alsoU8));
        Assert.False(u8.Equals(u16));
        Assert.Equal(u8.GetHashCode(), alsoU8.GetHashCode());
        Assert.Equal("string<u8>", u8.ToString());
    }
}
