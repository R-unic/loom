using Loom.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.Testing.LanguageServer;

[Collection("Assembly")]
public class ReferencesAndRenameTest
{
    private const string Source = """
                                  interface Player {
                                    score: number;
                                  }

                                  fn bump(p: Player): void {
                                    p.score = p.score + 1;
                                  }

                                  fn main(p: Player): void {
                                    bump(p);
                                    bump(p);
                                  }
                                  """;

    [Fact]
    public async Task References_FindsEveryUseOfAFunction()
    {
        var locations = await ReferencesAsync(Source, 4, 4);
        Assert.Equal(3, locations.Length);
        Assert.Equal([4, 9, 10], locations.Select(location => location.Range.Start.Line).Order());
    }

    [Fact]
    public async Task References_CanLeaveOutTheDeclaration()
    {
        var locations = await ReferencesAsync(Source, 4, 4, includeDeclaration: false);
        Assert.Equal([9, 10], locations.Select(location => location.Range.Start.Line).Order());
    }

    [Fact]
    public async Task References_FindsAPropertyThroughItsAccesses()
    {
        var locations = await ReferencesAsync(Source, 1, 3);
        Assert.Equal(3, locations.Length);
        Assert.Equal([1, 5, 5], locations.Select(location => location.Range.Start.Line).Order());
    }

    /// <summary>
    ///     The project's own <c>Player</c> shadows the Roblox class of that name. Both are declared in the
    ///     file's table, since intrinsics reach every file, and looking the type's members up on the wrong one
    ///     finds none of them.
    /// </summary>
    [Fact]
    public async Task References_ForATypeNamedAfterARobloxClass_UsesTheProjectsOwn()
    {
        var locations = await ReferencesAsync(
            """
            interface Instance {
              tag: string;
            }

            fn label(i: Instance): string {
              return i.tag;
            }
            """,
            1,
            3
        );

        Assert.Equal(2, locations.Length);
    }

    [Fact]
    public async Task References_ReachesEveryModuleTheSymbolIsUsedIn() =>
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var locations = await new ReferencesHandler(store).Handle(
                    new ReferenceParams
                    {
                        TextDocument = new TextDocumentIdentifier(uri), Position = new Position(1, 11), Context = new ReferenceContext { IncludeDeclaration = true }
                    },
                    TestContext.Current.CancellationToken
                );

