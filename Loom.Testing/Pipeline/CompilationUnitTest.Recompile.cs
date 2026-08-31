using Loom.Core.Diagnostics;

namespace Loom.Testing.Pipeline;

public partial class CompilationUnitTest
{
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
    /// <remarks>
    ///     A re-parsed file misses the import cache, which is the whole of how an edit to its import list is
    ///     noticed - hitting the cache here would keep resolving the imports it used to have.
    /// </remarks>
    [Fact]
    public void Recompile_WhenAFilesImportsChange_ResolvesTheNewOnes() =>
        Utility.WithTempProject(
            [("math.loom", "export let value: number = 1;"), ("main.loom", "import { value } from \"./math\"\nlet doubled: number = value;")],
            (unit, first) =>
            {
                Utility.AssertNoErrors(first);
                var mainFile = unit.SourceFiles.First(file => file.Name == "main.loom");

                var second = unit.Recompile(
                    new Dictionary<string, string> { [mainFile.AbsolutePath] = "import { value } from \"./nowhere\"\nlet doubled: number = value;" }
                );

                Utility.AssertDiagnostic(second.Diagnostics, InternalCodes.ModuleNotFound, "Could not find module './nowhere'.");
            }
        );

    /// <remarks>
    ///     A cycle is a fact about the whole graph rather than about one file's imports, so it is the one
    ///     thing the import cache must not keep: the file that closed the cycle is re-parsed and re-resolved,
    ///     but the other files in it are not, and a cycle reported into their kept diagnostics would outlive
    ///     the import that caused it.
    /// </remarks>
    [Fact]
    public void Recompile_AfterACycleIsBroken_StopsReportingIt() =>
        Utility.WithTempProject(
            [
                ("a.loom", "import { b } from \"./b\"\nexport let a: number = b;"),
                // the second import is what gives 'b' import diagnostics of its own, and so a kept bag for a
                // cycle to be written into: a file with nothing wrong with its imports has no bag to spoil
                ("b.loom", "import { a } from \"./a\"\nimport { z } from \"./nowhere\"\nexport let b: number = 1;")
            ],
            (unit, first) =>
            {
                var (_, diagnosticBag) = first;
                Assert.Contains(diagnosticBag.Set, diagnostic => diagnostic.Code == InternalCodes.CircularModuleDependency);
                var aFile = unit.SourceFiles.First(file => file.Name == "a.loom");

                // the edit is to 'a', so 'b' - the file the cycle was reported against - is answered from the
                // import cache, and is the one that would go on reporting a cycle that is no longer there
                unit.Recompile(new Dictionary<string, string> { [aFile.AbsolutePath] = "export let a: number = 1;" });

                foreach (var file in unit.SourceFiles)
                    Assert.DoesNotContain(
                        unit.ModuleGraph!.GetDiagnostics(file)?.Set ?? [],
                        diagnostic => diagnostic.Code == InternalCodes.CircularModuleDependency
                    );
            }
        );

    /// <remarks>What a file's own imports resolved to is kept between builds, so it has to be handed back as well as skipped.</remarks>
    [Fact]
    public void Recompile_KeepsTheImportDiagnosticsOfAFileItDidNotReparse() =>
        Utility.WithTempProject(
            [("broken.loom", "import { thing } from \"./nowhere\"\nlet x = 1;"), ("main.loom", "let y = 1;")],
            (unit, first) =>
            {
                Utility.AssertDiagnostic(first.Diagnostics, InternalCodes.ModuleNotFound, "Could not find module './nowhere'.");
                var brokenFile = unit.SourceFiles.First(file => file.Name == "broken.loom");
                var mainFile = unit.SourceFiles.First(file => file.Name == "main.loom");

                unit.Recompile(new Dictionary<string, string> { [mainFile.AbsolutePath] = "let y = 2;" });

                var kept = unit.ModuleGraph!.GetDiagnostics(brokenFile);
                Assert.NotNull(kept);
                Assert.Contains(kept.Set, diagnostic => diagnostic.Code == InternalCodes.ModuleNotFound);
            }
        );

}
