using NuLua;
using NuLua.Luau;
using Loom.Testing;

namespace Loom.Testing.Runtime;

/// <summary>
///     Executes the guarded form <c>&amp;&amp;</c>, <c>||</c> and <c>??</c> lower to when their right
///     operand hoists statements. The generator tests prove those statements moved under the guard; only
///     running them proves they stopped executing - which is the whole point, since a right operand with
///     side effects otherwise observes the wrong behaviour.
/// </summary>
/// <remarks>Loom spells an immutable binding <c>const</c>, which Luau has no keyword for.</remarks>
[Collection("Assembly")]
public class ShortCircuitRuntimeTest
{
    /// <summary>
    ///     A Workspace stub counting its own GetChildren calls, so an operand that ran is observable. Its
    ///     children answer <c>IsA</c> for anything, so the filter macro keeps whatever it is handed.
    /// </summary>
    private const string Stub = """
        local calls = 0
        local child = {}
        function child:IsA(name) return true end
        local world = {}
        function world:GetChildren()
            calls = calls + 1
            return CHILDREN
        end

        """;

    [Theory]
    [InlineData("let ok = world.get_children::<BasePart>().length > 0 && world.get_children::<Folder>().length > 0;", "{}", 1)]
    [InlineData("let ok = world.get_children::<BasePart>().length > 0 || world.get_children::<Folder>().length > 0;", "{ child }", 1)]
    [InlineData("let a: number? = 1; let b = a ?? world.get_children::<BasePart>().length;", "{}", 0)]
    [InlineData("let a: number? = none; let b = a ?? world.get_children::<BasePart>().length;", "{}", 1)]
    public void TheSkippedOperandNeverRuns(string source, string children, int expectedCalls)
    {
        var luau = Utility.GenerateAgainstWorkspace(source).Replace("const ", "local ");

        using var state = LuauState.Create();
        state.OpenLibraries();
        var returned = state.DoString(Stub.Replace("CHILDREN", children) + luau + "\nreturn calls")[0];

        Assert.Equal(expectedCalls.ToString(), returned.ToString());
    }
}
