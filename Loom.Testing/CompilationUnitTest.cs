using Loom.Config;
using Loom.Core.Diagnostics;
using Loom.Core.Pipeline;
using Loom.Luau.AST;
using BinaryOperator = Loom.Core.Parsing.AST.BinaryOperator;
using ExpressionStatement = Loom.Core.Parsing.AST.ExpressionStatement;
using PrimitiveType = Loom.Core.TypeChecking.Types.PrimitiveType;

namespace Loom.Testing;

[Collection("Assembly")]
public class CompilationUnitTest
{
    [Fact]
    public void Compiles_Project_NoEmit()
    {
        var config = GetConfig();
        config.NoEmit = true;

        var compilationUnit = new CompilationUnit(config);
        var result = compilationUnit.Compile();
        Utility.AssertNoErrors(result);
        Assert.Single(result.Files);

        var path = config.Files.OutputDirectory;
        Directory.Delete(path, true);
        Directory.CreateDirectory(path);
        File.Create(Path.Combine(path, ".gitkeep")).Dispose();

        var luauFiles = Directory.EnumerateFiles(path, "*.luau", SearchOption.TopDirectoryOnly);
        Assert.Empty(luauFiles);
    }

    [Fact]
    public void Compiles_Project()
    {
        var config = GetConfig();
        var compilationUnit = new CompilationUnit(config);
        var result = compilationUnit.Compile();
        Utility.AssertNoErrors(result);
        Assert.Single(result.Files);

        var file = result.Files.Find(file => file.Path.EndsWith("basic_binary.luau"));
        Assert.NotNull(file);
        Assert.Equal(4, file.Tokens.Count);
        Assert.Single(file.Tree.Statements);
        Assert.IsType<BinaryOperator>(Assert.IsType<ExpressionStatement>(file.Tree.Statements.First()).Expression);
        Assert.Null(file.SemanticModel.GetSymbol(file.Tree));
        Assert.Equal(PrimitiveType.Number, file.ReturnType);
        Assert.Single(file.LuauTree.Statements);

        var variable = Assert.IsType<ConstVariable>(file.LuauTree.Statements.First());
        var binary = Assert.IsType<Luau.AST.BinaryOperator>(variable.Initializer);
        Assert.Equal("_", variable.Name);
        Assert.IsType<NumberLiteral>(binary.Left);
        Assert.IsType<NumberLiteral>(binary.Right);

        var path = config.Files.OutputDirectory;
        Directory.Delete(path, true);
        Directory.CreateDirectory(path);
        File.Create(Path.Combine(path, ".gitkeep")).Dispose();
    }

