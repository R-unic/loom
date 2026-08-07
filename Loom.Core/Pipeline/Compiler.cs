using Loom.Core.Diagnostics;
using Loom.Core.FlowAnalysis;
using Loom.Core.Generation;
using Loom.Core.Lexing;
using Loom.Core.Parsing;
using Loom.Core.Resolving;
using Loom.Core.Text;
using Loom.Core.TypeChecking;

namespace Loom.Core.Pipeline;

public sealed class Compiler(CompilationUnit unit, SourceFile file)
{
    private readonly List<DiagnosticBag> _pipelineDiagnostics = [];

    public SourceFile SourceFile { get; } = file;

    /// <summary>
    ///     Everything reported against the file so far. Unlike <see cref="CompiledFile.Diagnostics" /> this is
    ///     readable after a phase failed, which is the only place the compiler error of that failure lives.
    /// </summary>
    public DiagnosticBag Diagnostics => Reported(DiagnosticBag.Concat(_pipelineDiagnostics, Options));

    /// <summary>Reporting behavior of this file's stages, which is the unit's except for a dependency's files.</summary>
    private DiagnosticOptions Options => field ??= unit.DiagnosticOptionsFor(SourceFile);

    /// <summary>Null when a phase failed; <see cref="Diagnostics" /> says why.</summary>
    public CompiledFile? Compile()
    {
        var parsedFile = Parse();
        return parsedFile == null ? null : Analyze(parsedFile);
    }

    /// <summary>
    ///     Phase one: lex and parse. Runs for every file in the unit before any file is analyzed, so
    ///     that module dependencies can be resolved from the parsed trees and analyzed in order.
    /// </summary>
    public ParsedFile? Parse() =>
        RunPhase(() =>
            {
                var lexer = new Lexer(SourceFile, Options);
                var lexerResult = TrackDiagnostics(lexer.Tokenize());
                var parser = new Parser(lexerResult);
                var parserResult = TrackDiagnostics(parser.Parse());

                return new ParsedFile(SourceFile, lexerResult, parserResult);
            }
        );

    /// <summary>
    ///     Phase two: everything after the parser. Diagnostics from <see cref="Parse" /> are carried over, as
    ///     are <paramref name="moduleDiagnostics" /> from building the unit's module graph, so the returned
    ///     file reports every diagnostic raised against it regardless of which phase found it.
    /// </summary>
    /// <returns>Null when the phase failed; <see cref="Diagnostics" /> says why.</returns>
    public CompiledFile? Analyze(ParsedFile parsedFile, DiagnosticBag? moduleDiagnostics = null) =>
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

                return new CompiledFile(SourceFile)
                {
                    Root = unit.Roots.Of(SourceFile),
                    Path = unit.Roots.OutputPathOf(SourceFile),
                    Diagnostics = Reported(DiagnosticBag.Concat(_pipelineDiagnostics, Options)),
                    RenderedLuau = renderedLuau,
                    LuauTree = generatorResult.LuauTree,
                    ReturnType = typeCheckerResult.ReturnType,
                    SemanticModel = semanticModel,
                    Tree = parsedFile.Tree,
                    Tokens = parsedFile.LexerResult.Tokens
                };
            }
        );

    /// <remarks>
    ///     The compiler error of a failed phase is tracked like any other stage's diagnostics rather than
    ///     reported into a bag that goes nowhere, so it survives in <see cref="Diagnostics" /> for the caller
    ///     to report. Nothing else can carry it: the phase has no result to attach it to.
    /// </remarks>
    private T? RunPhase<T>(Func<T> phase)
        where T : class
    {
        try
        {
            return phase();
        }
        catch (Exception e)
        {
            var diagnostics = new DiagnosticBag(options: Options);
            _pipelineDiagnostics.Add(diagnostics);
            diagnostics.CompilerError(SourceFile, $"The compiler threw an exception!\n{e.Message}\n{e.StackTrace}");
            return null;
        }
    }

    /// <summary>
    ///     What the build reports for this file: its own diagnostics when the file is the entry project's, and
    ///     one error naming the package when it belongs to a dependency the user is only consuming.
    /// </summary>
    private DiagnosticBag Reported(DiagnosticBag diagnostics) =>
        unit.PackageAttributionOf(SourceFile) is { } package ? diagnostics.AttributedTo(package, unit.DiagnosticOptions) : diagnostics;

    private T TrackDiagnostics<T>(T result)
        where T : DiagnosedResult
    {
        _pipelineDiagnostics.Add(result.Diagnostics);
        return result;
    }
}