using System.Text;
using Loom.Core.Pipeline;
using Loom.LanguageServer;

namespace Loom.Testing;

[Collection("Assembly")]
public class ProtocolStreamsTest
{
    [Fact]
    public void Claim_KeepsConsoleOutputOutOfTheProtocolStream()
    {
        var output = new MemoryStream();
        var consoleOutput = new StringWriter();

        WithClaimedConsole(() =>
            {
                var streams = ProtocolStreams.Claim(new MemoryStream(), output, consoleOutput);

                Console.WriteLine("Content-Length: 12");
                Log.Info("Wrote dist/main.luau");

                Assert.Same(output, streams.Output);
            }
        );

        Assert.Empty(output.ToArray());
        Assert.Contains("Content-Length: 12", consoleOutput.ToString());
        Assert.Contains("Wrote dist/main.luau", consoleOutput.ToString());
    }

    [Fact]
    public void Claim_LeavesTheProtocolInputUnreadByConsole()
    {
        var input = new MemoryStream(Encoding.UTF8.GetBytes("Content-Length: 2\r\n\r\n{}"));

        WithClaimedConsole(() =>
            {
                var streams = ProtocolStreams.Claim(input, new MemoryStream(), new StringWriter());

                Assert.Null(Console.In.ReadLine());
                Assert.Same(input, streams.Input);
                Assert.Equal(0, streams.Input.Position);
            }
        );
    }

    private static void WithClaimedConsole(Action action)
    {
        var originalOutput = Console.Out;
        var originalInput = Console.In;
        try
        {
            action();
        }
        finally
        {
            Console.SetOut(originalOutput);
            Console.SetIn(originalInput);
        }
    }
}
