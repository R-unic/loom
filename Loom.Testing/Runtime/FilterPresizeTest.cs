using NuLua;
using NuLua.Luau;
using Loom.Testing;

namespace Loom.Testing.Runtime;

/// <summary>
///     The instance filter macros allocate their result at the source's length and write through a
///     counter. These execute the emitted loop to prove the array still comes back with exactly the
///     matching elements and the right length - presizing reserves capacity, it must not set length.
/// </summary>
[Collection("Assembly")]
public class FilterPresizeTest
{
    private const string Source = """
        declare let world: Workspace;
        let parts = world.get_children::<BasePart>();
        """;

    [Fact]
    public void ThePresizedResultIsAllocatedAtTheSourceLength()
    {
        var rendered = Utility.GetLuauAST(Source, typeCheck: true).Render();

        Assert.Contains("table.create(#_source)", rendered);
        Assert.DoesNotContain("table.insert", rendered);
        Assert.Contains("_result[_count] = child", rendered);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(4, 0)]
    [InlineData(4, 2)]
    [InlineData(4, 4)]
    public void TheFilteredArrayHasOnlyTheMatchesAndTheRightLength(int total, int matching)
    {
        var luau = Utility.GetLuauAST(Source, typeCheck: true).Render().Replace("const ", "local ");
        var prelude = $$"""
            local children = {}
            for i = 1, {{total}} do
                children[i] = { kind = if i <= {{matching}} then "BasePart" else "Folder" }
                children[i].IsA = function(self, name) return self.kind == name end
            end
            local world = {}
            function world:GetChildren() return children end

            """;

        using var state = LuauState.Create();
        state.OpenLibraries();
        var returned = state.DoString(prelude + luau + "\nreturn #parts")[0];

        Assert.Equal(matching.ToString(), returned.ToString());
    }
}
