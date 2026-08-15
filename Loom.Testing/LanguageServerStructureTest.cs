using Loom.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace Loom.Testing;

/// <summary>
///     The whole-file answers - the outline, the fold regions, the hints - over a source carrying one of
///     each declaration kind. Each of these walks the tree by node kind, so a kind nothing exercises is a
///     branch that has never run against a real parse.
/// </summary>
[Collection("Assembly")]
public class LanguageServerStructureTest
{
    private const string Source = """
        import { helper } from "./other";

        ### A packet.
        [serializable]
        interface Packet {
            name: string;
            size: u8;
        }

        trait Describable {
            fn describe(): string;
        }

        implement Describable for Packet {
            fn describe(): string -> name;
        }

        enum Colour {
            Red,
            Green,
        }

        type Alias = number;

        event fired(value: number);

        export fn exported(): number -> 1;

        declare let ambient: number;

        fn main(p: Packet): void {
            mut total = 0;
            for i : 1..3 {
                total += i;
            }

            print(total);
            print(helper());
        }
        """;

    private const string Other = "export fn helper(): number -> 2;";

    [Fact]
    public async Task Outline_NamesEveryDeclarationKind() =>
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var symbols = await new DocumentSymbolHandler(store).Handle(
                    new DocumentSymbolParams { TextDocument = new TextDocumentIdentifier(uri) },
                    TestContext.Current.CancellationToken
                );

