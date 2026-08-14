using Loom.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.Testing;

/// <summary>
///     What has to change in the source when a file moves. A relative specifier is a path from the importing
///     file's directory, so a move breaks both directions at once: what the moved file names, and what names
///     it.
/// </summary>
[Collection("Assembly")]
public class ModuleRenameTest
{
    [Fact]
    public async Task Rename_RewritesTheSpecifiersThatNamedTheMovedFile() =>
        await WithProjectAsync(
            (store, directory) =>
            {
                var edits = ModuleRenames.EditsFor(
                    store.Projects(),
                    [new ModuleRename(Path.Combine(directory, "util", "math.loom"), Path.Combine(directory, "util", "arithmetic.loom"))]
                );

                var edit = Assert.Single(EditsOf(edits, Path.Combine(directory, "main.loom")));
                Assert.Equal("./util/arithmetic", edit.NewText);
            }
        );

    /// <remarks>The edit replaces what is inside the quotes, so how the path was quoted is not the server's to change.</remarks>
    [Fact]
    public async Task Rename_ReplacesThePathWithoutTheQuotesAroundIt() =>
        await WithProjectAsync(
            (store, directory) =>
            {
                var edits = ModuleRenames.EditsFor(
                    store.Projects(),
                    [new ModuleRename(Path.Combine(directory, "util", "math.loom"), Path.Combine(directory, "util", "arithmetic.loom"))]
                );

                var edit = Assert.Single(EditsOf(edits, Path.Combine(directory, "main.loom")));
                Assert.Equal(0, edit.Range.Start.Line);
                Assert.Equal(24, edit.Range.Start.Character);
                Assert.Equal(35, edit.Range.End.Character);
            }
        );

    /// <remarks>The moved file's own imports were written from where it used to be, and are just as broken by the move.</remarks>
    [Fact]
    public async Task Rename_RewritesTheMovedFilesOwnImports() =>
        await WithProjectAsync(
            (store, directory) =>
            {
                var edits = ModuleRenames.EditsFor(
                    store.Projects(),
                    [new ModuleRename(Path.Combine(directory, "util", "helpers.loom"), Path.Combine(directory, "helpers.loom"))]
                );

                var edit = Assert.Single(EditsOf(edits, Path.Combine(directory, "util", "helpers.loom")));
                Assert.Equal("./util/math", edit.NewText);
            }
        );

    /// <remarks>A folder rename arrives as the folder, and breaks every import that crossed it at once.</remarks>
    [Fact]
    public async Task Rename_OfADirectory_MovesEveryFileUnderIt() =>
        await WithProjectAsync(
            (store, directory) =>
            {
                var edits = ModuleRenames.EditsFor(
                    store.Projects(),
                    [new ModuleRename(Path.Combine(directory, "util"), Path.Combine(directory, "lib"))]
                );

                var edit = Assert.Single(EditsOf(edits, Path.Combine(directory, "main.loom")));
                Assert.Equal("./lib/math", edit.NewText);
            }
        );

    /// <remarks>Both ends moving together leaves the path between them unchanged, and an edit writing it back is noise in the diff.</remarks>
    [Fact]
    public async Task Rename_OfADirectory_LeavesTheImportsInsideItAlone() =>
        await WithProjectAsync(
            (store, directory) =>
            {
                var edits = ModuleRenames.EditsFor(
                    store.Projects(),
                    [new ModuleRename(Path.Combine(directory, "util"), Path.Combine(directory, "lib"))]
                );

                Assert.Empty(EditsOf(edits, Path.Combine(directory, "util", "helpers.loom")));
            }
        );

    [Fact]
    public async Task Rename_OfAnUnrelatedFile_ChangesNothing() =>
        await WithProjectAsync(
            (store, directory) =>
            {
                var edits = ModuleRenames.EditsFor(
                    store.Projects(),
                    [new ModuleRename(Path.Combine(directory, "unused.loom"), Path.Combine(directory, "spare.loom"))]
                );

                Assert.Empty(edits);
            }
        );

