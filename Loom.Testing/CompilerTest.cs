using Loom.Config;
using Loom.Core.Diagnostics;
using Loom.Core.Pipeline;
using Loom.Core.Text;

namespace Loom.Testing;

[Collection("Assembly")]
public class CompilerTest
{
    public static readonly IEnumerable<TheoryDataRow<string, string>> SnapshotFiles = Utility.GetSnapshotFiles("Luau", ".luau");

    [Theory]
    [MemberData(nameof(SnapshotFiles))]
    public void Compiles_Snapshots(string sourcePath, string snapshotPath)
    {
        var source = File.ReadAllText(sourcePath);
        var snapshot = File.ReadAllText(snapshotPath);
        AssertCompiled(source, snapshot);
    }

    [Fact]
    public void Parses_WithoutAnalyzing()
    {
        var parsedFile = Parse("let x = 1;");

        Assert.Single(parsedFile.Tree.Statements);
        Assert.NotEmpty(parsedFile.LexerResult.Tokens);
        Utility.AssertNoErrors(parsedFile.ParserResult.Diagnostics);
        Assert.Empty(parsedFile.Imports);
    }

    [Fact]
    public void Parses_TopLevelImports_InSourceOrder()
    {
        var parsedFile = Parse(
            """
            import { square } from "./math"
            let x = 1;
            import type { Vector } from "./vector"
            """
        );

        Assert.Equal(["./math", "./vector"], parsedFile.Imports.Select(import => import.ModulePath));
        Assert.Equal([false, true], parsedFile.Imports.Select(import => import.IsTypeOnly));
    }

    [Fact]
    public void Analyzes_ParsedFile_CarryingParserDiagnostics()
    {
        var compilationUnit = new CompilationUnit(new LoomConfig());
        var compiler = new Compiler(compilationUnit, Utility.TestFile("import { } from \"./math\""));

        var parsedFile = compiler.Parse();
        Assert.NotNull(parsedFile);

        var compiledFile = compiler.Analyze(parsedFile);
        Assert.NotNull(compiledFile);
        Utility.AssertDiagnostic(compiledFile.Diagnostics, InternalCodes.EmptyImportClause, "Import declaration must name at least one member.");
        Assert.Same(parsedFile.Tree, compiledFile.Tree);
        Assert.Equal(parsedFile.LexerResult.Tokens, compiledFile.Tokens);
    }

    [Fact]
    public void Compiles_EquivalentToParseThenAnalyze()
    {
        const string source = "let x = 1 + 2;";
        var compilationUnit = new CompilationUnit(new LoomConfig());

        var oneShot = new Compiler(compilationUnit, Utility.TestFile(source)).Compile();
        Assert.NotNull(oneShot);

        var phased = new Compiler(compilationUnit, Utility.TestFile(source));
        var parsedFile = phased.Parse();
        Assert.NotNull(parsedFile);

        var analyzed = phased.Analyze(parsedFile);
        Assert.NotNull(analyzed);
        Assert.Equal(oneShot.RenderedLuau, analyzed.RenderedLuau);
    }

    [Fact]
    public void Compiles_WithTheUnitsDiagnosticOptions_AtEveryStage()
    {
        var options = new DiagnosticOptions();
        var compilationUnit = new CompilationUnit(new LoomConfig(), options);
        var compiler = new Compiler(compilationUnit, Utility.TestFile("let x = 1;"));

        var parsedFile = compiler.Parse();
        Assert.NotNull(parsedFile);
        Assert.Same(options, parsedFile.LexerResult.Diagnostics.Options);
        Assert.Same(options, parsedFile.ParserResult.Diagnostics.Options);

        var compiledFile = compiler.Analyze(parsedFile);
        Assert.NotNull(compiledFile);
        Assert.Same(options, compiledFile.Diagnostics.Options);
        Assert.Same(options, compiledFile.SemanticModel.Diagnostics.Options);
    }

    /// <remarks>
    ///     What a host with no source directory to read needs: the playground compiles a buffer it already
    ///     holds, so handing the unit its files is the only way in.
    /// </remarks>
    [Fact]
    public void Compiles_TheSourceFiles_ItWasHanded()
    {
        var config = new LoomConfig { ProjectDirectory = string.Empty, NoEmit = true };
        var sourceFile = new SourceFile(Path.Combine("src", "playground.loom"), "let x = 1;");
        var compilationUnit = new CompilationUnit(config, [sourceFile]);

        Assert.Equal([sourceFile], compilationUnit.SourceFiles);

        var result = compilationUnit.Compile();
        Utility.AssertNoErrors(result.Diagnostics);
        Assert.NotEmpty(result.Files);
    }

    /// <remarks>
    ///     A source directory of "" makes the output path of any file throw, which is a stage failing on
    ///     something other than the file it was given — exactly what the compiler-error path is for.
    /// </remarks>
    [Fact]
    public void Reports_AFailedPhase_AsACompilerError_InsteadOfThrowing()
    {
        var config = new LoomConfig { Files = new FilesConfig { SourceDirectory = "" } };
        var compiler = new Compiler(new CompilationUnit(config), Utility.TestFile("let x = 1;"));

        Assert.Null(compiler.Compile());

        var compilerError = compiler.Diagnostics.Find(diagnostic => diagnostic.Code == InternalCodes.CompilerError);
        Assert.NotNull(compilerError);
        Assert.Contains("The compiler threw an exception!", compilerError.Message);
        Assert.Equal("this is a compiler bug! please report an issue.", compilerError.Hint);
    }

    private static ParsedFile Parse(string source)
    {
        var compilationUnit = new CompilationUnit(new LoomConfig());
        var parsedFile = new Compiler(compilationUnit, Utility.TestFile(source)).Parse();
        Assert.NotNull(parsedFile);

        return parsedFile;
    }

    private static void AssertCompiled(string source, string expected) =>
        Assert.Equal(expected.Replace(Environment.NewLine, "\n") + '\n', Compile(source).RenderedLuau.Replace(Environment.NewLine, "\n"));

    private static CompiledFile Compile(string source)
    {
        var compilationUnit = new CompilationUnit(new LoomConfig());
        var compiler = new Compiler(compilationUnit, Utility.TestFile(source));
        var file = compiler.Compile();
        Assert.NotNull(file);
        Assert.False(file.SemanticModel.DisableRuntimeLibraryImport);
        Utility.AssertNoErrors(file.Diagnostics);

        return file;
    }
}