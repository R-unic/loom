using Loom.Core.Diagnostics;
using Loom.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace Loom.Testing;

[Collection("Assembly")]
public class DocumentStoreTest
{
    [Fact]
    public void Open_CompilesAndReportsDiagnosticsForTheDocument()
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
            var result = store.Open(uri, "let x: string = 1;");

            Assert.NotNull(result);
            Utility.AssertDiagnostic(result.Diagnostics, InternalCodes.TypeMismatch, "Type '1' is not assignable to type 'string'.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Change_RecompilesIncrementallyAfterOpen()
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
            store.Open(uri, "let x = 1;");

            var changed = store.Change(
                uri,
                [new TextDocumentContentChangeEvent { Range = new LspRange(new Position(0, 8), new Position(0, 9)), Text = "true" }]
            );

            Assert.NotNull(changed);
            Utility.AssertNoErrors(changed);
            var file = Assert.Single(changed.Files);
            Assert.Contains("true", file.RenderedLuau);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Change_WithoutPriorOpen_ReturnsNull()
    {
        var store = new DocumentStore();
        var uri = DocumentUri.FromFileSystemPath(Path.Combine(Path.GetTempPath(), "does-not-exist.loom"));

        Assert.Null(store.Change(uri, [new TextDocumentContentChangeEvent { Text = "let x = 1;" }]));
    }

    [Fact]
    public void Open_OutsideAnyProject_ReturnsNull()
    {
        var directory = Path.Combine(Path.GetTempPath(), "loom-lsp-test-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "orphan.loom");
            File.WriteAllText(path, "let x = 1;");

            var store = new DocumentStore();
            var uri = DocumentUri.FromFileSystemPath(path);

            Assert.Null(store.Open(uri, "let x = 1;"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void TryGetState_ReturnsASymbolSnapshotThatALaterRecompileDoesNotChange()
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
            store.Open(uri, "let x = 1;");

            Assert.True(store.TryGetState(uri, out var opened));
            var snapshot = opened.Completions;
            Assert.Contains(snapshot.Identifiers, symbol => symbol.Name == "x" && symbol.Detail() == ": 1");
            Assert.DoesNotContain(snapshot.Identifiers, symbol => symbol.Name == "y");

            store.Change(uri, [new TextDocumentContentChangeEvent { Text = "let x = 1;\nlet y = \"two\";" }]);

            Assert.Same(snapshot, opened.Completions);
            Assert.DoesNotContain(snapshot.Identifiers, symbol => symbol.Name == "y");

            Assert.True(store.TryGetState(uri, out var changed));
            Assert.Contains(changed.Completions.Identifiers, symbol => symbol.Name == "y" && symbol.Detail() == ": \"two\"");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Change_ConcurrentlyWithStateReads_KeepsEveryEditAndNeverFaults()
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
            store.Open(uri, "let x = 1;");

            var names = Enumerable.Range(0, 24).Select(index => $"a{index}").ToArray();
            var readerFaults = 0;
            using var editsDone = new CancellationTokenSource();
            var reader = Task.Run(() =>
                {
                    while (!editsDone.IsCancellationRequested)
                        try
                        {
                            if (store.TryGetState(uri, out var state))
                                _ = state.Completions.Identifiers.Count(symbol => symbol.Name.Length > 0);
                        }
                        catch (Exception)
                        {
                            Interlocked.Increment(ref readerFaults);
                        }
                },
                TestContext.Current.CancellationToken
            );

            Parallel.ForEach(
                names,
                name => store.Change(uri, [new TextDocumentContentChangeEvent { Range = new LspRange(new Position(0, 0), new Position(0, 0)), Text = $"let {name} = 1;\n" }])
            );

            await editsDone.CancelAsync();
            await reader;

            Assert.Equal(0, readerFaults);
            Assert.True(store.TryGetState(uri, out var finalState));
            foreach (var name in names)
                Assert.Contains($"let {name} = 1;", finalState.File.SourceFile.SourceText);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Change_AfterParseFailure_RecoversOnceContentIsFixedOrDocumentIsReopened()
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
            store.Open(uri, "let x = 1;");

            var broken = store.Change(uri, [new TextDocumentContentChangeEvent { Text = "let" }]);
            Assert.NotNull(broken);

            var fixedResult = store.Change(uri, [new TextDocumentContentChangeEvent { Text = "let x = 1;" }]);
            Assert.NotNull(fixedResult);
            Utility.AssertNoErrors(fixedResult);

            var emptied = store.Change(uri, [new TextDocumentContentChangeEvent { Text = "" }]);
            Assert.NotNull(emptied);
            Utility.AssertNoErrors(emptied);

            store.Close(uri);
            var reopened = store.Open(uri, "let x = 1;");
            Assert.NotNull(reopened);
            Utility.AssertNoErrors(reopened);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
