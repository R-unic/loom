using Loom.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.Testing;

[Collection("Assembly")]
public class DiagnosticPublisherTest
{
    [Fact]
    public void Next_SendsASetPerFileTheCompileFoundSomethingIn() =>
        Utility.WithTempProject(
            [("main.loom", "let x: string = 1;"), ("other.loom", "let y: number = \"two\";")],
            (_, result) =>
            {
                var sent = Publisher().Next(result);

                Assert.Equal(2, sent.Count);
                Assert.All(sent.Values, diagnostics => Assert.NotEmpty(diagnostics));
            }
        );

    /// <summary>
    ///     A client keeps whatever it was last sent, so a file whose errors are gone has to be told so
    ///     explicitly - nothing else in the protocol says it.
    /// </summary>
    [Fact]
    public async Task Next_ClearsAFileThatHadDiagnosticsAndNoLongerDoes() =>
        await Utility.WithLspProjectAsync(
            (store, uri) =>
            {
                var publisher = Publisher();
                publisher.Next(store.Compile(uri)!);

                store.Change(uri, [new TextDocumentContentChangeEvent { Text = "let x = 1;" }]);
                var cleared = Assert.Single(publisher.Next(store.Compile(uri)!));

                Assert.EndsWith("main.loom", cleared.Key.Path);
                Assert.Empty(cleared.Value);
                return Task.CompletedTask;
            },
            "let x: string = 1;"
        );

    [Fact]
    public async Task Next_DoesNotKeepClearingAFileItAlreadyCleared() =>
        await Utility.WithLspProjectAsync(
            (store, uri) =>
            {
                var publisher = Publisher();
                publisher.Next(store.Compile(uri)!);

                store.Change(uri, [new TextDocumentContentChangeEvent { Text = "let x = 1;" }]);
                var result = store.Compile(uri)!;

                publisher.Next(result);
                Assert.Empty(publisher.Next(result));
                return Task.CompletedTask;
            },
            "let x: string = 1;"
        );

    [Fact]
    public void Next_KeepsSendingAFileThatStillHasDiagnostics() =>
        Utility.WithTempProject(
            [("main.loom", "let x: string = 1;")],
            (_, result) =>
            {
                var publisher = Publisher();
                publisher.Next(result);

                Assert.NotEmpty(Assert.Single(publisher.Next(result)).Value);
            }
        );

    [Fact]
    public void Publish_WithNoResult_SendsNothing() => Publisher().Publish(null);

    /// <summary>
    ///     A workspace may hold more than one project, and each compiles on its own. Clearing every file not in
    ///     this result would wipe the other project's diagnostics every time this one is edited.
    /// </summary>
    [Fact]
    public void Next_LeavesAnotherProjectsFilesAlone()
    {
        var publisher = Publisher();
        var otherProjectsFile = "";

        Utility.WithTempProject(
            [("other.loom", "let y: number = \"two\";")],
            (unit, result) =>
            {
                otherProjectsFile = unit.SourceFiles.First().AbsolutePath;
                publisher.Next(result);
            }
        );

        Utility.WithTempProject(
            [("main.loom", "let x = 1;")],
            (_, clean) => Assert.DoesNotContain(publisher.Next(clean).Keys, uri => FilePaths.Same(uri.GetFileSystemPath(), otherProjectsFile))
        );
    }

    /// <summary>
    ///     The facade is only ever used to send; every decision the publisher makes is in
    ///     <see cref="DiagnosticPublisher.Next" />, which is what these cases drive.
    /// </summary>
    private static DiagnosticPublisher Publisher() => new(null!);
}
