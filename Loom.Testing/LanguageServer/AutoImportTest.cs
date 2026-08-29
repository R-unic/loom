using Loom.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.Testing;

[Collection("Assembly")]
public class AutoImportTest
{
    private static readonly (string Path, string Source) Math =
        ("util/math.loom", "export fn double(n: number): number { return n * 2; }\nexport interface Vec { x: number; }");

    [Fact]
    public async Task Completion_OffersANameFromAModuleTheFileHasNotImported() =>
        await WithMathAsync(
            "let four = dou",
            async (handler, uri, _) =>
            {
                var items = await CompleteAsync(handler, uri, 0, 14);
                var item = Assert.Single(items, entry => entry.Label == "double");

                var resolved = await handler.Handle(item, TestContext.Current.CancellationToken);
                Assert.Equal("./util/math", resolved.LabelDetails?.Description);
            }
        );

    [Fact]
    public async Task Completion_SortsAnImportableNameBelowEverythingInScope() =>
        await WithMathAsync(
            "let four = dou",
            async (handler, uri, _) =>
            {
                var item = Assert.Single(await CompleteAsync(handler, uri, 0, 14), entry => entry.Label == "double");
                Assert.StartsWith("2", item.SortText);
            }
        );

    [Fact]
    public async Task Completion_AcceptingAnImportableNameWritesTheImport() =>
        await WithMathAsync(
            "let four = dou",
            async (handler, uri, _) =>
            {
                var item = Assert.Single(await CompleteAsync(handler, uri, 0, 14), entry => entry.Label == "double");
                var resolved = await handler.Handle(item, TestContext.Current.CancellationToken);

                var edit = Assert.Single(resolved.AdditionalTextEdits!);
                Assert.Equal("import { double } from \"./util/math\";\n", edit.NewText);
                Assert.Equal(0, edit.Range.Start.Line);
                Assert.Equal(0, edit.Range.Start.Character);
            }
        );

    [Fact]
    public async Task Completion_AddsToAnExistingImportOfTheSameModule() =>
        await WithMathAsync(
            "import { Vec } from \"./util/math\";\nlet four = dou",
            async (handler, uri, _) =>
            {
                var item = Assert.Single(await CompleteAsync(handler, uri, 1, 14), entry => entry.Label == "double");
                var resolved = await handler.Handle(item, TestContext.Current.CancellationToken);

                var edit = Assert.Single(resolved.AdditionalTextEdits!);
                Assert.Equal(", double", edit.NewText);
                Assert.Equal(0, edit.Range.Start.Line);
                Assert.Equal(12, edit.Range.Start.Character);
            }
        );

    [Fact]
    public async Task Completion_WritesANewImportBelowTheOnesTheFileOpensWith() =>
        await WithMathAsync(
            "import { Vec } from \"./util/other\";\nlet four = dou",
            async (handler, uri, _) =>
            {
                var item = Assert.Single(await CompleteAsync(handler, uri, 1, 14), entry => entry.Label == "double");
                var resolved = await handler.Handle(item, TestContext.Current.CancellationToken);

                Assert.Equal(1, Assert.Single(resolved.AdditionalTextEdits!).Range.Start.Line);
            },
            ("util/other.loom", "export interface Vec { y: number; }")
        );

    [Fact]
    public async Task Completion_LeavesOutANameTheFileAlreadyImported() =>
        await WithMathAsync(
            "import { double } from \"./util/math\";\nlet four = dou",
            async (handler, uri, _) =>
            {
                var item = Assert.Single(await CompleteAsync(handler, uri, 1, 14), entry => entry.Label == "double");
                var resolved = await handler.Handle(item, TestContext.Current.CancellationToken);

                Assert.Null(resolved.AdditionalTextEdits);
                Assert.Null(resolved.LabelDetails?.Description);
            }
        );

    [Fact]
    public async Task Completion_OffersAnImportableTypeOnlyInATypePosition() =>
        await WithMathAsync(
            "fn main(): void {\n  let a: Ve\n}",
            async (handler, uri, _) =>
            {
                Assert.Single(await CompleteAsync(handler, uri, 1, 11), entry => entry.Label == "Vec");
                Assert.DoesNotContain(await CompleteAsync(handler, uri, 1, 11), entry => entry.Label == "double");
            }
        );

    private static async Task<CompletionItem[]> CompleteAsync(CompletionHandler handler, OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri uri, int line, int character)
    {
        var result = await handler.Handle(
            new CompletionParams { TextDocument = new TextDocumentIdentifier(uri), Position = new Position(line, character) },
            TestContext.Current.CancellationToken
        );

        return [.. result];
    }

    private static Task WithMathAsync(
        string source,
        Func<CompletionHandler, OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri, DocumentStore, Task> act,
        params (string Path, string Source)[] extraFiles) =>
        Utility.WithLspProjectAsync(
            (store, uri) => act(new CompletionHandler(store), uri, store),
            source,
            [Math, ..extraFiles]
        );
}