                var found = locations!.ToArray();
                Assert.Equal(3, found.Length);
                Assert.Equal(2, found.Select(location => location.Uri).Distinct().Count());
            },
            "import { double } from \"./util/math\";\nlet four = double(2);",
            ("util/math.loom", "export fn double(n: number): number { return n * 2; }")
        );

    /// <remarks>
    ///     A module whose text never spells the name is skipped without being walked, so a module that does
    ///     spell it without referring to it is what decides whether that shortcut is a filter or a search.
    /// </remarks>
    [Fact]
    public async Task References_LeaveOutAModuleThatOnlyNamesTheSymbolInCommentsAndStrings() =>
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var locations = await new ReferencesHandler(store).Handle(
                    new ReferenceParams
                    {
                        TextDocument = new TextDocumentIdentifier(uri), Position = new Position(0, 11), Context = new ReferenceContext { IncludeDeclaration = true }
                    },
                    TestContext.Current.CancellationToken
                );

                var found = locations!.ToArray();
                Assert.Single(found);
                Assert.EndsWith("main.loom", found[0].Uri.Path, StringComparison.Ordinal);
            },
            "export fn double(n: number): number { return n * 2; }",
            ("other.loom", "## double is described but never called\nlet note = \"double\";")
        );

    [Fact]
    public async Task Rename_RewritesTheDeclarationAndEveryUse()
    {
        var edit = await RenameAsync(Source, 4, 4, "increase");
        var edits = Assert.Single(edit!.Changes!).Value.ToArray();

        Assert.Equal(3, edits.Length);
        Assert.All(edits, single => Assert.Equal("increase", single.NewText));
    }

    [Fact]
    public async Task Rename_ReachesTheImportSpecifierInAnotherModule() =>
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var edit = await new RenameHandler(store).Handle(
                    new RenameParams { TextDocument = new TextDocumentIdentifier(uri), Position = new Position(1, 11), NewName = "twice" },
                    TestContext.Current.CancellationToken
                );

                Assert.Equal(2, edit!.Changes!.Count);
                Assert.Equal(4, edit.Changes.Values.Sum(edits => edits.Count()));
            },
            "import { double } from \"./util/math\";\nlet four = double(2);",
            ("util/math.loom", "export fn double(n: number): number { return n * 2; }\nlet eight = double(4);")
        );

    /// <summary>An alias is the importing file's own name for the symbol, so renaming the declaration must leave it alone.</summary>
    [Fact]
    public async Task Rename_LeavesAnImportAliasAndItsUsesAlone() =>
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var edit = await new RenameHandler(store).Handle(
                    new RenameParams { TextDocument = new TextDocumentIdentifier(uri), Position = new Position(0, 11), NewName = "twice" },
                    TestContext.Current.CancellationToken
                );

                var edits = edit!.Changes!.First(change => change.Key.Path.EndsWith("main.loom")).Value.ToArray();
                Assert.Single(edits);
                Assert.Equal(0, edits[0].Range.Start.Line);
            },
            "import { double as d } from \"./util/math\";\nlet four = d(2);",
            ("util/math.loom", "export fn double(n: number): number { return n * 2; }")
        );

    [Fact]
    public async Task Rename_RefusesAnIntrinsic() => Assert.Null(await RenameAsync("fn main(): void {\n  print(1);\n}", 1, 4, "log"));

    [Theory]
    [InlineData("let")]
    [InlineData("number")]
    [InlineData("end")]
    [InlineData("1bad")]
    [InlineData("")]
    public async Task Rename_RefusesANameTheCompilerWouldReject(string newName) => Assert.Null(await RenameAsync(Source, 4, 4, newName));

    [Fact]
    public async Task PrepareRename_OffersTheNameUnderTheCursor()
    {
        var range = await PrepareAsync(Source, 4, 4);
        Assert.Equal(3, range!.Range!.Start.Character);
        Assert.Equal(7, range.Range.End.Character);
    }

    [Fact]
    public async Task PrepareRename_RefusesAnIntrinsic() => Assert.Null(await PrepareAsync("fn main(): void {\n  print(1);\n}", 1, 4));

    [Fact]
    public async Task Highlight_MarksTheDeclarationAsAWriteAndUsesAsReads()
    {
        var highlights = await HighlightsAsync("fn main(): void {\n  mut total = 1;\n  total = total + 1;\n}", 1, 7);

        Assert.Equal(3, highlights.Length);
        Assert.Equal(2, highlights.Count(highlight => highlight.Kind == DocumentHighlightKind.Write));
        Assert.Equal(1, highlights.Count(highlight => highlight.Kind == DocumentHighlightKind.Read));
    }

    [Fact]
    public async Task Highlight_StaysInsideTheOpenDocument() =>
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var highlights = await new DocumentHighlightHandler(store).Handle(
                    new DocumentHighlightParams { TextDocument = new TextDocumentIdentifier(uri), Position = new Position(1, 11) },
                    TestContext.Current.CancellationToken
                );

                Assert.Equal(2, highlights!.Count());
            },
            "import { double } from \"./util/math\";\nlet four = double(2);",
            ("util/math.loom", "export fn double(n: number): number { return n * 2; }")
        );

    private static async Task<Location[]> ReferencesAsync(string source, int line, int character, bool includeDeclaration = true)
    {
        var locations = Array.Empty<Location>();
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var result = await new ReferencesHandler(store).Handle(
                    new ReferenceParams
                    {
                        TextDocument = new TextDocumentIdentifier(uri),
                        Position = new Position(line, character),
                        Context = new ReferenceContext { IncludeDeclaration = includeDeclaration }
                    },
                    TestContext.Current.CancellationToken
                );

                locations = result?.ToArray() ?? [];
            },
            source
        );

        return locations;
    }

    private static async Task<WorkspaceEdit?> RenameAsync(string source, int line, int character, string newName)
    {
        WorkspaceEdit? edit = null;
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
                edit = await new RenameHandler(store).Handle(
                    new RenameParams { TextDocument = new TextDocumentIdentifier(uri), Position = new Position(line, character), NewName = newName },
                    TestContext.Current.CancellationToken
                ),
            source
        );

        return edit;
    }

    private static async Task<RangeOrPlaceholderRange?> PrepareAsync(string source, int line, int character)
    {
        RangeOrPlaceholderRange? range = null;
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
                range = await new PrepareRenameHandler(store).Handle(
                    new PrepareRenameParams { TextDocument = new TextDocumentIdentifier(uri), Position = new Position(line, character) },
                    TestContext.Current.CancellationToken
                ),
            source
        );

        return range;
    }

    private static async Task<DocumentHighlight[]> HighlightsAsync(string source, int line, int character)
    {
        var highlights = Array.Empty<DocumentHighlight>();
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var result = await new DocumentHighlightHandler(store).Handle(
                    new DocumentHighlightParams { TextDocument = new TextDocumentIdentifier(uri), Position = new Position(line, character) },
                    TestContext.Current.CancellationToken
                );

                highlights = result?.ToArray() ?? [];
            },
            source
        );

        return highlights;
    }
}
