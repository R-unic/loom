using Loom.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.Testing;

/// <summary>
///     What a delete would break, and the warning shown for it. Unlike a rename there is nowhere to rewrite a
///     broken import to, so the whole of the feature is naming the problem before the file is gone.
/// </summary>
[Collection("Assembly")]
public class ModuleDeletionTest
{
    [Fact]
    public async Task Broken_NamesEveryRelativeImportThatWouldStopResolving() =>
        await WithProjectAsync(
            (store, directory) =>
            {
                var broken = ModuleDeletions.Broken(store.Projects(), [Path.Combine(directory, "util", "math.loom")]);

                // both main, which imports it by a path through 'util', and its neighbour inside 'util' -
                // math.loom is 'the' file every other fixture file was written to depend on
                Assert.Equal(2, broken.Count);
                Assert.Contains(broken, entry => entry.ImportingPath.EndsWith("main.loom", StringComparison.Ordinal) && entry.Specifier == "./util/math");
                Assert.Contains(broken, entry => entry.ImportingPath.EndsWith("helpers.loom", StringComparison.Ordinal) && entry.Specifier == "./math");
            }
        );

    [Fact]
    public async Task Broken_ReachesEveryFileUnderADeletedDirectory() =>
        await WithProjectAsync(
            (store, directory) =>
            {
                var broken = ModuleDeletions.Broken(store.Projects(), [Path.Combine(directory, "util")]);

                Assert.Contains(broken, entry => entry.Specifier == "./util/math");
            }
        );

    /// <remarks>The importing file is going away in the same batch, so there is nothing left to warn it about.</remarks>
    [Fact]
    public async Task Broken_LeavesOutAnImporterThatIsAlsoBeingDeleted() =>
        await WithProjectAsync(
            (store, directory) =>
            {
                var broken = ModuleDeletions.Broken(
                    store.Projects(),
                    [Path.Combine(directory, "util", "math.loom"), Path.Combine(directory, "util", "helpers.loom")]
                );

                Assert.DoesNotContain(broken, entry => entry.ImportingPath.EndsWith("helpers.loom", StringComparison.Ordinal));
            }
        );

    [Fact]
    public async Task Broken_LeavesOutAnUnrelatedFile() =>
        await WithProjectAsync(
            (store, directory) =>
            {
                var broken = ModuleDeletions.Broken(store.Projects(), [Path.Combine(directory, "unused.loom")]);
                Assert.Empty(broken);
            }
        );

    [Fact]
    public void Describe_NamesTheSpecifierAndTheFileItWasWrittenIn()
    {
        var message = ModuleDeletions.Describe([new BrokenImport(@"C:\src\main.loom", "./util/math")]);

        Assert.Contains("'./util/math'", message);
        Assert.Contains("main.loom", message);
        Assert.Contains("1 import", message);
    }

    [Fact]
    public void Describe_CapsTheListAndCountsTheRest()
    {
        var broken = Enumerable.Range(0, 8).Select(i => new BrokenImport($@"C:\src\f{i}.loom", "./util/math")).ToArray();
        var message = ModuleDeletions.Describe(broken);

        Assert.Contains("8 imports", message);
        Assert.Contains("3 more", message);
    }

    [Fact]
    public async Task Handle_WarnsWhenADeleteWouldBreakAnImport() =>
        await WithProjectAsync(
            async (store, directory) =>
            {
                string? warning = null;
                var handler = new WillDeleteFilesHandler(store, message => warning = message);

                var edit = await handler.Handle(
                    new WillDeleteFileParams
                    {
                        Files = new Container<FileDelete>(new FileDelete { Uri = new Uri(DocumentUri.FromFileSystemPath(Path.Combine(directory, "util", "math.loom")).ToString()) })
                    },
                    TestContext.Current.CancellationToken
                );

                Assert.Null(edit);
                Assert.NotNull(warning);
                Assert.Contains("./util/math", warning);
            }
        );

    [Fact]
    public async Task Handle_SaysNothingWhenNothingWouldBreak() =>
        await WithProjectAsync(
            async (store, directory) =>
            {
                var warned = false;
                var handler = new WillDeleteFilesHandler(store, _ => warned = true);

                await handler.Handle(
                    new WillDeleteFileParams
                    {
                        Files = new Container<FileDelete>(new FileDelete { Uri = new Uri(DocumentUri.FromFileSystemPath(Path.Combine(directory, "unused.loom")).ToString()) })
                    },
                    TestContext.Current.CancellationToken
                );

                Assert.False(warned);
            }
        );

    private static Task WithProjectAsync(Action<DocumentStore, string> act) =>
        WithProjectAsync((store, directory) =>
            {
                act(store, directory);
                return Task.CompletedTask;
            }
        );

    private static async Task WithProjectAsync(Func<DocumentStore, string, Task> act)
    {
        var directory = Path.Combine(Path.GetTempPath(), "loom-delete-test-" + Guid.NewGuid());
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
