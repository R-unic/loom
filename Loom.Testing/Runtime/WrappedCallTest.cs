using Loom.Core.Diagnostics;
using NuLua;
using NuLua.Luau;

namespace Loom.Testing.Runtime;

[Collection("Assembly")]
public class WrappedCallTest
{
    private const string Store = """
        declare interface Store {
            [luau_name("GetAsync"), luau_method, wraps_errors]
            get_async: fn(key: string): Result<string, RobloxError>;
        }

        declare let store: Store;


        """;

    [Fact]
    public void AWrappedCall_LowersToXpcallWithTheSharedHandler()
    {
        var rendered = Utility.GetLuauAST(Store + """let outcome = store.get_async("player_1");""", typeCheck: true).Render();

        Assert.Contains("xpcall(store.GetAsync, Loom.roblox_error, store, \"player_1\")", rendered);
        Assert.Contains("ok = true", rendered);
        Assert.Contains("ok = false", rendered);
        Assert.DoesNotContain("function()", rendered);
    }

    [Fact]
    public void AWrappedCall_ProducesAResultTheCombinatorsAccept() =>
        Utility.AssertNoErrors(
            Utility.GetTypeCheckerDiagnostics(Store + """let value = store.get_async("k").unwrap_or("default");""")
        );

    [Fact]
    public void AWrappedCall_RequiresHandlingRatherThanPanicking() =>
        Utility.AssertDiagnostic(
            Utility.GetTypeCheckerDiagnostics(Store + """let value = store.get_async("k").unwrap();"""),
            InternalCodes.PanicOutsideFallibleFunction,
            "'unwrap' can panic, and this code cannot recover from it."
        );

    [Theory]
    [InlineData("return \"payload\"", "true|payload")]
    [InlineData("error(\"datastore down\")", "false|datastore down")]
    public void AWrappedCall_CatchesAtRuntime(string body, string expected)
    {
        var luau = Utility.GetLuauAST(Store + """let outcome = store.get_async("k");""", typeCheck: true)
            .Render()
            .Replace("const ", "local ");

        var prelude = $$"""
            local Loom = {}
            function Loom.roblox_error(message)
                return { message = tostring(message), traceback = "<traceback>" }
            end
            local store = {}
            function store:GetAsync(key)
                {{body}}
            end

            """;

        const string epilogue = """

            return tostring(outcome.ok) .. "|" .. (if outcome.ok then outcome.value else outcome.error.message)
            """;

        using var state = LuauState.Create();
        state.OpenLibraries();

        var returned = state.DoString(prelude + luau + epilogue)[0].ToString();

        Assert.StartsWith(expected.Split('|')[0], returned);
        Assert.Contains(expected.Split('|')[1], returned);
    }

    [Fact]
    public void AWrappedMemberCannotBeReferencedWithoutCalling() =>
        Utility.AssertDiagnostic(
            Utility.GetTypeCheckerDiagnostics(Store + "let f = store.get_async;"),
            InternalCodes.UncalledWrappedMember,
            "'get_async' can only be called, not referenced."
        );

    [Fact]
    public void CallingAWrappedMemberIsStillAllowed() =>
        Assert.DoesNotContain(
            Utility.GetTypeCheckerDiagnostics(Store + """let r = store.get_async("k");""").Set,
            diagnostic => diagnostic.Code == InternalCodes.UncalledWrappedMember
        );
}
