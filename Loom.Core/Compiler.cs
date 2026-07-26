using Loom.Core.Diagnostics;
using Loom.Core.FlowAnalysis;
using Loom.Core.Generation;
using Loom.Core.Lexing;
using Loom.Core.Parsing;
using Loom.Core.Resolving;
using Loom.Core.Text;
using Loom.Core.TypeChecking;

namespace Loom.Core;

public sealed class Compiler(CompilationUnit unit, SourceFile file)
{
    private readonly List<DiagnosticBag> _pipelineDiagnostics = [];

    public CompiledFile Compile()
    {
        var parsedFile = Parse();
        return parsedFile == null ? null! : Analyze(parsedFile);
    }

    /// <summary>
    ///     Phase one: lex and parse. Runs for every file in the unit before any file is analyzed, so
    ///     that module dependencies can be resolved from the parsed trees and analyzed in order.
    /// </summary>
    public ParsedFile? Parse() =>
        RunPhase(() =>
            {
                var lexer = new Lexer(file);
                var lexerResult = TrackDiagnostics(lexer.Tokenize());
                var parser = new Parser(lexerResult);
                var parserResult = TrackDiagnostics(parser.Parse());

                return new ParsedFile(file, lexerResult, parserResult);
            }
        );

    /// <summary>
    ///     Phase two: everything after the parser. Diagnostics from <see cref="Parse" /> are carried over, as
    ///     are <paramref name="moduleDiagnostics" /> from building the unit's module graph, so the returned
    ///     file reports every diagnostic raised against it regardless of which phase found it.
    /// </summary>
    public CompiledFile Analyze(ParsedFile parsedFile, DiagnosticBag? moduleDiagnostics = null) =>
        RunPhase(() =>
            {
                if (moduleDiagnostics != null)
                    _pipelineDiagnostics.Add(moduleDiagnostics);

                var resolver = new Resolver(parsedFile.ParserResult, unit);
                var semanticModel = TrackDiagnostics(resolver.Resolve());
                var flowAnalyzer = new FlowAnalyzer(semanticModel);
                TrackDiagnostics(flowAnalyzer.Analyze());
                var typeChecker = new TypeChecker(semanticModel, flowAnalyzer);
                var typeCheckerResult = TrackDiagnostics(typeChecker.Check());
                var generator = new LuauGenerator(semanticModel, unit.RuntimeImport, unit.ModuleRequirePaths);
                var generatorResult = TrackDiagnostics(generator.Generate());
                var renderedLuau = generatorResult.LuauTree.Render();

                return new CompiledFile(file)
                {
                    Path = FileManager.GetOutputPath(file, unit.Config),
                    Diagnostics = DiagnosticBag.Concat(_pipelineDiagnostics),
                    RenderedLuau = renderedLuau,
                    LuauTree = generatorResult.LuauTree,
                    ReturnType = typeCheckerResult.ReturnType,
                    SemanticModel = semanticModel,
                    Tree = parsedFile.Tree,
                    Tokens = parsedFile.LexerResult.Tokens
                };
            }
        )!;

    private T? RunPhase<T>(Func<T> phase)
        where T : class
    {
        try
        {
            return phase();
        }
        catch (Exception e)
        {
            var diagnostics = DiagnosticBag.Concat(_pipelineDiagnostics);
            DiagnosticBag.FailFast = true;
            diagnostics.CompilerError(file, $"The compiler threw an exception!\n{e.Message}\n{e.StackTrace}");
            return null;
        }
    }

    private T TrackDiagnostics<T>(T result)
        where T : DiagnosedResult
    {
        _pipelineDiagnostics.Add(result.Diagnostics);
        return result;
    }
}