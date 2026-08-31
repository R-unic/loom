using Loom.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace Loom.Testing.LanguageServer;

[Collection("Assembly")]
public class DocumentLinkHandlerTest
{
    [Fact]
    public async Task Handle_LinksARelativeImportToTheFileItNames() =>
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var result = await new DocumentLinkHandler(store).Handle(
                    new DocumentLinkParams { TextDocument = new TextDocumentIdentifier(uri) },
                    TestContext.Current.CancellationToken
                );

                var link = Assert.Single(result!);
                Assert.EndsWith("math.loom", link.Target!.Path);
            },
            "import { double } from \"./util/math\";\nlet four = double(2);",
            ("util/math.loom", "export fn double(n: number): number { return n * 2; }")
        );

    [Fact]
    public async Task Handle_LinksANamespaceImport() =>
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var result = await new DocumentLinkHandler(store).Handle(
                    new DocumentLinkParams { TextDocument = new TextDocumentIdentifier(uri) },
                    TestContext.Current.CancellationToken
                );

                var link = Assert.Single(result!);
                Assert.EndsWith("math.loom", link.Target!.Path);
            },
            "import * as math from \"./util/math\";\nlet four = math.double(2);",
            ("util/math.loom", "export fn double(n: number): number { return n * 2; }")
        );

    [Fact]
    public async Task Handle_LinksAReExport() =>
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var result = await new DocumentLinkHandler(store).Handle(
                    new DocumentLinkParams { TextDocument = new TextDocumentIdentifier(uri) },
                    TestContext.Current.CancellationToken
                );

                var link = Assert.Single(result!);
                Assert.EndsWith("math.loom", link.Target!.Path);
            },
            "export * from \"./util/math\";",
            ("util/math.loom", "export fn double(n: number): number { return n * 2; }")
        );

    [Fact]
    public async Task Handle_SkipsAStatementThatNamesNoModule() =>
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var result = await new DocumentLinkHandler(store).Handle(
                    new DocumentLinkParams { TextDocument = new TextDocumentIdentifier(uri) },
                    TestContext.Current.CancellationToken
                );

                Assert.Empty(result!);
            },
            "let x = 1;\nprint(x);"
        );

    [Fact]
    public async Task Handle_SkipsAnEmptySpecifier() =>
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var result = await new DocumentLinkHandler(store).Handle(
                    new DocumentLinkParams { TextDocument = new TextDocumentIdentifier(uri) },
                    TestContext.Current.CancellationToken
                );

                Assert.Empty(result!);
            },
            "import { x } from \"\";"
        );

    [Fact]
    public async Task Handle_SkipsASpecifierThatDoesNotResolve() =>
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var result = await new DocumentLinkHandler(store).Handle(
                    new DocumentLinkParams { TextDocument = new TextDocumentIdentifier(uri) },
                    TestContext.Current.CancellationToken
                );

                Assert.Empty(result!);
            },
            "import { double } from \"./util/missing\";"
        );

    [Fact]
    public async Task Handle_ForAnUnknownDocument_ReturnsNothing()
    {
        var handler = new DocumentLinkHandler(new DocumentStore());
        var uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.FromFileSystemPath(Path.Combine(Path.GetTempPath(), "does-not-exist.loom"));

        Assert.Null(
            await handler.Handle(new DocumentLinkParams { TextDocument = new TextDocumentIdentifier(uri) }, TestContext.Current.CancellationToken)
        );
    }

    /// <summary>A resolve request answers with exactly the link it was given: a link is complete the moment it is produced.</summary>
    [Fact]
    public async Task Resolve_ReturnsTheLinkUnchanged()
    {
        var handler = new DocumentLinkHandler(new DocumentStore());
        var link = new DocumentLink { Range = new Range(new Position(0, 0), new Position(0, 1)) };

        Assert.Same(link, await handler.Handle(link, TestContext.Current.CancellationToken));
    }
}
