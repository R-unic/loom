using Loom.Core.Text;

namespace Loom.Testing;

public class LocationTest
{
    private static readonly SourceFile _testFile = new("test.loom", "let x: number = 5;\nlet y = x + 10;\nprint(y);");

    [Fact]
    public void Equals_ComparesPosition_NotColumn()
    {
        var firstLineStart = new Location(_testFile, 0);
        var secondLineStart = new Location(_testFile, 19);
        Assert.Equal(firstLineStart.Character, secondLineStart.Character);
        Assert.NotEqual(firstLineStart.Line, secondLineStart.Line);
        Assert.NotEqual(firstLineStart, secondLineStart);
        Assert.False(firstLineStart.Equals(secondLineStart));
    }

    [Fact]
    public void Equals_And_GetHashCode_AgreeForSamePosition()
    {
        var a = new Location(_testFile, 19);
        var b = new Location(_testFile, 19);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void GetHashCode_DiffersForDifferentPositions_SameColumn()
    {
        var firstLineStart = new Location(_testFile, 0);
        var secondLineStart = new Location(_testFile, 19);
        Assert.NotEqual(firstLineStart.GetHashCode(), secondLineStart.GetHashCode());
    }

    [Fact]
    public void EqualityOperator_ComparesByFileAndPosition()
    {
        var a = new Location(_testFile, 19);
        var b = new Location(_testFile, 19);
        var c = new Location(_testFile, 0);
        Assert.True(a == b);
        Assert.False(a == c);
    }

    [Fact]
    public void InequalityOperator_ComparesByFileAndPosition()
    {
        var a = new Location(_testFile, 19);
        var b = new Location(_testFile, 0);
        Assert.True(a != b);
        Assert.False(a != new Location(_testFile, 19));
    }

    [Fact]
    public void ObjectEquals_NonLocationObject_ReturnsFalse()
    {
        var location = new Location(_testFile, 0);

        // ReSharper disable once SuspiciousTypeConversion.Global
        Assert.False(location.Equals("not a location"));
    }

    [Fact]
    public void ObjectEquals_BoxedLocation_ComparesCorrectly()
    {
        var a = new Location(_testFile, 19);
        object b = new Location(_testFile, 19);
        Assert.True(a.Equals(b));
    }

    [Fact]
    public void AdditionOperator_AdvancesPositionWithinSameFile()
    {
        var start = new Location(_testFile, 0);
        var advanced = start + 4;
        Assert.Equal(4, advanced.Position);
        Assert.Equal(start.File, advanced.File);
    }

    [Fact]
    public void Empty_CreatesLocationAtPositionZero()
    {
        var empty = Location.Empty(_testFile);
        Assert.Equal(0, empty.Position);
        Assert.Equal(_testFile, empty.File);
    }
}

public class LocationSpanTest
{
    private static readonly SourceFile _testFile = new("test.loom", "let x: number = 5;\nlet y = x + 10;\nprint(y);");

    [Fact]
    public void EqualityOperators_CompareByFileStartAndEnd()
    {
        var a = new LocationSpan(new Location(_testFile, 0), 3);
        var b = new LocationSpan(new Location(_testFile, 0), 3);
        var c = new LocationSpan(new Location(_testFile, 4), 3);

        Assert.True(a == b);
        Assert.False(a == c);
        Assert.False(a != b);
        Assert.True(a != c);
    }

    [Fact]
    public void Equals_Object_ComparesLikeTheTypedOverload()
    {
        var a = new LocationSpan(new Location(_testFile, 0), 3);
        var b = new LocationSpan(new Location(_testFile, 0), 3);

        Assert.True(a.Equals((object)b));
        Assert.False(a.Equals("not a span"));
    }
}