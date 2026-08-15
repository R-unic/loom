using Loom.LanguageServer;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.Testing;

/// <summary>
///     Who calls a function, and who it calls, exercised as a client actually would: prepare at a position to
///     get an item, then hand that item's own <c>Data</c> straight back into the next request rather than
///     assuming anything about its shape. Everything happens against one open project, since the item carries
///     the path it was prepared against and that path only means anything for as long as the project does.
/// </summary>
[Collection("Assembly")]
public class CallHierarchyTest
{
    private const string Source = """
        fn helper(n: number): number -> n * 2;

        fn middle(n: number): number -> helper(n) + 1;

        fn main(): void {
            print(middle(1));
            print(helper(2));
        }

        trait Greeter {
            fn greet(): string;
        }

        interface English { }

        implement Greeter for English {
            fn greet(): string {
                helper(1);
                return "hi";
            }
        }

        let unused = helper(9);
        """;

    [Fact]
    public async Task Prepare_OffersTheFunctionUnderTheCursor() =>
        await WithHandlersAsync(
            async (prepare, _, _, uri) =>
            {
                var item = Assert.Single((await PrepareAsync(prepare, uri, line: 0, character: 3))!);
                Assert.Equal("helper", item.Name);
            }
        );

    [Fact]
    public async Task Prepare_OffersNothingForANonFunctionSymbol() =>
        await WithHandlersAsync(async (prepare, _, _, uri) => Assert.Null(await PrepareAsync(prepare, uri, line: 0, character: 12)));

    [Fact]
    public async Task IncomingCalls_FindsEveryNamedFunctionThatCallsIt() =>
        await WithHandlersAsync(
            async (prepare, incoming, _, uri) =>
            {
                var item = Assert.Single((await PrepareAsync(prepare, uri, line: 0, character: 3))!);
                var calls = (await incoming.Handle(new CallHierarchyIncomingCallsParams { Item = item }, Cancel))!.ToArray();

                Assert.Equal(["greet", "main", "middle"], calls.Select(call => call.From.Name).Order());
            }
        );

    /// <remarks>
    ///     The fixture's trailing <c>let unused = helper(9);</c> is a reference to <c>helper</c> at the top
    ///     level of the module - no named function encloses it, so it contributes no edge rather than one
    ///     under an empty or null name.
    /// </remarks>
    [Fact]
    public async Task IncomingCalls_ContributesNoEdgeForAReferenceAtTheTopLevelOfTheModule() =>
        await WithHandlersAsync(
            async (prepare, incoming, _, uri) =>
            {
                var item = Assert.Single((await PrepareAsync(prepare, uri, line: 0, character: 3))!); // helper
                var calls = (await incoming.Handle(new CallHierarchyIncomingCallsParams { Item = item }, Cancel))!.ToArray();

                Assert.Equal(3, calls.Length);
                Assert.All(calls, call => Assert.False(string.IsNullOrEmpty(call.From.Name)));
            }
        );

    [Fact]
    public async Task IncomingCalls_ReportsEveryCallSiteInsideOneCaller() =>
        await WithHandlersAsync(
            async (prepare, incoming, _, uri) =>
            {
                var item = Assert.Single((await PrepareAsync(prepare, uri, line: 2, character: 3))!); // middle
                var calls = await incoming.Handle(new CallHierarchyIncomingCallsParams { Item = item }, Cancel);

                var fromMain = Assert.Single(calls!, call => call.From.Name == "main");
                Assert.Single(fromMain.FromRanges);
            }
        );

    [Fact]
    public async Task OutgoingCalls_FindsEveryNamedFunctionItCalls() =>
        await WithHandlersAsync(
            async (prepare, _, outgoing, uri) =>
            {
                var item = Assert.Single((await PrepareAsync(prepare, uri, line: 4, character: 3))!); // main
                var calls = (await outgoing.Handle(new CallHierarchyOutgoingCallsParams { Item = item }, Cancel))!.ToArray();

                // 'main' calls 'middle' and 'helper' directly, and 'print' twice - an intrinsic is a
                // FunctionSymbol like any other, so it belongs in the same list
                Assert.Equal(["helper", "middle", "print"], calls.Select(call => call.To.Name).Order());
            }
        );

    [Fact]
    public async Task OutgoingCalls_ForAFunctionThatCallsNothing_AreEmpty() =>
        await WithHandlersAsync(
            async (prepare, _, outgoing, uri) =>
            {
                var item = Assert.Single((await PrepareAsync(prepare, uri, line: 0, character: 3))!); // helper
                Assert.Empty((await outgoing.Handle(new CallHierarchyOutgoingCallsParams { Item = item }, Cancel))!);
            }
        );

    /// <remarks>A trait method's body is written where its declaration never names it, so the hierarchy has to find it by resolved symbol rather than by syntax.</remarks>
    [Fact]
    public async Task IncomingCalls_ReachAnImplementationsBody() =>
        await WithHandlersAsync(
            async (prepare, incoming, _, uri) =>
            {
                var item = Assert.Single((await PrepareAsync(prepare, uri, line: 0, character: 3))!); // helper
                var calls = (await incoming.Handle(new CallHierarchyIncomingCallsParams { Item = item }, Cancel))!.ToArray();

                Assert.Contains(calls, call => call.From.Name == "greet");
            }
        );

    /// <remarks>
    ///     A tree view keeps a prepared item's Data around while the file goes on being edited, so a follow-up
    ///     request has to notice when the offset it names has stopped naming the thing the item claims to be -
    ///     here simulated directly, since reproducing it through a real edit would depend on exactly where the
    ///     incremental compile happens to land the same-named node.
    /// </remarks>
    [Fact]
    public async Task IncomingCalls_RefusesAnItemWhoseOffsetNoLongerNamesIt() =>
        await WithHandlersAsync(
            async (prepare, incoming, _, uri) =>
            {
                var real = Assert.Single((await PrepareAsync(prepare, uri, line: 2, character: 3))!); // middle
                var stale = real with { Data = new JObject { ["loomUri"] = uri.ToString(), ["loomOffset"] = real.Data!["loomOffset"], ["loomName"] = "helper" } };

                Assert.Null(await incoming.Handle(new CallHierarchyIncomingCallsParams { Item = stale }, Cancel));
            }
        );

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static Task<Container<CallHierarchyItem>?> PrepareAsync(CallHierarchyPrepareHandler handler, DocumentUri uri, int line, int character) =>
        handler.Handle(new CallHierarchyPrepareParams { TextDocument = new TextDocumentIdentifier(uri), Position = new Position(line, character) }, Cancel);

    private static async Task WithHandlersAsync(
        Func<CallHierarchyPrepareHandler, CallHierarchyIncomingCallsHandler, CallHierarchyOutgoingCallsHandler, DocumentUri, Task> act) =>
        await Utility.WithLspProjectAsync(
            (store, uri) => act(new CallHierarchyPrepareHandler(store), new CallHierarchyIncomingCallsHandler(store), new CallHierarchyOutgoingCallsHandler(store), uri),
            Source
        );
}
