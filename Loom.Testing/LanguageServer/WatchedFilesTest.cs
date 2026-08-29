using Loom.Core.Diagnostics;
using Loom.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.Testing;

[Collection("Assembly")]
public class WatchedFilesTest
{
    [Fact]
    public void ReloadFromDisk_PicksUpAnEditMadeOutsideTheEditor() =>
        WithProject(
            (store, paths) =>
            {
                store.Open(DocumentUri.FromFileSystemPath(paths.Main), File.ReadAllText(paths.Main));

                File.WriteAllText(paths.Math, "export fn twice(n: number): number { return n * 2; }");
                var result = Assert.Single(store.ReloadFromDisk([new WatchedFile(paths.Math, true)]));

                Assert.Contains(result.Diagnostics.Set, diagnostic => diagnostic.Code == InternalCodes.NoExportedMember);
            }
        );

    [Fact]
    public void ReloadFromDisk_RefreshesWhatTheOpenDocumentIsAnsweredFrom() =>
        WithProject(
            (store, paths) =>
            {
                var mainUri = DocumentUri.FromFileSystemPath(paths.Main);
                store.Open(mainUri, File.ReadAllText(paths.Main));

                File.WriteAllText(paths.Math, "export fn double(n: number): number { return n * 2; }\nexport fn triple(n: number): number { return n * 3; }");
                store.ReloadFromDisk([new WatchedFile(paths.Math, true)]);

                Assert.True(store.TryGetState(mainUri, out var state));
                Assert.Contains(state.Completions.Importable, symbol => symbol.Name == "triple");
            }
        );

    /// <summary>The buffer is what the user is looking at and what every request is answered against.</summary>
    [Fact]
    public void ReloadFromDisk_LeavesAnOpenDocumentsBufferAlone() =>
        WithProject(
            (store, paths) =>
            {
                var mathUri = DocumentUri.FromFileSystemPath(paths.Math);
                store.Open(mathUri, File.ReadAllText(paths.Math));
                store.Change(mathUri, [new TextDocumentContentChangeEvent { Text = "export fn edited(n: number): number { return n; }" }]);

                File.WriteAllText(paths.Math, "export fn from_disk(n: number): number { return n; }");
                store.ReloadFromDisk([new WatchedFile(paths.Math, true)]);

                Assert.True(store.TryGetState(mathUri, out var state));
                Assert.Contains("edited", state.File.SourceFile.SourceText);
            }
        );

    [Fact]
    public void ReloadFromDisk_SeesAFileCreatedOutsideTheEditor() =>
        WithProject(
            (store, paths) =>
            {
                var mainUri = DocumentUri.FromFileSystemPath(paths.Main);
                store.Open(mainUri, File.ReadAllText(paths.Main));

                var added = Path.Combine(Path.GetDirectoryName(paths.Main)!, "extra.loom");
                File.WriteAllText(added, "export fn quadruple(n: number): number { return n * 4; }");
                store.ReloadFromDisk([new WatchedFile(added, true)]);

                Assert.True(store.TryGetState(mainUri, out var state));
                Assert.Contains(state.Unit.SourceFiles, file => file.Name == "extra.loom");
                Assert.Contains(state.Completions.Importable, symbol => symbol.Name == "quadruple");
            }
        );

    [Fact]
    public void ReloadFromDisk_ForgetsAFileDeletedOutsideTheEditor() =>
        WithProject(
            (store, paths) =>
            {
                var mainUri = DocumentUri.FromFileSystemPath(paths.Main);
                store.Open(mainUri, File.ReadAllText(paths.Main));

                File.Delete(paths.Math);
                var result = Assert.Single(store.ReloadFromDisk([new WatchedFile(paths.Math, false)]));

                Assert.DoesNotContain(result.Files, file => file.SourceFile.Name == "math.loom");
                Assert.True(store.TryGetState(mainUri, out var state));
                Assert.DoesNotContain(state.Unit.SourceFiles, file => file.Name == "math.loom");
            }
        );

    [Fact]
    public void ReloadFromDisk_IgnoresFilesOutsideAnyProject() =>
        WithProject(
            (store, paths) =>
            {
                store.Open(DocumentUri.FromFileSystemPath(paths.Main), File.ReadAllText(paths.Main));

                var elsewhere = Path.Combine(Path.GetTempPath(), "loom-not-in-any-project.loom");
                Assert.Empty(store.ReloadFromDisk([new WatchedFile(elsewhere, true)]));
            }
        );

    [Fact]
    public void ReloadFromDisk_IgnoresFilesThatAreNotLoomSource() =>
        WithProject(
            (store, paths) =>
            {
                store.Open(DocumentUri.FromFileSystemPath(paths.Main), File.ReadAllText(paths.Main));

                Assert.Empty(store.ReloadFromDisk([new WatchedFile(Path.ChangeExtension(paths.Math, ".luau"), true)]));
            }
        );

    /// <summary>
    ///     A manifest decides where a project's sources are and what it depends on, so there is nothing to
    ///     update in place - the unit is rebuilt from the configuration as it now reads.
    /// </summary>
    [Fact]
    public void ReloadFromDisk_ForAManifestChange_RebuildsTheProject() =>
        WithProject(
            (store, paths) =>
            {
                var mainUri = DocumentUri.FromFileSystemPath(paths.Main);
                store.Open(mainUri, File.ReadAllText(paths.Main));
                Assert.True(store.TryGetState(mainUri, out var before));

                store.ReloadFromDisk([new WatchedFile(paths.Config, true)]);

                Assert.True(store.TryGetState(mainUri, out var after));
                Assert.NotSame(before.Unit, after.Unit);
            }
        );

    private static void WithProject(Action<DocumentStore, (string Config, string Main, string Math)> act)
    {
        var directory = Path.Combine(Path.GetTempPath(), "loom-watch-test-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(directory, "src"));
        try
        {
            var config = Path.Combine(directory, "loom-config.toml");
            File.WriteAllText(config, "[files]\nsource_directory = \"src\"\noutput_directory = \"dist\"\n");

            var math = Path.Combine(directory, "src", "math.loom");
            var main = Path.Combine(directory, "src", "main.loom");
            File.WriteAllText(math, "export fn double(n: number): number { return n * 2; }");
            File.WriteAllText(main, "import { double } from \"./math\";\nlet four = double(2);");

            act(new DocumentStore(), (config, main, math));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
