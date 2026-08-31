using Loom.Core.Diagnostics;
using Loom.Core.Pipeline;

namespace Loom.Testing.Modules;

/// <summary>
///     Roblox's own API is realm-restricted at the engine level the same way a <c>[server]</c>/<c>[client]</c>
///     declaration is - <c>Players.LocalPlayer</c> is <c>nil</c> on the server, <c>RunService.RenderStepped</c>
///     never fires there, and <c>DataStoreService</c> is server-only by design. This is the same diagnostic
///     an import crossing <c>[realms]</c> already is, checked where a member access resolves against the
///     generated intrinsic declaration instead of where an import binds a name.
/// </summary>
public partial class SourceRootTest
{
    private const string RealmManifest = "project_type = \"game\"\n[realms]\nclient = \"client\"\nserver = \"server\"\n";

    [Theory]
    [InlineData("server/main.loom", true)]
    [InlineData("client/main.loom", false)]
    [InlineData("shared/main.loom", true)]
    public void AccessingAClientOnlyProperty_FromNonClientCode_IsRejected(string path, bool rejected)
    {
        var directory = Path.Combine(Path.GetTempPath(), "loom-realm-api-" + Guid.NewGuid());
        try
        {
            var config = WriteProject(
                directory,
                RealmManifest,
                [(path, "let players = get_service::<Players>();\nlet me = players.local_player;")]
            );

            config.NoEmit = true;

            var unit = new CompilationUnit(new SourceRootSet(new SourceRoot(config)));
            var diagnostics = unit.Compile().Diagnostics;
            var restrictions = diagnostics.Set.Where(d => d.Code == InternalCodes.RealmRestrictedApiMember).ToList();

            if (!rejected)
            {
                Assert.Empty(restrictions);
                return;
            }

            Assert.Contains(restrictions, d => d.Message.Contains("'local_player' is client-only"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("client/main.loom", true)]
    [InlineData("server/main.loom", false)]
    [InlineData("shared/main.loom", true)]
    public void AccessingAServerOnlyService_FromNonServerCode_IsRejected(string path, bool rejected)
    {
        var directory = Path.Combine(Path.GetTempPath(), "loom-realm-api-" + Guid.NewGuid());
        try
        {
            var config = WriteProject(
                directory,
                RealmManifest,
                [(path, "let store_service = get_service::<DataStoreService>();\nlet store = store_service.get_global_data_store();")]
            );

            config.NoEmit = true;

            var unit = new CompilationUnit(new SourceRootSet(new SourceRoot(config)));
            var diagnostics = unit.Compile().Diagnostics;
            var restrictions = diagnostics.Set.Where(d => d.Code == InternalCodes.RealmRestrictedApiMember).ToList();

            if (!rejected)
            {
                Assert.Empty(restrictions);
                return;
            }

            Assert.Contains(restrictions, d => d.Message.Contains("'get_global_data_store' is server-only"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("render_stepped")]
    [InlineData("pre_render")]
    public void ClientOnlyRunServiceEvents_AreRejectedFromServerCode(string eventName)
    {
        var directory = Path.Combine(Path.GetTempPath(), "loom-realm-api-" + Guid.NewGuid());
        try
        {
            var config = WriteProject(
                directory,
                RealmManifest,
                [
                    (
                        "server/main.loom",
                        $"let run_service = get_service::<RunService>();\nfn handler(dt: number): void {{}}\nrun_service.{eventName} += handler;"
                    )
                ]
            );

            config.NoEmit = true;

            var unit = new CompilationUnit(new SourceRootSet(new SourceRoot(config)));
            var restrictions = unit.Compile().Diagnostics.Set.Where(d => d.Code == InternalCodes.RealmRestrictedApiMember).ToList();

            Assert.Contains(restrictions, d => d.Message.Contains("client-only"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    ///     A <c>RemoteEvent</c>'s client-to-server half (<c>FireServer</c>/<c>OnClientEvent</c>) and its
    ///     server-to-client half (<c>FireClient</c>/<c>FireAllClients</c>/<c>OnServerEvent</c>) each only
    ///     make sense from the realm that is the sender or the recipient - the same restriction applies to
    ///     <c>UnreliableRemoteEvent</c>, which mirrors <c>RemoteEvent</c>'s surface exactly.
    /// </summary>
    [Theory]
    [InlineData("RemoteEvent", "fire_server()", "client")]
    [InlineData("RemoteEvent", "fire_client()", "server")]
    [InlineData("RemoteEvent", "fire_all_clients()", "server")]
    [InlineData("UnreliableRemoteEvent", "fire_server()", "client")]
    [InlineData("UnreliableRemoteEvent", "fire_client()", "server")]
    public void RemoteEventMethods_AreRejectedFromTheOtherRealm(string className, string call, string restrictedTo)
    {
        var rejectingPath = restrictedTo == "client" ? "server/main.loom" : "client/main.loom";
        var directory = Path.Combine(Path.GetTempPath(), "loom-realm-api-" + Guid.NewGuid());
        try
        {
            var config = WriteProject(
                directory,
                RealmManifest,
                [(rejectingPath, $"let remote = new_instance::<{className}>();\nremote.{call};")]
            );

            config.NoEmit = true;

            var unit = new CompilationUnit(new SourceRootSet(new SourceRoot(config)));
            var restrictions = unit.Compile().Diagnostics.Set.Where(d => d.Code == InternalCodes.RealmRestrictedApiMember).ToList();

            Assert.Contains(restrictions, d => d.Message.Contains($"is {restrictedTo}-only"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    ///     A <c>RemoteFunction</c>'s <c>OnServerInvoke</c>/<c>OnClientInvoke</c> callbacks did not exist in
    ///     the generated intrinsics at all before this - every <c>Callback</c>-typed member was invisible to
    ///     <see cref="Loom.TypeGenerator.Generators.ClassGenerator.IsAccessible" />'s predecessor, which
    ///     gated every member kind on read security when a callback's is hardcoded unreadable. Reaching them
    ///     here proves both that they now exist and that they carry the same realm restriction their
    ///     matching <c>Invoke*</c> method does.
    /// </summary>
    [Theory]
    [InlineData("on_server_invoke", "server")]
    [InlineData("on_client_invoke", "client")]
    public void RemoteFunctionCallbacks_AreRejectedFromTheOtherRealm(string callbackName, string restrictedTo)
    {
        var rejectingPath = restrictedTo == "client" ? "server/main.loom" : "client/main.loom";
        var directory = Path.Combine(Path.GetTempPath(), "loom-realm-api-" + Guid.NewGuid());
        try
        {
            var config = WriteProject(
                directory,
                RealmManifest,
                [(rejectingPath, $"let remote = new_instance::<RemoteFunction>();\nlet handler = remote.{callbackName};")]
            );

            config.NoEmit = true;

            var unit = new CompilationUnit(new SourceRootSet(new SourceRoot(config)));
            var restrictions = unit.Compile().Diagnostics.Set.Where(d => d.Code == InternalCodes.RealmRestrictedApiMember).ToList();

            Assert.Contains(restrictions, d => d.Message.Contains($"is {restrictedTo}-only"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    ///     A project that never declared <c>[realms]</c> has one realm and no boundary to cross
    ///     (<see cref="SourceRoot.RealmOf" />) - so unlike an import, which a user chooses to narrow with
    ///     their own <c>[server]</c>/<c>[client]</c> attribute, a Roblox API's built-in restriction must not
    ///     turn into a permanent, unannounced ban on services like <c>DataStoreService</c> for every project
    ///     that has not opted into modelling the split at all.
    /// </summary>
    [Fact]
    public void RobloxApiRealmRestriction_DoesNotApply_WhenTheProjectDeclaresNoRealms()
    {
        var directory = Path.Combine(Path.GetTempPath(), "loom-realm-api-" + Guid.NewGuid());
        try
        {
            var config = WriteProject(
                directory,
                "project_type = \"game\"\n",
                [
                    (
                        "main.loom",
                        "let store_service = get_service::<DataStoreService>();\n"
                        + "let store = store_service.get_global_data_store();\n"
                        + "let players = get_service::<Players>();\n"
                        + "let me = players.local_player;"
                    )
                ]
            );

            config.NoEmit = true;

            var unit = new CompilationUnit(new SourceRootSet(new SourceRoot(config)));
            var restrictions = unit.Compile().Diagnostics.Set.Where(d => d.Code == InternalCodes.RealmRestrictedApiMember).ToList();

            Assert.Empty(restrictions);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
