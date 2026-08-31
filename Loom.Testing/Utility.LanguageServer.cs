using Loom.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol;

namespace Loom.Testing;

internal static partial class Utility
{
    /// <summary>
    ///     Opens <paramref name="source" /> as <c>src/main.loom</c> of a throwaway project in a document store,
    ///     alongside any <paramref name="otherFiles" /> the case needs, and hands both to <paramref name="act" />.
    ///     Language server requests are answered from a real compilation, so they need a project on disk.
    /// </summary>
    public static async Task WithLspProjectAsync(
        Func<DocumentStore, DocumentUri, Task> act,
        string source,
        params (string Path, string Source)[] otherFiles)
    {
        var directory = Path.Combine(Path.GetTempPath(), "loom-lsp-test-" + Guid.NewGuid());
        var sourceDirectory = Path.Combine(directory, "src");
        Directory.CreateDirectory(sourceDirectory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "loom-config.toml"), "[files]\nsource_directory = \"src\"\noutput_directory = \"dist\"\n");
            foreach (var (path, otherSource) in otherFiles)
            {
                var filePath = Path.Combine(sourceDirectory, path);
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                await File.WriteAllTextAsync(filePath, otherSource);
            }

            var mainPath = Path.Combine(sourceDirectory, "main.loom");
            await File.WriteAllTextAsync(mainPath, source);

            var store = new DocumentStore();
            var uri = DocumentUri.FromFileSystemPath(mainPath);
            store.Open(uri, source);

            await act(store, uri);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
