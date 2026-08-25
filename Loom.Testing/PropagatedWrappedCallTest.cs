using NuLua;
using NuLua.Luau;

namespace Loom.Testing;

/// <summary>
///     A wrapped call whose Result is propagated immediately is fused, so the success path builds no
///     Result at all. These execute both arms to prove the fusion did not change behaviour.
/// </summary>
[Collection("Assembly")]
public class PropagatedWrappedCallTest
{
    private const string Source = """
        declare interface Store {
            [luau_name("GetAsync"), luau_method, wraps_errors]
            get_async: fn(key: string): Result<string, RobloxError>;
        }

        declare let store: Store;

        fn load(key: string): Result<string, RobloxError> {
            let value = store.get_async(key)?;
            return Result::ok(value);
        }

        let outcome = load("k");
        """;

    [Fact]
    public void TheSuccessPathBuildsNoIntermediateResult()
    {
        var rendered = Utility.GetLuauAST(Source, typeCheck: true).Render();

        Assert.Contains("if not _ok then", rendered);
        Assert.Contains("return { ok = false, error = _value }", rendered);
        Assert.DoesNotContain("_result", rendered);
        Assert.Equal(1, Occurrences(rendered, "ok = true"));
    }

    [Theory]
    [InlineData("return \"payload\"", "true|payload")]
    [InlineData("error(\"datastore down\")", "false|datastore down")]
    public void BothArmsStillBehave(string body, string expected)
    {
        var luau = Utility.GetLuauAST(Source, typeCheck: true).Render().Replace("const ", "local ");
        var prelude = $$"""
            local Loom = {}
            function Loom.roblox_error(message)
                return { message = tostring(message) }
            end
            local store = {}
            function store:GetAsync(key)
                {{body}}
            end

            """;

        using var state = LuauState.Create();
        state.OpenLibraries();
        var returned = state.DoString(
            prelude + luau + "\nreturn tostring(outcome.ok) .. \"|\" .. (if outcome.ok then outcome.value else outcome.error.message)"
        )[0].ToString();

        Assert.StartsWith(expected.Split('|')[0], returned);
        Assert.Contains(expected.Split('|')[1], returned);
    }

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            count++;

        return count;
    }
}
