using Loom.Config;
using Loom.Core.Diagnostics;
using Loom.Core.FlowAnalysis;
using Loom.Core.Generation;
using Loom.Core.Lexing;
using Loom.Core.Parsing;
using Loom.Core.Parsing.AST;
using Loom.Core.Pipeline;
using Loom.Core.Resolving;
using Loom.Core.Text;
using Loom.Core.TypeChecking;
using Loom.LanguageServer;
using Loom.Luau.AST;
using OmniSharp.Extensions.LanguageServer.Protocol;
using Resolver = Loom.Core.Resolving.Resolver;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.Testing;

internal static class Utility
{
    public static readonly LocationSpan Span = LocationSpan.Empty(TestFile(""));

    public static IReadOnlyList<Token> GetTokens(string source, bool withTrivia = false) => withTrivia ? Tokenize(source).TokensWithTrivia : Tokenize(source).Tokens;
    public static Tree GetAST(string source) => Parse(source).Tree;

    /// <summary>
    ///     The type of <paramref name="source" />'s last statement. Source that does not lex and parse
    ///     cleanly fails here rather than answering: the parser's recovery node types as 'never', which a
    ///     test asserting only the type would otherwise read as a real inference result.
    /// </summary>
    public static Type GetLastStatementType(string source)
    {
        var result = TypeCheck(source, out var upstream);
        AssertNoErrors(upstream);

        return result.ReturnType;
    }

    public static LuauTree GetLuauAST(string source, bool typeCheck = false, bool disableRuntimeLib = true) =>
        Generate(source, typeCheck, disableRuntimeLib).LuauTree;

    public static DiagnosticBag GetGeneratorDiagnostics(string source, bool typeCheck = false) => Generate(source, typeCheck).Diagnostics;

    /// <summary>
    ///     Renders <paramref name="source" /> with an ambient 'world' in scope, for the generator cases that
    ///     need a real Instance to hang the instance macros off.
    /// </summary>
    public static string GenerateAgainstWorkspace(string source)
    {
        const string declaration = "declare let world: Workspace;\n";
        AssertNoErrors(GetGeneratorDiagnostics(declaration + source, typeCheck: true));

        return GetLuauAST(declaration + source, typeCheck: true).Render();
    }

    private static LuauGeneratorResult Generate(string source, bool typeCheck = false, bool disableRuntimeLib = true)
    {
        var (_, semanticModel, flowAnalyzer) = FlowAnalyze(source, out var upstream, disableRuntimeLib);
        if (typeCheck)
        {
            var typeChecker = new TypeChecker(semanticModel, flowAnalyzer);
            upstream = DiagnosticBag.Concat([upstream, typeChecker.Check().Diagnostics]);
        }

        var result = new LuauGenerator(semanticModel).Generate();
        return result with { Diagnostics = DiagnosticBag.Concat([upstream, result.Diagnostics]) };
    }

    public static DiagnosticBag GetLexerDiagnostics(string source) => Tokenize(source).Diagnostics;
    public static ParserResult Parse(string source) => Parse(source, out _);
    public static DiagnosticBag GetParserDiagnostics(string source) => Parse(source).Diagnostics;

    /// <summary>
    ///     Parses <paramref name="source" />, handing back the lexer's and parser's diagnostics in
    ///     <paramref name="upstream" /> so a later stage's result can carry them the way
    ///     <see cref="Compiler" /> does. Without that a malformed source silently reaches the stage
    ///     under test: the parser recovers, the stage types the recovered node as <c>never</c>, and an
    ///     assertion on the stage's own bag alone sees no errors at all.
    /// </summary>
    private static ParserResult Parse(string source, out DiagnosticBag upstream)
    {
        var lexerResult = Tokenize(source);
        var parserResult = new Parser(lexerResult).Parse();
        upstream = DiagnosticBag.Concat([lexerResult.Diagnostics, parserResult.Diagnostics]);

        return parserResult;
    }

