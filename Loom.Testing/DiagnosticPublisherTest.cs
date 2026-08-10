using Loom.Core.Pipeline;
using Loom.LanguageServer;

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
    public void Next_ClearsAFileThatHadDiagnosticsAndNoLongerDoes()
    {
        var publisher = Publisher();

        Utility.WithTempProject([("main.loom", "let x: string = 1;")], (_, broken) => publisher.Next(broken));
        Utility.WithTempProject(
            [("main.loom", "let x = 1;")],
            (_, fixedUp) =>
            {
                var sent = publisher.Next(fixedUp);
                var cleared = Assert.Single(sent);

                Assert.EndsWith("main.loom", cleared.Key.Path);
                Assert.Empty(cleared.Value);
            }
        );
    }

    [Fact]
    public void Next_DoesNotKeepClearingAFileItAlreadyCleared()
    {
        var publisher = Publisher();

        Utility.WithTempProject([("main.loom", "let x: string = 1;")], (_, broken) => publisher.Next(broken));
        Utility.WithTempProject(
            [("main.loom", "let x = 1;")],
            (_, fixedUp) =>
            {
                publisher.Next(fixedUp);
                Assert.Empty(publisher.Next(fixedUp));
            }
        );
    }

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
    ///     The facade is only ever used to send; every decision the publisher makes is in
    ///     <see cref="DiagnosticPublisher.Next" />, which is what these cases drive.
    /// </summary>
    private static DiagnosticPublisher Publisher() => new(null!);
}
