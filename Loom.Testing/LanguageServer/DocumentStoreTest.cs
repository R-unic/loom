using Loom.Config;
using Loom.Core.Diagnostics;
using Loom.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;
using Version = Loom.Config.Version;

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

    /// <remarks>
    ///     An editor has to see what a build sees: the unit spans the packages the lock file pins, so a symbol
    ///     imported from one resolves rather than coming back unknown.
    /// </remarks>
    [Fact]
    public void Open_SpansThePackagesTheLockFilePins()
    {
        var directory = WritePackagedProject(writeLock: true);
        try
        {
            var path = Path.Combine(directory, "src", "main.loom");
            var result = new DocumentStore().Open(DocumentUri.FromFileSystemPath(path), File.ReadAllText(path));

            Assert.NotNull(result);
            Utility.AssertNoErrors(result);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <remarks>
    ///     A project half-way through being set up still gets answers about the file on screen: the import is
    ///     reported as unresolved, which it is, rather than the editor answering nothing at all.
    /// </remarks>
    [Fact]
    public void Open_AProjectWithNoLockFile_StillCompilesItsOwnFiles()
    {
        var directory = WritePackagedProject(writeLock: false);
        try
        {
            var path = Path.Combine(directory, "src", "main.loom");
            var result = new DocumentStore().Open(DocumentUri.FromFileSystemPath(path), File.ReadAllText(path));

            Assert.NotNull(result);
            Utility.AssertDiagnostic(result.Diagnostics, InternalCodes.PackageNotFound, "Cannot find package 'math'.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>A project with one package installed the way a package manager leaves it, optionally locked.</summary>
    private static string WritePackagedProject(bool writeLock)
    {
        var directory = Path.Combine(Path.GetTempPath(), "loom-lsp-test-" + Guid.NewGuid());
        var packageDirectory = Path.Combine(directory, "packages", "math");
        Directory.CreateDirectory(Path.Combine(directory, "src"));
        Directory.CreateDirectory(Path.Combine(packageDirectory, "src"));

        File.WriteAllText(
            Path.Combine(directory, "loom-config.toml"),
            "[dependencies]\nmath = \"^1.0\"\n[files]\nsource_directory = \"src\"\noutput_directory = \"dist\"\n"
        );

        File.WriteAllText(
            Path.Combine(packageDirectory, "loom-config.toml"),
            "project_type = \"library\"\n[package]\nname = \"math\"\nversion = \"1.0.0\"\n[files]\nsource_directory = \"src\"\noutput_directory = \"dist\"\n"
        );

        File.WriteAllText(Path.Combine(packageDirectory, "src", "init.loom"), "export let pi = 3;");
        File.WriteAllText(Path.Combine(directory, "src", "main.loom"), "import { pi } from \"math\";\nlet x: number = pi;");
        if (writeLock)
            new LockFile([new LockedPackage(PackageName.Parse("math"), Version.Parse("1.0.0"))]).WriteTo(directory);

        return directory;
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

            // asserted against the semantic model rather than the emitted Luau: the server compiles with
            // 'no_emit', so there is no Luau, and the model is what it answers every request from anyway
            var file = Assert.Single(changed.Files);
            Assert.Equal("let x = true;", file.SourceFile.SourceText);
            Assert.Equal("true", file.SemanticModel.GetType(Assert.Single(file.Tree.Statements)).ToString());
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
            await File.WriteAllTextAsync(Path.Combine(directory, "loom-config.toml"), "[files]\nsource_directory = \"src\"\noutput_directory = \"dist\"\n", TestContext.Current.CancellationToken);
            var path = Path.Combine(directory, "src", "main.loom");
            await File.WriteAllTextAsync(path, "let x = 1;", TestContext.Current.CancellationToken);

            var store = new DocumentStore();
            var uri = DocumentUri.FromFileSystemPath(path);
            store.Open(uri, "let x = 1;");

            var names = Enumerable.Range(0, 24).Select(index => $"a{index}").ToArray();
            var readerFaults = 0;
            using var editsDone = new CancellationTokenSource();
            var stopReading = editsDone.Token;
            var reader = Task.Run(() =>
                {
                    while (!stopReading.IsCancellationRequested)
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

    /// <summary>An editor discards unsaved edits when a document closes, so the project has to go back to what is on disk.</summary>
    [Fact]
    public void Close_PutsTheFileBackToItsSavedText()
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

            store.Change(mathUri, [new TextDocumentContentChangeEvent { Text = "export fn twice(n: number): number { return n * 2; }" }]);
            Assert.Contains(store.Compile(mainUri)!.Diagnostics.Set, diagnostic => diagnostic.Code == InternalCodes.NoExportedMember);

            store.Close(mathUri);

            Assert.True(store.TryGetState(mainUri, out var state));
            Utility.AssertNoErrors(state.File.Diagnostics);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Close_WithNoUnsavedEdits_LeavesTheProjectAlone() =>
        WithProject(
            (store, uri, __) =>
            {
                store.Close(uri);
                Assert.False(store.TryGetState(uri, out _));
            }
        );

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
