using Loom.Core.Pipeline;
using Loom.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.Testing;

/// <summary>
///     The document lifecycle - open, change, close - and the reload the client's file watcher drives. These
///     handlers own no answers of their own; what they own is when a compile happens and what the client is
///     told afterwards, so each case drives the handler and reads what reached the publisher.
/// </summary>
[Collection("Assembly")]
public class LanguageServerLifecycleTest
{
    private static readonly TimeSpan _noDelay = TimeSpan.Zero;

    [Fact]
    public async Task Open_PublishesDiagnosticsImmediately() =>
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var publisher = DiagnosticPublisherTest.Recording(out var sent);
                using var debouncer = new Debouncer(_noDelay);
                var handler = new TextDocumentSyncHandler(store, publisher, debouncer);

                await handler.Handle(
                    new DidOpenTextDocumentParams { TextDocument = new TextDocumentItem { Uri = uri, Text = "let x: number = \"no\";" } },
                    TestContext.Current.CancellationToken
                );

                var published = Assert.Single(sent, message => message.Uri == uri);
                Assert.NotEmpty(published.Diagnostics);
            },
            "let x: number = \"no\";"
        );

    [Fact]
    public async Task Change_PublishesOnceTypingStops() =>
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var publisher = DiagnosticPublisherTest.Recording(out var sent);
                using var debouncer = new Debouncer(_noDelay);
                var handler = new TextDocumentSyncHandler(store, publisher, debouncer);

                await handler.Handle(
                    new DidChangeTextDocumentParams
                    {
                        TextDocument = new OptionalVersionedTextDocumentIdentifier { Uri = uri, Version = 2 },
                        ContentChanges = new Container<TextDocumentContentChangeEvent>(new TextDocumentContentChangeEvent { Text = "let y: number = \"no\";" })
                    },
                    TestContext.Current.CancellationToken
                );

                await WaitForAsync(() => sent.Count > 0);
                Assert.Contains(sent, message => message.Uri == uri && message.Diagnostics.Any());
            },
            "let x = 1;"
        );

    [Fact]
    public async Task Change_ToAnUnopenedDocument_IsIgnored() =>
        await Utility.WithLspProjectAsync(
            async (store, _) =>
            {
                var publisher = DiagnosticPublisherTest.Recording(out var sent);
                using var debouncer = new Debouncer(_noDelay);
                var handler = new TextDocumentSyncHandler(store, publisher, debouncer);
                var unopened = DocumentUri.FromFileSystemPath(Path.Combine(Path.GetTempPath(), "never-opened.loom"));

                await handler.Handle(
                    new DidChangeTextDocumentParams
                    {
                        TextDocument = new OptionalVersionedTextDocumentIdentifier { Uri = unopened, Version = 1 },
                        ContentChanges = new Container<TextDocumentContentChangeEvent>(new TextDocumentContentChangeEvent { Text = "let x = 1;" })
                    },
                    TestContext.Current.CancellationToken
                );

                await Task.Delay(50, TestContext.Current.CancellationToken);
                Assert.Empty(sent);
            },
            "let x = 1;"
        );

    /// <remarks>
    ///     A client keeps whatever it was last sent, so a closed file's errors would sit in the Problems panel
    ///     against a document nothing is analyzing any more.
    /// </remarks>
    [Fact]
    public async Task Close_ClearsTheFilesDiagnostics() =>
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var publisher = DiagnosticPublisherTest.Recording(out var sent);
                using var debouncer = new Debouncer(_noDelay);
                var handler = new TextDocumentSyncHandler(store, publisher, debouncer);

                await handler.Handle(
                    new DidCloseTextDocumentParams { TextDocument = new TextDocumentIdentifier(uri) },
                    TestContext.Current.CancellationToken
                );

                var published = Assert.Single(sent, message => message.Uri == uri);
                Assert.Empty(published.Diagnostics);
            },
            "let x: number = \"no\";"
        );

    [Fact]
    public async Task Save_PublishesNothing() =>
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var publisher = DiagnosticPublisherTest.Recording(out var sent);
                using var debouncer = new Debouncer(_noDelay);
                var handler = new TextDocumentSyncHandler(store, publisher, debouncer);

                await handler.Handle(
                    new DidSaveTextDocumentParams { TextDocument = new TextDocumentIdentifier(uri) },
                    TestContext.Current.CancellationToken
                );

                Assert.Empty(sent);
            },
            "let x = 1;"
        );

    [Fact]
    public async Task GetTextDocumentAttributes_NamesTheLanguage() =>
        await Utility.WithLspProjectAsync(
            (store, uri) =>
            {
                using var debouncer = new Debouncer(_noDelay);
                var handler = new TextDocumentSyncHandler(store, DiagnosticPublisherTest.Recording(out _), debouncer);

                Assert.Equal("loom", handler.GetTextDocumentAttributes(uri).LanguageId);
                return Task.CompletedTask;
            },
            "let x = 1;"
        );

    /// <remarks>
    ///     A file changed on disk that the editor has no buffer for - a branch switch, a generator - has to
    ///     reach the compile, or every request afterwards is answered against text that is gone.
    /// </remarks>
    [Fact]
    public async Task WatchedFiles_RecompilesAFileChangedOnDisk() =>
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var otherPath = Path.Combine(Path.GetDirectoryName(uri.GetFileSystemPath())!, "other.loom");
                File.WriteAllText(otherPath, "export let value: number = \"no\";");

                var publisher = DiagnosticPublisherTest.Recording(out var sent);
                using var debouncer = new Debouncer(_noDelay);
                var handler = new WatchedFilesHandler(store, publisher, debouncer);

                await handler.Handle(
                    new DidChangeWatchedFilesParams
                    {
                        Changes = new Container<FileEvent>(
                            new FileEvent { Uri = DocumentUri.FromFileSystemPath(otherPath), Type = FileChangeType.Changed }
                        )
                    },
                    TestContext.Current.CancellationToken
                );

                await WaitForAsync(() => sent.Count > 0);
                Assert.NotEmpty(sent);
            },
            "let x = 1;",
            ("other.loom", "export let value = 1;")
        );

    [Fact]
    public async Task WatchedFiles_WithNoUsableChanges_DoesNothing() =>
        await Utility.WithLspProjectAsync(
            async (store, _) =>
            {
                var publisher = DiagnosticPublisherTest.Recording(out var sent);
                using var debouncer = new Debouncer(_noDelay);
                var handler = new WatchedFilesHandler(store, publisher, debouncer);

                await handler.Handle(
                    new DidChangeWatchedFilesParams { Changes = new Container<FileEvent>() },
                    TestContext.Current.CancellationToken
                );

                await Task.Delay(50, TestContext.Current.CancellationToken);
                Assert.Empty(sent);
            },
            "let x = 1;"
        );

    [Fact]
    public async Task WatchedFiles_ReportsADeletedFileAsGone() =>
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var otherPath = Path.Combine(Path.GetDirectoryName(uri.GetFileSystemPath())!, "other.loom");
                File.Delete(otherPath);

                var publisher = DiagnosticPublisherTest.Recording(out var sent);
                using var debouncer = new Debouncer(_noDelay);
                var handler = new WatchedFilesHandler(store, publisher, debouncer);

                await handler.Handle(
                    new DidChangeWatchedFilesParams
                    {
                        Changes = new Container<FileEvent>(
                            new FileEvent { Uri = DocumentUri.FromFileSystemPath(otherPath), Type = FileChangeType.Deleted }
                        )
                    },
                    TestContext.Current.CancellationToken
                );

                await WaitForAsync(() => sent.Count > 0);
                Assert.NotEmpty(sent);
            },
            "import { value } from \"./other\";\nprint(value);",
            ("other.loom", "export let value = 1;")
        );

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
            await Task.Delay(20, TestContext.Current.CancellationToken);
    }
}
