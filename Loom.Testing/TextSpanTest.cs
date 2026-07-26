using Loom.Core.Text;

namespace Loom.Testing;

public class TextSpanTest
{
    [Fact]
    public void Empty_HasZeroPositionAndLength()
    {
        var span = TextSpan.Empty;
        Assert.Equal(0, span.Position);
        Assert.Equal(0, span.Length);
        Assert.Equal(0, span.End);
    }

    [Fact]
    public void End_IsPositionPlusLength()
    {
        var span = new TextSpan(5, 10);
        Assert.Equal(15, span.End);
    }

    [Fact]
    public void FromStartEnd_ComputesLength()
    {
        var span = TextSpan.FromStartEnd(5, 15);
        Assert.Equal(5, span.Position);
        Assert.Equal(10, span.Length);
    }

    [Fact]
    public void Equals_ComparesPositionAndLength()
    {
        var a = new TextSpan(1, 2);
        var b = new TextSpan(1, 2);
        var c = new TextSpan(1, 3);
        Assert.True(a.Equals(b));
        Assert.False(a.Equals(c));

        // ReSharper disable once SuspiciousTypeConversion.Global
        Assert.False(a.Equals("not a span"));
        Assert.True(a.Equals((object)b));
    }

    [Fact]
    public void EqualityOperators_MatchEquals()
    {
        var a = new TextSpan(1, 2);
        var b = new TextSpan(1, 2);
        var c = new TextSpan(3, 4);
        Assert.True(a == b);
        Assert.False(a == c);
        Assert.True(a != c);
        Assert.False(a != b);
    }

    [Fact]
    public void GetHashCode_SameForEqualSpans()
    {
        var a = new TextSpan(1, 2);
        var b = new TextSpan(1, 2);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void ToString_FormatsAsRange()
    {
        var span = new TextSpan(3, 7);
        Assert.Equal("[3..10]", span.ToString());
    }
}