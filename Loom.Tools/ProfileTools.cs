using System.Diagnostics;
using Loom.Config;
using Loom.Core.FlowAnalysis;
using Loom.Core.Generation;
using Loom.Core.Lexing;
using Loom.Core.Parsing;
using Loom.Core.Pipeline;
using Loom.Core.Resolving;
using Loom.Core.TypeChecking;

namespace Loom.Tools;

internal static class ProfileTools
{
    public static void Profile(string[] arguments)
    {
        var filePath = arguments.ElementAtOrDefault(1);
        if (string.IsNullOrEmpty(filePath))
        {
            Console.WriteLine("Usage: loomtools profile <file> [iterations]");
            return;
        }

        var iterations = int.TryParse(arguments.ElementAtOrDefault(2), out var n) ? n : 1;
        var file = FileManager.LoadSingle(filePath);

        // Use ProjectType.Plugin so Intrinsics.Register auto-injects PluginSecurity.loom
        // instead of None.loom, avoiding a duplicate-symbol collision when the profiled
        // file itself is None.loom (which is otherwise always auto-injected as intrinsics).
        var config = new LoomConfig { NoEmit = true, ProjectType = ProjectType.Plugin };
        var unit = new CompilationUnit(config);

        for (var i = 0; i < iterations; i++)
        {
            var total = Stopwatch.StartNew();

            var sw = Stopwatch.StartNew();
            var lexer = new Lexer(file);
            var lexerResult = lexer.Tokenize();
            var lexMs = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var parser = new Parser(lexerResult);
            var parserResult = parser.Parse();
            var parseMs = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var resolver = new Resolver(parserResult, unit);
            var semanticModel = resolver.Resolve();
            var resolveMs = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var flowAnalyzer = new FlowAnalyzer(semanticModel);
            flowAnalyzer.Analyze();
            var flowMs = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var typeChecker = new TypeChecker(semanticModel, flowAnalyzer);
            var typeCheckerResult = typeChecker.Check();
            var typeCheckMs = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var generator = new LuauGenerator(semanticModel, typeCheckerDiagnostics: typeCheckerResult.Diagnostics);
            var generatorResult = generator.Generate();
            var generateMs = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            generatorResult.LuauTree.Render();
            var renderMs = sw.Elapsed.TotalMilliseconds;

            var totalMs = total.Elapsed.TotalMilliseconds;
            Console.WriteLine(
                $"[{i}] tokens={lexerResult.Tokens.Count} lex={lexMs:F1}ms parse={parseMs:F1}ms resolve={resolveMs:F1}ms flow={flowMs:F1}ms typecheck={typeCheckMs:F1}ms generate={generateMs:F1}ms render={renderMs:F1}ms TOTAL={totalMs:F1}ms"
            );
        }
    }
}