    [Fact]
    public void Compiles_Project_WithDeclarationFile_PopulatesGlobals()
    {
        var dir = Path.Combine(Path.GetTempPath(), "loom-test-" + Guid.NewGuid());
        var srcDir = Path.Combine(dir, "src");
        Directory.CreateDirectory(srcDir);
        try
        {
            File.WriteAllText(
                Path.Combine(dir, "loom-config.toml"),
                "project_type = \"game\"\n[files]\nsource_directory = \"src\"\noutput_directory = \"dist\"\n"
            );

            File.WriteAllText(Path.Combine(srcDir, "types.d.loom"), "declare let global_number: number;");
            File.WriteAllText(Path.Combine(srcDir, "main.loom"), "let x = 1;");

            var config = ConfigReader.LocateFromDirectory(dir);
            Assert.NotNull(config);
            config.NoEmit = true;

            var compilationUnit = new CompilationUnit(config);
            var result = compilationUnit.Compile();

            Utility.AssertNoErrors(result);
            Assert.Equal(2, result.Files.Count);
            var main = result.Files.Single(file => file.SourceFile.Name == "main.loom");
            Assert.Contains(compilationUnit.Globals.Of(main.SourceFile).Keys, symbol => symbol.Name == "global_number");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <remarks>
    ///     Which of the two files is reported depends on the order they were analyzed in, so this pins the
    ///     pair rather than either side of it: the diagnostic sits in one of them and names the other.
    /// </remarks>
    [Fact]
    public void Reports_AnAmbientName_DeclaredTwiceInOneRoot() =>
        Utility.WithTempProject(
            [("first.d.loom", "declare let version: number;"), ("second.d.loom", "declare let version: number;"), ("main.loom", "print(version);")],
            (_, result) =>
            {
                var diagnostic = Assert.Single(result.Diagnostics.Set, diagnostic => diagnostic.Code == InternalCodes.DuplicateGlobal);
                var named = diagnostic.Span.File.Name == "first.d.loom" ? "second.d.loom" : "first.d.loom";

                Assert.Equal($"'version' is already declared by '{named}'.", diagnostic.Message);
                Assert.Equal(
                    "a project's declaration files share one ambient scope, so each name may only be declared once across them",
                    diagnostic.Hint
                );
            }
        );

    /// <remarks>Types and values are looked up separately, so one name in each namespace is two names.</remarks>
    [Fact]
    public void Allows_AnAmbientType_AndAnAmbientValue_OfOneName() =>
        Utility.WithTempProject(
            [
                ("values.d.loom", "declare let version: number;"),
                ("types.d.loom", "type version = string;"),
                ("main.loom", "let name: version = \"1.0\";\nprint(name, version);")
            ],
            (_, result) => Utility.AssertNoErrors(result)
        );

    [Fact]
    public void Compiles_EveryFile_WhenAnotherFileHasDiagnostics() =>
        Utility.WithTempProject(
            [("bad.loom", "import { } from \"./math\""), ("good.loom", "let x = 1;")],
            (_, result) =>
            {
                Assert.Equal(2, result.Files.Count);
                Utility.AssertDiagnostic(result.Diagnostics, InternalCodes.EmptyImportClause, "Import declaration must name at least one member.");

                var good = Assert.Single(result.Files, file => file.SourceFile.Name == "good.loom");
                Assert.Contains("const x = 1", good.RenderedLuau);
            }
        );

    [Fact]
    public void Compiles_ASingleFile_ReportingItsImportsAsUnresolvable()
    {
        var directory = Path.Combine(Path.GetTempPath(), "loom-test-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(directory, "src"));
        try
        {
            File.WriteAllText(Path.Combine(directory, "loom-config.toml"), "[files]\nsource_directory = \"src\"\noutput_directory = \"dist\"\n");

            var path = Path.Combine(directory, "src", "main.loom");
            File.WriteAllText(path, "import { pi } from \"./math\"\nprint(pi);");

            var config = ConfigReader.LocateFromDirectory(directory);
            Assert.NotNull(config);
            config.NoEmit = true;

            // the file is the whole unit, so nothing can satisfy the import — saying so beats binding it to
            // nothing, which is what a compile without a module graph used to do
            var compiled = new CompilationUnit(config).Compile(FileManager.LoadSingle(path));
            Assert.NotNull(compiled);
            Utility.AssertDiagnostic(compiled.Diagnostics, InternalCodes.ModuleNotFound, "Could not find module './math'.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <remarks>
    ///     Dropping the output directory after the unit has loaded its files makes every output path throw,
    ///     which stands in for any stage failing: the unit has to report it rather than let the exception out.
    /// </remarks>
    [Fact]
    public void Reports_FilesTheCompilerGaveUpOn_InsteadOfThrowing()
    {
        var directory = Path.Combine(Path.GetTempPath(), "loom-test-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(directory, "src"));
        try
        {
            File.WriteAllText(Path.Combine(directory, "loom-config.toml"), "[files]\nsource_directory = \"src\"\noutput_directory = \"dist\"\n");
            File.WriteAllText(Path.Combine(directory, "src", "main.loom"), "let x = 1;");

            var config = ConfigReader.LocateFromDirectory(directory);
            Assert.NotNull(config);
            config.NoEmit = true;

            var compilationUnit = new CompilationUnit(config);
            config.Files.OutputDirectory = null!;

            var result = compilationUnit.Compile();
            Assert.Empty(result.Files);

            var failure = Assert.Single(result.Failures);
            Assert.Equal("main.loom", failure.File.Name);

            var compilerError = failure.Diagnostics.Find(diagnostic => diagnostic.Code == InternalCodes.CompilerError);
            Assert.NotNull(compilerError);
            Assert.Contains(compilerError, result.Diagnostics.Set);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <remarks>
    ///     Emptying the source directory after the unit has loaded its files makes the module resolver throw
    ///     while the graph is built, which is a failure of the unit rather than of any one file.
    /// </remarks>
    [Fact]
    public void Reports_EveryFile_WhenTheModuleGraphCannotBeBuilt()
    {
        var directory = Path.Combine(Path.GetTempPath(), "loom-test-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(directory, "src"));
        try
        {
            File.WriteAllText(Path.Combine(directory, "loom-config.toml"), "[files]\nsource_directory = \"src\"\noutput_directory = \"dist\"\n");
            File.WriteAllText(Path.Combine(directory, "src", "main.loom"), "import { helper } from \"./util\"\nprint(helper);");
            File.WriteAllText(Path.Combine(directory, "src", "util.loom"), "export let helper = 1;");

            var config = ConfigReader.LocateFromDirectory(directory);
            Assert.NotNull(config);
            config.NoEmit = true;

            var compilationUnit = new CompilationUnit(config);
            config.Files.SourceDirectory = "";

            var result = compilationUnit.Compile();
            Assert.Empty(result.Files);
            Assert.Equal(["main.loom", "util.loom"], result.Failures.Select(failure => failure.File.Name).Order());

            // one error for the unit, not one per file
            var compilerError = Assert.Single(result.Diagnostics.Set, diagnostic => diagnostic.Code == InternalCodes.CompilerError);
            Assert.Contains("module graph", compilerError.Message);
            Assert.All(result.Failures, failure => Assert.Contains(compilerError, failure.Diagnostics.Set));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Compiles_WithTheUnitsDiagnosticOptions_IncludingModuleDiagnostics()
    {
        var options = new DiagnosticOptions();
        Utility.WithTempProject(
            [("main.loom", "import { square } from \"./missing\"")],
            (unit, result) =>
            {
                Assert.Same(options, unit.DiagnosticOptions);
                Assert.Same(options, result.Diagnostics.Options);

                var file = Assert.Single(result.Files);
                Assert.Same(options, file.Diagnostics.Options);

                var moduleDiagnostics = unit.ModuleGraph?.GetDiagnostics(file.SourceFile);
                Assert.NotNull(moduleDiagnostics);
                Utility.AssertDiagnostic(moduleDiagnostics, InternalCodes.ModuleNotFound, "Could not find module './missing'.");
                Assert.Same(options, moduleDiagnostics.Options);
            },
            diagnosticOptions: options
        );
    }

    private static LoomConfig GetConfig()
    {
        var config = ConfigReader.LocateFromDirectory(AssemblyFixture.Snapshots);
        Assert.NotNull(config);
        Assert.Equal(AssemblyFixture.Snapshots, config.ProjectDirectory);

        return config;
    }

    #region Recompile
    [Fact]
    public void Recompile_WithNoChanges_ReusesEveryCompiledFile() =>
        Utility.WithTempProject(
            [("math.loom", "export let value: number = 1;"), ("main.loom", "import { value } from \"./math\"\nlet doubled: number = value;")],
            (unit, first) =>
            {
                var second = unit.Recompile(new HashSet<string>());

                Utility.AssertNoErrors(second);
                Assert.Empty(second.Reanalyzed);
                Assert.True(second.EstimatedTimeSaved > TimeSpan.Zero);
                foreach (var file in first.Files)
                {
                    var reused = second.Files.Find(f => f.SourceFile.Name == file.SourceFile.Name);
                    Assert.Same(file, reused);
                }
            }
        );

    [Fact]
    public void Recompile_DependencyChange_WithUnchangedExportedShape_SkipsDependent() =>
        Utility.WithTempProject(
            [("math.loom", "export let value: number = 1;"), ("main.loom", "import { value } from \"./math\"\nlet doubled: number = value;")],
            (unit, first) =>
            {
                var mathFile = unit.SourceFiles.First(f => f.Name == "math.loom");
                var mainFileBefore = first.Files.Find(f => f.SourceFile.Name == "main.loom")!;
                File.WriteAllText(mathFile.AbsolutePath, "export let value: number = 2;");

                var second = unit.Recompile(new HashSet<string> { mathFile.AbsolutePath });

                Utility.AssertNoErrors(second);
                Assert.Contains(second.Reanalyzed, f => f.Name == "math.loom");
                Assert.DoesNotContain(second.Reanalyzed, f => f.Name == "main.loom");
                Assert.True(second.EstimatedTimeSaved > TimeSpan.Zero);

                var mainFileAfter = second.Files.Find(f => f.SourceFile.Name == "main.loom");
                Assert.Same(mainFileBefore, mainFileAfter);
            }
        );

    [Fact]
    public void Recompile_DependencyChange_WithChangedExportedShape_ReanalyzesDependent() =>
        Utility.WithTempProject(
            [("math.loom", "export let value: number = 1;"), ("main.loom", "import { value } from \"./math\"\nlet doubled: number = value;")],
            (unit, first) =>
            {
                var mathFile = unit.SourceFiles.First(f => f.Name == "math.loom");
                var mainFileBefore = first.Files.Find(f => f.SourceFile.Name == "main.loom")!;
                File.WriteAllText(mathFile.AbsolutePath, "export let value: string = \"hi\";");

                var second = unit.Recompile(new HashSet<string> { mathFile.AbsolutePath });

                Assert.Contains(second.Reanalyzed, f => f.Name == "math.loom");
                Assert.Contains(second.Reanalyzed, f => f.Name == "main.loom");

                var mainFileAfter = second.Files.Find(f => f.SourceFile.Name == "main.loom");
                Assert.NotSame(mainFileBefore, mainFileAfter);
                Utility.AssertDiagnostic(second.Diagnostics, InternalCodes.TypeMismatch, "Type 'string' is not assignable to type 'number'.");
            }
        );

    /// <summary>
    ///     A dependent keeps the compiler that parsed it across re-analyses, so an analysis has to drop what
    ///     the last one reported - otherwise fixing the module a file imports from leaves the error that fix
    ///     resolved reported against it forever.
    /// </summary>
    [Fact]
    public void Recompile_AfterADependencyIsFixed_DropsTheDependentsOldDiagnostics() =>
        Utility.WithTempProject(
            [("math.loom", "export let value: number = 1;"), ("main.loom", "import { value } from \"./math\"\nlet doubled: number = value;")],
            (unit, _) =>
            {
                var mathFile = unit.SourceFiles.First(file => file.Name == "math.loom");

                File.WriteAllText(mathFile.AbsolutePath, "export let value: string = \"hi\";");
                var broken = unit.Recompile(new HashSet<string> { mathFile.AbsolutePath });
                Utility.AssertDiagnostic(broken.Diagnostics, InternalCodes.TypeMismatch, "Type 'string' is not assignable to type 'number'.");

                File.WriteAllText(mathFile.AbsolutePath, "export let value: number = 2;");
                var repaired = unit.Recompile(new HashSet<string> { mathFile.AbsolutePath });

                Utility.AssertNoErrors(repaired);
                Assert.Empty(repaired.Files.Find(file => file.SourceFile.Name == "main.loom")!.Diagnostics.Set);
            }
        );

    [Fact]
    public void Recompile_UnknownChangedPath_FallsBackToFullCompile() =>
        Utility.WithTempProject(
            [("main.loom", "let x = 1;")],
            (unit, first) =>
            {
                var unknownPath = Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid() + ".loom");
                var second = unit.Recompile(new HashSet<string> { unknownPath });

                Utility.AssertNoErrors(second);
                Assert.Equal(first.Files.Count, second.Reanalyzed.Count);
                Assert.Equal(TimeSpan.Zero, second.EstimatedTimeSaved);
            }
        );

    [Fact]
    public void Recompile_WithInMemoryContent_UsesGivenTextInsteadOfDisk() =>
        Utility.WithTempProject(
            [("main.loom", "let x = 1;")],
            (unit, first) =>
            {
                var mainFile = unit.SourceFiles.First(f => f.Name == "main.loom");
                var second = unit.Recompile(new Dictionary<string, string> { [mainFile.AbsolutePath] = "let x: string = 1;" });

                Assert.Equal("let x = 1;", File.ReadAllText(mainFile.AbsolutePath));
                Utility.AssertDiagnostic(second.Diagnostics, InternalCodes.TypeMismatch, "Type '1' is not assignable to type 'string'.");
                Assert.Contains(second.Reanalyzed, f => f.Name == "main.loom");
                Assert.NotSame(first.Files.Single(), second.Files.Single());
            }
        );
    [Fact]
    public void Recompile_AfterInvalidThenFixedContent_ClearsDiagnostics() =>
        Utility.WithTempProject(
            [("main.loom", "let x = 1;")],
            (unit, _) =>
            {
                var mainFile = unit.SourceFiles.First(f => f.Name == "main.loom");

                var broken = unit.Recompile(new Dictionary<string, string> { [mainFile.AbsolutePath] = "let" });
                Utility.AssertDiagnostic(broken.Diagnostics, InternalCodes.MustHaveInitializer, "Immutable declarations must be initialized.");

                var fixedResult = unit.Recompile(new Dictionary<string, string> { [mainFile.AbsolutePath] = "let x = 1;" });
                Utility.AssertNoErrors(fixedResult);
            }
        );
    #endregion Recompile
}