    public static SemanticModel GetSemanticModel(
        string source,
        bool isDeclaration = false,
        bool disableRuntimeLib = true,
        ProjectType projectType = ProjectType.Game) =>
        GetSemanticModel(source, out _, isDeclaration, disableRuntimeLib, projectType);

    private static SemanticModel GetSemanticModel(
        string source,
        out DiagnosticBag upstream,
        bool isDeclaration = false,
        bool disableRuntimeLib = true,
        ProjectType projectType = ProjectType.Game)
    {
        var parserResult = Parse(source, out upstream);
        if (isDeclaration)
            parserResult.Tree.File.IsDeclaration = true;

        var compilationUnit = new CompilationUnit(new LoomConfig { ProjectType = projectType });
        var semanticModel = new Resolver(parserResult, compilationUnit).Resolve();
        semanticModel.DisableRuntimeLibraryImport = disableRuntimeLib;

        return semanticModel;
    }

    public static (FlowAnalyzerResult AnalyzerResult, SemanticModel SemanticModel, FlowAnalyzer Analyzer) FlowAnalyze(
        string source,
        bool disableRuntimeLib = true,
        ProjectType projectType = ProjectType.Game) =>
        FlowAnalyze(source, out _, disableRuntimeLib, projectType);

    private static (FlowAnalyzerResult AnalyzerResult, SemanticModel SemanticModel, FlowAnalyzer Analyzer) FlowAnalyze(
        string source,
        out DiagnosticBag upstream,
        bool disableRuntimeLib = true,
        ProjectType projectType = ProjectType.Game)
    {
        var semanticModel = GetSemanticModel(source, out upstream, disableRuntimeLib: disableRuntimeLib, projectType: projectType);
        var flowAnalyzer = new FlowAnalyzer(semanticModel);
        var result = flowAnalyzer.Analyze();
        return (result, semanticModel, flowAnalyzer);
    }

    internal static TypeCheckerResult TypeCheck(string source, ProjectType projectType = ProjectType.Game) => TypeCheck(source, out _, projectType);

    private static TypeCheckerResult TypeCheck(string source, out DiagnosticBag upstream, ProjectType projectType = ProjectType.Game)
    {
        var (_, semanticModel, flowAnalyzer) = FlowAnalyze(source, out upstream, projectType: projectType);
        var result = new TypeChecker(semanticModel, flowAnalyzer).Check();

        return result with { Diagnostics = DiagnosticBag.Concat([upstream, result.Diagnostics]) };
    }

    public static DiagnosticBag GetTypeCheckerDiagnostics(string source, ProjectType projectType = ProjectType.Game) =>
        TypeCheck(source, projectType).Diagnostics;

    /// <summary>
    ///     Everything the stages after the parser report for <paramref name="source" />, merged into one bag
    ///     the way a build shows them. A name every stage looks up is only reported once from any single
    ///     stage's bag, so proving it is not reported twice takes all of them together.
    /// </summary>
    public static DiagnosticBag GetAnalysisDiagnostics(string source, ProjectType projectType = ProjectType.Game)
    {
        var (analyzerResult, semanticModel, flowAnalyzer) = FlowAnalyze(source, projectType: projectType);
        var typeCheckerDiagnostics = new TypeChecker(semanticModel, flowAnalyzer).Check().Diagnostics;
        var generatorDiagnostics = new LuauGenerator(semanticModel).Generate().Diagnostics;

        return DiagnosticBag.Concat([semanticModel.Diagnostics, analyzerResult.Diagnostics, typeCheckerDiagnostics, generatorDiagnostics]);
    }

    /// <summary>Asserts that exactly one diagnostic in <paramref name="diagnostics" /> mentions <paramref name="name" />.</summary>
    public static void AssertReportedOnce(DiagnosticBag diagnostics, string name)
    {
        var mentioning = diagnostics.Set.Where(diagnostic => diagnostic.Message.Contains($"'{name}'")).ToList();
        Assert.Single(mentioning);
    }

    public static Token IdentifierToken(string name, LocationSpan? span = null) => Token(SyntaxKind.Identifier, name, span);
    private static Token Token(SyntaxKind kind, string text, LocationSpan? span = null) => new(kind, span ?? Span, text);

