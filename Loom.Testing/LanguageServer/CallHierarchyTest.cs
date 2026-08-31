using Loom.Core.Resolving.Symbols;
using Loom.LanguageServer;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.Testing.LanguageServer;

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

    [Fact]
    public async Task IncomingCalls_RefusesAnItemWithMalformedData() =>
        await WithHandlersAsync(
            async (_, incoming, _, uri) =>
            {
                var malformed = new CallHierarchyItem
                {
                    Name = "helper",
                    Kind = OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind.Function,
                    Uri = uri,
                    Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(new Position(0, 0), new Position(0, 1)),
                    SelectionRange = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(new Position(0, 0), new Position(0, 1)),
                    Data = new JObject { ["loomUri"] = uri.ToString() }
                };

                Assert.Null(await incoming.Handle(new CallHierarchyIncomingCallsParams { Item = malformed }, Cancel));
            }
        );

    [Fact]
    public async Task IncomingCalls_RefusesAnItemWhoseUriIsNotOpen() =>
        await WithHandlersAsync(
            async (prepare, incoming, _, uri) =>
            {
                var real = Assert.Single((await PrepareAsync(prepare, uri, line: 0, character: 3))!); // helper
                var closedUri = DocumentUri.FromFileSystemPath(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid() + ".loom"));
                var stale = real with { Data = new JObject { ["loomUri"] = closedUri.ToString(), ["loomOffset"] = real.Data!["loomOffset"], ["loomName"] = "helper" } };

                Assert.Null(await incoming.Handle(new CallHierarchyIncomingCallsParams { Item = stale }, Cancel));
            }
        );

    /// <remarks>
    ///     A call reached through a plain identifier receiver is a QualifiedName; through anything else - a
    ///     call result, an element access - it is a PropertyAccess. Both name the same way.
    /// </remarks>
    [Fact]
    public async Task OutgoingCalls_NamesCalleesReachedThroughAMemberAccess() =>
        await Utility.WithLspProjectAsync(
            (store, uri) =>
            {
                Assert.True(store.TryGetState(uri, out var state));
                var symbol = CallHierarchy.At(state.File, MemberSource.IndexOf("use1", StringComparison.Ordinal) + 3)!;

                var calls = CallHierarchy.OutgoingCalls(symbol, state.Unit).ToArray();
                Assert.Empty(calls);
                return Task.CompletedTask;
            },
            MemberSource
        );

    /// <remarks>A receiver reached through an element access - not a plain identifier - makes the member a PropertyAccess rather than a QualifiedName.</remarks>
    [Fact]
    public async Task OutgoingCalls_NamesCalleesReachedThroughAnElementAccessResult() =>
        await Utility.WithLspProjectAsync(
            (store, uri) =>
            {
                Assert.True(store.TryGetState(uri, out var state));
                var symbol = CallHierarchy.At(state.File, MemberSource.IndexOf("use2", StringComparison.Ordinal) + 3)!;

                Assert.Empty(CallHierarchy.OutgoingCalls(symbol, state.Unit));
                return Task.CompletedTask;
            },
            MemberSource
        );

    private const string MemberSource = """
        interface Calculator {
          add: fn(n: number): number;
        }

        fn use1(calc: Calculator): number {
          return calc.add(1);
        }

        interface Box {
          get: fn(): number;
        }

        fn use2(boxes: Box[]): number {
          return boxes[0].get();
        }

        fn use3(): number {
          return (fn(): number -> 1)();
        }
        """;

    /// <summary>Every named call site is still walked even when the callee expression is neither a name nor a member access.</summary>
    [Fact]
    public async Task OutgoingCalls_IgnoresACallWhoseCalleeIsNeitherANameNorAMemberAccess() =>
        await Utility.WithLspProjectAsync(
            (store, uri) =>
            {
                Assert.True(store.TryGetState(uri, out var state));
                var symbol = CallHierarchy.At(state.File, MemberSource.IndexOf("use3", StringComparison.Ordinal) + 3)!;

                Assert.Empty(CallHierarchy.OutgoingCalls(symbol, state.Unit));
                return Task.CompletedTask;
            },
            MemberSource
        );

    /// <remarks>A symbol looked up against a different project's unit has nothing to walk rather than crashing.</remarks>
    [Fact]
    public async Task OutgoingCalls_ForASymbolFromAnotherCompilationUnit_AreEmpty()
    {
        FunctionSymbol? symbol = null;
        await Utility.WithLspProjectAsync(
            (store, uri) =>
            {
                Assert.True(store.TryGetState(uri, out var state));
                symbol = CallHierarchy.At(state.File, 3);
                return Task.CompletedTask;
            },
            "fn helper(): void { }"
        );

        Assert.NotNull(symbol);

        await Utility.WithLspProjectAsync(
            (store, uri) =>
            {
                Assert.True(store.TryGetState(uri, out var state));
                Assert.Empty(CallHierarchy.OutgoingCalls(symbol!, state.Unit));
                return Task.CompletedTask;
            },
            "fn other(): void { }"
        );
    }

    /// <remarks>A reference inside an anonymous function contributes no edge: an anonymous function has no name a caller could be attributed to.</remarks>
    [Fact]
    public async Task IncomingCalls_ContributesNoEdgeForAReferenceInsideAnAnonymousFunction() =>
        await Utility.WithLspProjectAsync(
            (store, uri) =>
            {
                Assert.True(store.TryGetState(uri, out var state));
                var target = CallHierarchy.At(state.File, 3)!;

                var calls = CallHierarchy.IncomingCalls(target, state.Unit, Cancel);
                Assert.Empty(calls);
                return Task.CompletedTask;
            },
            "fn target(n: number): number -> n * 2;\nlet wrapper = fn(): number -> target(5);"
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
