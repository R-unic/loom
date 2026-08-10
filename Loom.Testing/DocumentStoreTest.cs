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

            Assert.True(
                store.Change(
                    uri,
                    [new TextDocumentContentChangeEvent { Range = new LspRange(new Position(0, 8), new Position(0, 9)), Text = "true" }]
                )
            );

            var changed = store.Compile(uri);
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
    public void Change_WithoutPriorOpen_RecordsNothing()
    {
        var store = new DocumentStore();
        var uri = DocumentUri.FromFileSystemPath(Path.Combine(Path.GetTempPath(), "does-not-exist.loom"));

        Assert.False(store.Change(uri, [new TextDocumentContentChangeEvent { Text = "let x = 1;" }]));
        Assert.Null(store.Compile(uri));
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

            store.Change(uri, [new TextDocumentContentChangeEvent { Text = "let" }]);
            Assert.NotNull(store.Compile(uri));

            store.Change(uri, [new TextDocumentContentChangeEvent { Text = "let x = 1;" }]);
            var fixedResult = store.Compile(uri);
            Assert.NotNull(fixedResult);
            Utility.AssertNoErrors(fixedResult);

            store.Change(uri, [new TextDocumentContentChangeEvent { Text = "" }]);
            var emptied = store.Compile(uri);
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

    [Fact]
    public void Change_DoesNotCompile() =>
        WithProject(
            (store, uri, _) =>
            {
                store.Change(uri, [new TextDocumentContentChangeEvent { Text = "let x = 2;" }]);
                Assert.True(store.IsDirty(uri));
            }
        );

    [Fact]
    public void Compile_BringsTheDocumentUpToDateAndLeavesItClean() =>
        WithProject(
            (store, uri, _) =>
            {
                store.Change(uri, [new TextDocumentContentChangeEvent { Text = "let renamed = 2;" }]);

                Assert.NotNull(store.Compile(uri));
                Assert.False(store.IsDirty(uri));
            }
        );

    [Fact]
    public void Compile_WithNothingChanged_ReusesTheLastResult() =>
        WithProject(
            (store, uri, _) =>
            {
                store.Change(uri, [new TextDocumentContentChangeEvent { Text = "let x = 2;" }]);

                Assert.Same(store.Compile(uri), store.Compile(uri));
            }
        );

    /// <summary>Reading is what forces the compile, so an answer never describes text the user has already replaced.</summary>
    [Fact]
    public void TryGetState_CompilesTheEditsThatHaveNotBeenCompiledYet() =>
        WithProject(
            (store, uri, _) =>
            {
                store.Change(uri, [new TextDocumentContentChangeEvent { Text = "let renamed = 2;" }]);

                Assert.True(store.TryGetState(uri, out var state));
                Assert.False(store.IsDirty(uri));
                Assert.Contains(state.Completions.Identifiers, symbol => symbol.Name == "renamed");
            }
        );

    /// <summary>
    ///     Both buffers share a unit, so compiling one has to carry the other's unsaved text with it - otherwise
    ///     this file is analyzed against a version of its neighbour that exists only on disk.
    /// </summary>
    [Fact]
    public void Compile_CarriesEveryOpenBuffersEditsIntoTheOneCompile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "loom-lsp-test-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(directory, "src"));
        try
        {
            File.WriteAllText(Path.Combine(directory, "loom-config.toml"), "[files]\nsource_directory = \"src\"\noutput_directory = \"dist\"\n");
            var mathPath = Path.Combine(directory, "src", "math.loom");
            var mainPath = Path.Combine(directory, "src", "main.loom");
            File.WriteAllText(mathPath, "export fn double(n: number): number { return n * 2; }");
            File.WriteAllText(mainPath, "import { double } from \"./math\";\nlet four = double(2);");

            var store = new DocumentStore();
            var mathUri = DocumentUri.FromFileSystemPath(mathPath);
            var mainUri = DocumentUri.FromFileSystemPath(mainPath);
            store.Open(mathUri, File.ReadAllText(mathPath));
            store.Open(mainUri, File.ReadAllText(mainPath));

            // the export main.loom imports is renamed in the editor and never saved
            store.Change(mathUri, [new TextDocumentContentChangeEvent { Text = "export fn twice(n: number): number { return n * 2; }" }]);

            var result = store.Compile(mainUri);
            Assert.NotNull(result);
            Assert.False(store.IsDirty(mathUri));
            Assert.Contains(result.Diagnostics.Set, diagnostic => diagnostic.Code == InternalCodes.NoExportedMember);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static void WithProject(Action<DocumentStore, DocumentUri, string> act)
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

            act(store, uri, path);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
