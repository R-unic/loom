using Loom.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.Testing;

[Collection("Assembly")]
public class CompletionHandlerTest
{
    [Fact]
    public async Task Handle_CompletesTheDocumentsOwnDeclarationsAndAmbientNames()
    {
        var directory = Path.Combine(Path.GetTempPath(), "loom-lsp-test-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(directory, "src"));
        try
        {
            File.WriteAllText(Path.Combine(directory, "loom-config.toml"), "[files]\nsource_directory = \"src\"\noutput_directory = \"dist\"\n");
            var path = Path.Combine(directory, "src", "main.loom");
            File.WriteAllText(path, "let x = 1;");

            var store = new DocumentStore();
            var uri = DocumentUri.FromFileSystemPath(path);
            store.Open(uri, "fn greet(name: string): string { return name; }");

            var handler = new CompletionHandler(store);
            var completions = await handler.Handle(
                new CompletionParams { TextDocument = new TextDocumentIdentifier(uri), Position = new Position(0, 0) },
                TestContext.Current.CancellationToken
            );

            var greet = Assert.Single(completions, item => item.Label == "greet");
            Assert.Equal(CompletionItemKind.Function, greet.Kind);
            Assert.Equal(" fn(string): string", greet.LabelDetails?.Detail);
            Assert.Contains(completions, item => item.Label == "print");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Handle_ForAnUnknownDocument_ReturnsNoCompletions()
    {
        var handler = new CompletionHandler(new DocumentStore());
        var uri = DocumentUri.FromFileSystemPath(Path.Combine(Path.GetTempPath(), "does-not-exist.loom"));

        var completions = await handler.Handle(
            new CompletionParams { TextDocument = new TextDocumentIdentifier(uri), Position = new Position(0, 0) },
            TestContext.Current.CancellationToken
        );

        Assert.Empty(completions);
    }
}