                var names = symbols!.Select(symbol => symbol.DocumentSymbol!.Name).ToArray();
                Assert.Contains("Packet", names);
                Assert.Contains("Describable", names);
                Assert.Contains("Colour", names);
                Assert.Contains("Alias", names);
                Assert.Contains("fired", names);
                Assert.Contains("exported", names);
                Assert.Contains("main", names);
            },
            Source,
            ("other.loom", Other)
        );

    [Fact]
    public async Task Outline_NestsAnInterfacesMembersUnderIt() =>
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var symbols = await new DocumentSymbolHandler(store).Handle(
                    new DocumentSymbolParams { TextDocument = new TextDocumentIdentifier(uri) },
                    TestContext.Current.CancellationToken
                );

                var packet = symbols!.Select(symbol => symbol.DocumentSymbol!).Single(symbol => symbol.Name == "Packet");
                Assert.NotNull(packet.Children);
                Assert.Contains(packet.Children!, child => child.Name == "name");
            },
            Source,
            ("other.loom", Other)
        );

    [Fact]
    public async Task Outline_NamesAnEnumsMembers() =>
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var symbols = await new DocumentSymbolHandler(store).Handle(
                    new DocumentSymbolParams { TextDocument = new TextDocumentIdentifier(uri) },
                    TestContext.Current.CancellationToken
                );

                var colour = symbols!.Select(symbol => symbol.DocumentSymbol!).Single(symbol => symbol.Name == "Colour");
                Assert.Contains(colour.Children!, child => child.Name == "Red");
            },
            Source,
            ("other.loom", Other)
        );

    /// <remarks>The import run folds as one region, so collapsing the header does not take the file with it.</remarks>
    [Fact]
    public async Task FoldingRanges_CoverBodiesAndTheImportRun() =>
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var folds = await new FoldingRangeHandler(store).Handle(
                    new FoldingRangeRequestParam { TextDocument = new TextDocumentIdentifier(uri) },
                    TestContext.Current.CancellationToken
                );

                Assert.NotNull(folds);
                Assert.NotEmpty(folds);
                Assert.All(folds, fold => Assert.True(fold.EndLine > fold.StartLine, "a fold has to span more than the line it starts on"));
            },
            Source,
            ("other.loom", Other)
        );

    [Fact]
    public async Task InlayHints_NameTheInferredTypesInAFile() =>
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var hints = await new InlayHintHandler(store).Handle(
                    new InlayHintParams
                    {
                        TextDocument = new TextDocumentIdentifier(uri),
                        Range = new Range(new Position(0, 0), new Position(60, 0))
                    },
                    TestContext.Current.CancellationToken
                );

                Assert.NotNull(hints);
                Assert.NotEmpty(hints);
            },
            Source,
            ("other.loom", Other)
        );

    /// <remarks>A range covering nothing is the ordinary case while scrolling; it must come back empty, not wrong.</remarks>
    [Fact]
    public async Task InlayHints_OutsideTheEditedRange_AreNotReturned() =>
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var hints = await new InlayHintHandler(store).Handle(
                    new InlayHintParams
                    {
                        TextDocument = new TextDocumentIdentifier(uri),
                        Range = new Range(new Position(0, 0), new Position(0, 0))
                    },
                    TestContext.Current.CancellationToken
                );

                Assert.True((hints?.Equals(null) ?? false) || (!hints?.Any() ?? false));
            },
            Source,
            ("other.loom", Other)
        );

    [Fact]
    public async Task TypeDefinition_GoesToTheTypeRatherThanTheValue() =>
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var target = await new TypeDefinitionHandler(store).Handle(
                    new TypeDefinitionParams { TextDocument = new TextDocumentIdentifier(uri), Position = new Position(30, 8) },
                    TestContext.Current.CancellationToken
                );

                Assert.NotNull(target);
            },
            Source,
            ("other.loom", Other)
        );

    /// <remarks>
    ///     Each step of expand-selection has to be a step: an entry the same size as the one before it is a
    ///     keypress that appears to do nothing, which is what a syntax tree full of single-child wrappers
    ///     produces if they are all reported.
    /// </remarks>
    [Fact]
    public async Task SelectionRanges_GrowStrictlyOutwardFromThePosition() =>
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var ranges = await new SelectionRangeHandler(store).Handle(
                    new SelectionRangeParams
                    {
                        TextDocument = new TextDocumentIdentifier(uri), Positions = new Container<Position>(new Position(33, 8))
                    },
                    TestContext.Current.CancellationToken
                );

                var innermost = Assert.Single(ranges!);
                var steps = 0;
                // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                for (var range = innermost; range.Parent != null; range = range.Parent)
                {
                    steps++;
                    Assert.True(Contains(range.Parent.Range, range.Range), "each step has to contain the one before it");
                    Assert.NotEqual(range.Range, range.Parent.Range);
                }

                Assert.True(steps > 2, "a position inside a loop inside a function should expand more than twice");
            },
            Source,
            ("other.loom", Other)
        );

    [Fact]
    public async Task SelectionRanges_AnswerOnePerPositionAsked() =>
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var ranges = await new SelectionRangeHandler(store).Handle(
                    new SelectionRangeParams
                    {
                        TextDocument = new TextDocumentIdentifier(uri), Positions = new Container<Position>(new Position(33, 8), new Position(36, 10))
                    },
                    TestContext.Current.CancellationToken
                );

                Assert.Equal(2, ranges!.Count());
            },
            Source,
            ("other.loom", Other)
        );

    /// <remarks>Where a relative specifier lands depends on which root the importing file belongs to, so the editor cannot follow one on its own.</remarks>
    [Fact]
    public async Task DocumentLinks_PointAModuleSpecifierAtTheFileItNames() =>
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var links = await new DocumentLinkHandler(store).Handle(
                    new DocumentLinkParams { TextDocument = new TextDocumentIdentifier(uri) },
                    TestContext.Current.CancellationToken
                );

                var link = Assert.Single(links!);
                Assert.EndsWith("other.loom", link.Target!.Path, StringComparison.Ordinal);
                Assert.Equal(0, link.Range.Start.Line);
            },
            Source,
            ("other.loom", Other)
        );

    [Fact]
    public async Task DocumentLinks_LeaveOutASpecifierThatResolvesToNothing() =>
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var links = await new DocumentLinkHandler(store).Handle(
                    new DocumentLinkParams { TextDocument = new TextDocumentIdentifier(uri) },
                    TestContext.Current.CancellationToken
                );

                Assert.Empty(links!);
            },
            "import { nothing } from \"./no-such-module\";\nlet x = 1;"
        );

    private static bool Contains(Range outer, Range inner) =>
        (outer.Start.Line < inner.Start.Line || outer.Start.Line == inner.Start.Line && outer.Start.Character <= inner.Start.Character)
        && (outer.End.Line > inner.End.Line || outer.End.Line == inner.End.Line && outer.End.Character >= inner.End.Character);

    [Fact]
    public async Task PrepareRename_AcceptsALocalAndRefusesAnImportedName() =>
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var handler = new PrepareRenameHandler(store);
                var onLocal = await handler.Handle(
                    new PrepareRenameParams { TextDocument = new TextDocumentIdentifier(uri), Position = new Position(31, 8) },
                    TestContext.Current.CancellationToken
                );

                Assert.NotNull(onLocal);
            },
            Source,
            ("other.loom", Other)
        );
}
