using Loom.Core.Pipeline;

namespace Loom.Testing.Pipeline;

/// <summary>
///     What the CLI prints when a compile finishes. Every line here is the only thing a user sees of a
///     build, so a result whose diagnostics or timing never make it to the console is a build that looks
///     like it did nothing.
/// </summary>
[Collection("Assembly")]
public class LogTest
{
    [Fact]
    public void Fatal_GoesToStandardError()
    {
        var error = Capture(() => Log.Fatal("something broke"), standardError: true);

        Assert.Contains("fatal", error);
        Assert.Contains("something broke", error);
    }

    [Fact]
    public void Info_GoesToStandardOutput()
    {
        var output = Capture(() => Log.Info("all good"));

        Assert.Contains("info", output);
        Assert.Contains("all good", output);
    }

    [Fact]
    public void OutputResult_ReportsTheTiming()
    {
        var output = Capture(() => Log.OutputResult(Compile("let x = 1;")));

        Assert.Contains("Compiled in", output);
        Assert.Contains("ms", output);
    }

    [Fact]
    public void OutputResult_ReportsWhatTheCompileFound()
    {
        var output = Capture(() => Log.OutputResult(Compile("let x: number = \"no\";")));

        Assert.Contains("not assignable", output);
    }

    /// <remarks>A clean build says so with its timing alone and prints no diagnostic block at all.</remarks>
    [Fact]
    public void OutputResult_OnACleanCompile_PrintsNoDiagnostics()
    {
        var output = Capture(() => Log.OutputResult(Compile("let x = 1;")));

        Assert.DoesNotContain("error", output);
        Assert.DoesNotContain("warning", output);
    }

    private static CompilationResult Compile(string source)
    {
        CompilationResult? result = null;
        Utility.WithTempProject([("main.loom", source)], (_, compiled) => result = compiled);

        return result!;
    }

    private static string Capture(Action act, bool standardError = false)
    {
        var previousOut = Console.Out;
        var previousError = Console.Error;
        var writer = new StringWriter();
        try
        {
            if (standardError)
                Console.SetError(writer);
            else
                Console.SetOut(writer);

            act();
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }

        return writer.ToString();
    }
}
