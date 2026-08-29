using Loom.Core.Text;

namespace Loom.Testing;

public class DocumentationTableTest
{
    [Fact]
    public void StripMarker_RemovesTheMarkerAndOneFollowingSpace() => Assert.Equal("hello", DocumentationTable.StripMarker("### hello"));

    [Fact]
    public void StripMarker_KeepsIndentationPastTheSingleSeparatorSpace() => Assert.Equal(" hello", DocumentationTable.StripMarker("###  hello"));

    [Fact]
    public void StripMarker_LeavesALineWithNoMarkerUnchanged() => Assert.Equal("hello", DocumentationTable.StripMarker("hello"));

    [Fact]
    public void StripMarker_TrimsLeadingWhitespaceBeforeTheMarker() => Assert.Equal("hello", DocumentationTable.StripMarker("   ### hello"));
}
