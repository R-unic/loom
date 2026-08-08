using Loom.Config;
using Loom.Core.Pipeline;

namespace Loom.Tools;

/// <summary>Dev-only helper for regenerating <c>Snapshots/Luau/*.luau</c> fixtures from their <c>.loom</c> source.</summary>
internal static class CompileTools
{
    public static void Compile(string[] arguments)
    {
        var filePath = arguments.ElementAtOrDefault(1);
        if (string.IsNullOrEmpty(filePath))
        {
            Console.WriteLine("Usage: loomtools compile <file>");
            return;
        }

        var file = FileManager.LoadSingle(filePath);
        var compilationUnit = new CompilationUnit(new LoomConfig());
        var compiler = new Compiler(compilationUnit, file);
        var compiled = compiler.Compile();
        if (compiled == null)
        {
            foreach (var diagnostic in compiler.Diagnostics.Set)
                Console.Error.WriteLine(diagnostic.Message);

            return;
        }

        // Diagnostics go to stderr so stdout is the Luau alone, and regenerating a fixture is a redirect.
        foreach (var diagnostic in compiled.Diagnostics.Set)
            Console.Error.WriteLine($"[{diagnostic.Severity}] {diagnostic.Message}");

        Console.Write(compiled.RenderedLuau);
    }
}
