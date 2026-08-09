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
using Loom.Luau.AST;
using Resolver = Loom.Core.Resolving.Resolver;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.Testing;

internal static class Utility
{
    public static readonly LocationSpan Span = LocationSpan.Empty(TestFile(""));

    public static IReadOnlyList<Token> GetTokens(string source, bool withTrivia = false) => withTrivia ? Tokenize(source).TokensWithTrivia : Tokenize(source).Tokens;
    public static Tree GetAST(string source) => Parse(source).Tree;
    public static Type GetLastStatementType(string source) => TypeCheck(source).ReturnType;

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
        var result = FlowAnalyze(source, disableRuntimeLib);
        if (typeCheck)
        {
            var typeChecker = new TypeChecker(result.SemanticModel, result.Analyzer);
            typeChecker.Check();
        }

        return new LuauGenerator(result.SemanticModel).Generate();
    }

    public static DiagnosticBag GetLexerDiagnostics(string source) => Tokenize(source).Diagnostics;
    public static ParserResult Parse(string source) => new Parser(Tokenize(source)).Parse();
    public static DiagnosticBag GetParserDiagnostics(string source) => Parse(source).Diagnostics;

    public static SemanticModel GetSemanticModel(
        string source,
        bool isDeclaration = false,
        bool disableRuntimeLib = true,
        ProjectType projectType = ProjectType.Game)
    {
        var parserResult = Parse(source);
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
        ProjectType projectType = ProjectType.Game)
    {
        var semanticModel = GetSemanticModel(source, disableRuntimeLib: disableRuntimeLib, projectType: projectType);
        var flowAnalyzer = new FlowAnalyzer(semanticModel);
        var result = flowAnalyzer.Analyze();
        return (result, semanticModel, flowAnalyzer);
    }

    private static TypeChecker GetTypeChecker(string source, ProjectType projectType = ProjectType.Game)
    {
        var (_, semanticModel, flowAnalyzer) = FlowAnalyze(source, projectType: projectType);
        return new TypeChecker(semanticModel, flowAnalyzer);
    }

    public static TypeCheckerResult TypeCheck(string source, ProjectType projectType = ProjectType.Game) => GetTypeChecker(source, projectType).Check();

    public static DiagnosticBag GetTypeCheckerDiagnostics(string source, ProjectType projectType = ProjectType.Game) =>
        TypeCheck(source, projectType).Diagnostics;

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

            var config = ConfigReader.LocateFromDirectory(directory);
            Assert.NotNull(config);
            config.NoEmit = true;

            var compilationUnit = new CompilationUnit(config, diagnosticOptions);
            assert(compilationUnit, compilationUnit.Compile());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static LexerResult Tokenize(string source) => new Lexer(TestFile(source)).Tokenize();
}