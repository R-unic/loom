using Loom.Core.Diagnostics;

namespace Loom.Core.Pipeline;

public static class Log
{
    public static void Fatal(string message) =>
        Console.Error.WriteLine($"{Colors.Dim}[{Colors.Reset}{Colors.Bold}{Colors.Red}fatal{Colors.Reset}{Colors.Dim}]{Colors.Reset} {message}");

    /// <summary>For something that stopped a step short of what was asked, rather than the command as a whole - unlike <see cref="Fatal" />, execution continues.</summary>
    public static void Warn(string message) =>
        Console.Error.WriteLine($"{Colors.Dim}[{Colors.Reset}{Colors.Bold}{Colors.Yellow}warn{Colors.Reset}{Colors.Dim}]{Colors.Reset} {message}");

    public static void Info(string message) =>
        Console.WriteLine($"{Colors.Dim}[{Colors.Reset}{Colors.Bold}{Colors.Blue}info{Colors.Reset}{Colors.Dim}]{Colors.Reset} {message}");

    public static void OutputResult(CompilationResult result)
    {
        var diagnosticInfo = result.Files
            .Where(f => !f.SourceFile.IsDeclaration)
            .Select(f => f.Diagnostics.WithoutInfo().ToString())
            .Where(diagnostics => !string.IsNullOrEmpty(diagnostics));

        var failureInfo = result.Failures.Count == 0
            ? []
            : new[]
            {
                $"Not compiled: {string.Join(", ", result.Failures.Select(failure => failure.File.Name))}",
                DiagnosticBag.Concat(result.Failures.ConvertAll(failure => failure.Diagnostics)).WithoutInfo().ToString()
            };

        var lines = diagnosticInfo.Concat(failureInfo).ToList();
        if (lines.Count > 0)
            Console.WriteLine(string.Join(Environment.NewLine, lines));

        var timingLine = $"Compiled in {Colors.Bold}{Colors.Green}{result.Elapsed.TotalSeconds * 1000:N0} ms{Colors.Reset}.";
        if (result.EstimatedTimeSaved > TimeSpan.Zero)
            timingLine += $" Saved {Colors.Bold}{Colors.Green}{result.EstimatedTimeSaved.TotalSeconds * 1000:N0} ms{Colors.Reset} with heuristics.";

        Info(timingLine);
    }
}