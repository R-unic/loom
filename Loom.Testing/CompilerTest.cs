using Loom.Config;
using Loom.Core;
using Loom.Core.Diagnostics;
using Loom.Core.Pipeline;

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
        var phased = new Compiler(compilationUnit, Utility.TestFile(source));
        var parsedFile = phased.Parse();
        Assert.NotNull(parsedFile);

        Assert.Equal(oneShot.RenderedLuau, phased.Analyze(parsedFile).RenderedLuau);
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
        Assert.False(file.SemanticModel.DisableRuntimeLibraryImport);
        Utility.AssertNoErrors(file.Diagnostics);

        return file;
    }
}