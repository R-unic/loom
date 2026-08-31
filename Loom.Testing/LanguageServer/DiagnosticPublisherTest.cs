using Loom.Config;
using Loom.Core.Pipeline;
using Loom.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.Testing.LanguageServer;

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
                Assert.All(sent.Values, Assert.NotEmpty);
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
                otherProjectsFile = unit.SourceFiles[0].AbsolutePath;
                publisher.Next(result);
            }
        );

        Utility.WithTempProject(
            [("main.loom", "let x = 1;")],
            (_, clean) => Assert.DoesNotContain(publisher.Next(clean).Keys, uri => FilePaths.Same(uri.GetFileSystemPath(), otherProjectsFile))
        );
    }

    /// <remarks>A file the compiler gave up on entirely is still one the compile had an answer for, and is covered the same as a file that merely has diagnostics.</remarks>
    [Fact]
    public void Next_CoversAFileTheCompilerGaveUpOnAsWellAsTheOnesThatSucceeded()
    {
        var directory = Path.Combine(Path.GetTempPath(), "loom-test-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(directory, "src"));
        try
        {
            File.WriteAllText(Path.Combine(directory, "loom-config.toml"), "[files]\nsource_directory = \"src\"\noutput_directory = \"dist\"\n");
            File.WriteAllText(Path.Combine(directory, "src", "main.loom"), "let x = 1;");

            var config = ConfigReader.LocateFromDirectory(directory);
            Assert.NotNull(config);
            config.NoEmit = true;

            var unit = new CompilationUnit(config);
            config.Files.OutputDirectory = null!;

            var result = unit.Compile();
            Assert.Single(result.Failures);

            var sent = Publisher().Next(result);
            Assert.Contains(sent.Keys, uri => uri.Path.EndsWith("main.loom", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>A dead connection is not this class's problem to solve: sending has to swallow whatever the underlying transport throws.</summary>
    [Fact]
    public void Publish_WhenSendingThrows_DoesNotPropagate() =>
        Utility.WithTempProject(
            [("main.loom", "let x: string = 1;")],
            (_, result) => new DiagnosticPublisher(_ => throw new InvalidOperationException("dead connection")).Publish(result)
        );

    /// <summary>
    ///     Sending is one call on a connection a test has no way to hold; every decision the publisher makes
    ///     is in <see cref="DiagnosticPublisher.Next" />, which is what these cases drive.
    /// </summary>
    private static DiagnosticPublisher Publisher() => new(static _ => { });

    /// <summary>A publisher that records what it would have sent, for the cases that drive it end to end.</summary>
    internal static DiagnosticPublisher Recording(out List<PublishDiagnosticsParams> sent)
    {
        var recorded = new List<PublishDiagnosticsParams>();
        sent = recorded;

        return new DiagnosticPublisher(recorded.Add);
    }
}