    [Fact]
    public async Task Handle_AnswersWithNoEditWhenNothingWouldChange() =>
        await WithProjectAsync(
            async (store, directory) =>
            {
                var edit = await new WillRenameFilesHandler(store).Handle(
                    new WillRenameFilesParameters
                    {
                        Files = new Container<FileRenaming>(
                            new FileRenaming
                            {
                                OldUri = DocumentUri.FromFileSystemPath(Path.Combine(directory, "unused.loom")),
                                NewUri = DocumentUri.FromFileSystemPath(Path.Combine(directory, "spare.loom"))
                            }
                        )
                    },
                    TestContext.Current.CancellationToken
                );

                Assert.Null(edit);
            }
        );

    [Fact]
    public async Task Handle_ReturnsTheEditsKeyedByTheFilesTheyChange() =>
        await WithProjectAsync(
            async (store, directory) =>
            {
                var edit = await new WillRenameFilesHandler(store).Handle(
                    new WillRenameFilesParameters
                    {
                        Files = new Container<FileRenaming>(
                            new FileRenaming
                            {
                                OldUri = DocumentUri.FromFileSystemPath(Path.Combine(directory, "util", "math.loom")),
                                NewUri = DocumentUri.FromFileSystemPath(Path.Combine(directory, "util", "arithmetic.loom"))
                            }
                        )
                    },
                    TestContext.Current.CancellationToken
                );

                Assert.NotNull(edit);

                // both files that named it: the one importing it by a path through 'util', and its neighbour
                // inside 'util' importing it by name
                Assert.Equal(2, edit!.Changes!.Count);
                Assert.Equal("./util/arithmetic", Single(edit, "main.loom").NewText);
                Assert.Equal("./arithmetic", Single(edit, "helpers.loom").NewText);
            }
        );

    private static TextEdit Single(WorkspaceEdit edit, string fileName) =>
        Assert.Single(edit.Changes!.First(change => change.Key.Path.EndsWith(fileName, StringComparison.Ordinal)).Value);

    private static IReadOnlyList<TextEdit> EditsOf(IReadOnlyDictionary<DocumentUri, IReadOnlyList<TextEdit>> edits, string path) =>
        edits.GetValueOrDefault(DocumentUri.FromFileSystemPath(path)) ?? [];

    /// <summary>
    ///     A project laid out so that a move has something to break in both directions: <c>main</c> imports
    ///     down into <c>util</c>, and a file inside <c>util</c> imports its neighbour.
    /// </summary>
    private static Task WithProjectAsync(Action<DocumentStore, string> act) =>
        WithProjectAsync((store, directory) =>
            {
                act(store, directory);
                return Task.CompletedTask;
            }
        );

    private static async Task WithProjectAsync(Func<DocumentStore, string, Task> act)
    {
        var directory = Path.Combine(Path.GetTempPath(), "loom-rename-test-" + Guid.NewGuid());
        var sourceDirectory = Path.Combine(directory, "src");
        Directory.CreateDirectory(Path.Combine(sourceDirectory, "util"));
        try
        {
            File.WriteAllText(Path.Combine(directory, "loom-config.toml"), "[files]\nsource_directory = \"src\"\noutput_directory = \"dist\"\n");
            File.WriteAllText(Path.Combine(sourceDirectory, "util", "math.loom"), "export fn double(n: number): number -> n * 2;");
            File.WriteAllText(Path.Combine(sourceDirectory, "util", "helpers.loom"), "import { double } from \"./math\"\nexport let four = double(2);");
            File.WriteAllText(Path.Combine(sourceDirectory, "unused.loom"), "let alone = 1;");

            var mainPath = Path.Combine(sourceDirectory, "main.loom");
            var mainSource = "import { double } from \"./util/math\"\nlet four = double(2);";
            File.WriteAllText(mainPath, mainSource);

            var store = new DocumentStore();
            store.Open(DocumentUri.FromFileSystemPath(mainPath), mainSource);

            await act(store, sourceDirectory);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