    public static T AssertNoErrors<T>(T result)
        where T : DiagnosedResult
    {
        AssertNoErrors(result.Diagnostics);
        return result;
    }

    public static void AssertNoErrors(DiagnosticBag diagnostics) => Assert.DoesNotContain(diagnostics.Set, d => d.Severity == DiagnosticSeverity.Error);

    public static void AssertDiagnostic(DiagnosticBag diagnostics, string code, string message, string? hint = null)
    {
        var diagnostic = diagnostics.Find(d => d.Code == code);
        Assert.NotNull(diagnostic);
        Assert.Equal(message, diagnostic.Message);

        if (hint == null) return;
        Assert.Equal(hint, diagnostic.Hint);
    }

    public static IEnumerable<TheoryDataRow<string, string>> GetSnapshotFiles(string folderName, string targetExtension) =>
        Directory.EnumerateFiles(AssemblyFixture.Snapshots + '/' + folderName, $"*{FileManager.LoomExtension}")
            .Select(path => new TheoryDataRow<string, string>(path, path.Replace(FileManager.LoomExtension, targetExtension)));

    public static SourceFile TestFile(string source) => new("test", source);

    /// <summary>
    ///     Compiles a throwaway project whose source directory holds <paramref name="files" />, keyed by path
    ///     relative to that directory so nested modules can be written as <c>"util/init.loom"</c>.
    /// </summary>
    public static void WithTempProject(
        IEnumerable<(string Path, string Source)> files,
        Action<CompilationUnit, CompilationResult> assert,
        string? rojoProject = null,
        DiagnosticOptions? diagnosticOptions = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "loom-test-" + Guid.NewGuid());
        var sourceDirectory = Path.Combine(directory, "src");
        Directory.CreateDirectory(sourceDirectory);
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "loom-config.toml"),
                "project_type = \"game\"\n[files]\nsource_directory = \"src\"\noutput_directory = \"dist\"\n"
            );

            if (rojoProject != null)
                File.WriteAllText(Path.Combine(directory, RojoResolver.ProjectFileName), rojoProject);

            foreach (var (path, source) in files)
            {
                var filePath = Path.Combine(sourceDirectory, path);
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                File.WriteAllText(filePath, source);
            }

            // emits for real: the output goes into the throwaway directory this deletes on the way out, and
            // 'no_emit' now also skips generating the Luau these cases assert on
            var config = ConfigReader.LocateFromDirectory(directory);
            Assert.NotNull(config);

            var compilationUnit = new CompilationUnit(config, diagnosticOptions);
            assert(compilationUnit, compilationUnit.Compile());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    ///     Opens <paramref name="source" /> as <c>src/main.loom</c> of a throwaway project in a document store,
    ///     alongside any <paramref name="otherFiles" /> the case needs, and hands both to <paramref name="act" />.
    ///     Language server requests are answered from a real compilation, so they need a project on disk.
    /// </summary>
    public static async Task WithLspProjectAsync(
        Func<DocumentStore, DocumentUri, Task> act,
        string source,
        params (string Path, string Source)[] otherFiles)
    {
        var directory = Path.Combine(Path.GetTempPath(), "loom-lsp-test-" + Guid.NewGuid());
        var sourceDirectory = Path.Combine(directory, "src");
        Directory.CreateDirectory(sourceDirectory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "loom-config.toml"), "[files]\nsource_directory = \"src\"\noutput_directory = \"dist\"\n");
            foreach (var (path, otherSource) in otherFiles)
            {
                var filePath = Path.Combine(sourceDirectory, path);
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                await File.WriteAllTextAsync(filePath, otherSource);
            }

            var mainPath = Path.Combine(sourceDirectory, "main.loom");
            await File.WriteAllTextAsync(mainPath, source);

            var store = new DocumentStore();
            var uri = DocumentUri.FromFileSystemPath(mainPath);
            store.Open(uri, source);

            await act(store, uri);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static LexerResult Tokenize(string source) => new Lexer(TestFile(source)).Tokenize();
}