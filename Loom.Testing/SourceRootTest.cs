using Loom.Config;
using Loom.Core.Diagnostics;
using Loom.Core.Modules;
using Loom.Core.Pipeline;
using Loom.Core.Text;

namespace Loom.Testing;

/// <summary>
///     A unit spanning two projects: the entry app, and a package it depends on, distributed as source. The
///     two roots disagree on project directory, output directory and identity, so every decision the compiler
///     makes per file has to follow that file's own root rather than the entry project's.
/// </summary>
[Collection("Assembly")]
public class SourceRootTest
{
    private const string AppManifest = "project_type = \"game\"\n[dependencies]\nmath = \"^1.0\"\n";

    /// <summary>An app that compiles the package without depending on it, as it would a dependency of a dependency.</summary>
    private const string AppManifestWithoutDependencies = "project_type = \"game\"\n";

    private const string PackageManifest = "project_type = \"library\"\n[package]\nname = \"math\"\nversion = \"1.0.0\"\n";

    /// <summary>The same pair under a name no intrinsic has, for the tests that read the generated locals.</summary>
    private const string GeometryAppManifest = "project_type = \"game\"\n[dependencies]\ngeometry = \"^1.0\"\n";

    private const string GeometryPackageManifest = "project_type = \"library\"\n[package]\nname = \"geometry\"\nversion = \"1.0.0\"\n";

    /// <summary>Maps the app's output directory, which is where its dependencies' output is written too.</summary>
    private const string AppRojoProject = """
        {
          "tree": {
            "$className": "DataModel",
            "ReplicatedStorage": {
              "Shared": { "$path": "dist" }
            }
          }
        }
        """;

    /// <summary>Maps the app's own code but nothing of the packages folder beneath it.</summary>
    private const string AppRojoProjectWithoutPackages = """
        {
          "tree": {
            "$className": "DataModel",
            "ReplicatedStorage": {
              "Shared": { "$path": "dist/main.luau" }
            }
          }
        }
        """;

    private sealed record Workspace(string Directory, LoomConfig App, LoomConfig Package);

    [Fact]
    public void Owns_TheFilesUnderItsOwnSourceDirectory()
        => WithWorkspace((workspace, unit) =>
            {
                var main = unit.SourceFiles.First(file => file.Name == "main.loom");
                var package = unit.SourceFiles.First(file => file.Name == "init.loom");

                Assert.Equal(2, unit.SourceFiles.Count());
                Assert.Same(unit.Roots.Entry, unit.Roots.Of(main));
                Assert.Same(unit.Roots[1], unit.Roots.Of(package));
                Assert.Same(workspace.Package, unit.Roots.ConfigOf(package));

                // a file under no root at all - an intrinsic, a lone file handed to the unit - reads the
                // entry project's settings, which is what it read when a unit had a single root
                Assert.Same(unit.Roots.Entry, unit.Roots.Of(Utility.TestFile("let x = 1;")));
            }
        );

    /// <remarks>
    ///     A dependency's Luau is part of the build consuming it, so it is written into the consumer's output
    ///     directory rather than into the dependency's own — under a packages folder, in a folder named by the
    ///     package. The dependency's own output directory is what it uses when it is built by itself.
    /// </remarks>
    [Fact]
    public void Compiles_ADependency_IntoTheEntryProjectsPackagesFolder()
        => WithWorkspace((workspace, unit) =>
            {
                var result = unit.Compile();
                Utility.AssertNoErrors(result);
                Assert.Equal(2, result.Files.Count);

                var main = result.Files.Single(file => file.SourceFile.Name == "main.loom");
                var package = result.Files.Single(file => file.SourceFile.Name == "init.loom");

                Assert.Equal(Path.Combine(workspace.App.Files.OutputDirectory, "main.luau"), main.Path);
                Assert.Equal(Path.Combine(workspace.App.Files.OutputDirectory, "packages", "math", "init.luau"), package.Path);
                Assert.True(File.Exists(main.Path));
                Assert.True(File.Exists(package.Path));
                Assert.False(Directory.Exists(workspace.Package.Files.OutputDirectory));
            }
        );

    /// <remarks>A scope is a folder above the name, which is the one layout that cannot collide with a package named for the scope.</remarks>
    [Fact]
    public void Compiles_AScopedDependency_IntoAFolderPerScope()
        => WithWorkspace((workspace, unit) =>
            {
                var result = unit.Compile();
                Utility.AssertNoErrors(result);

                var package = result.Files.Single(file => file.SourceFile.Name == "init.loom");
                Assert.Equal(Path.Combine(workspace.App.Files.OutputDirectory, "packages", "acme", "math", "init.luau"), package.Path);
            },
            appManifest: "project_type = \"game\"\n[dependencies]\n\"acme/math\" = \"^1.0\"\n",
            packageManifest: "project_type = \"library\"\n[package]\nname = \"acme/math\"\nversion = \"1.0.0\"\n"
        );

    /// <remarks>
    ///     Emission is the entry project's call for the whole unit: a library author's own <c>no_emit</c> is
    ///     about building that library alone, and must not leave a consumer's build missing the files its own
    ///     output tree is supposed to contain.
    /// </remarks>
    [Fact]
    public void Emits_EveryRoot_WhenTheEntryProjectAsksForOutput()
        => WithWorkspace((workspace, unit) =>
            {
                Utility.AssertNoErrors(unit.Compile());

                Assert.True(File.Exists(Path.Combine(workspace.App.Files.OutputDirectory, "main.luau")));
                Assert.True(File.Exists(Path.Combine(workspace.App.Files.OutputDirectory, "packages", "math", "init.luau")));
            },
            configure: workspace => workspace.Package.NoEmit = true
        );

    [Fact]
    public void Emits_Nothing_WhenTheEntryProjectSetsNoEmit()
        => WithWorkspace((workspace, unit) =>
            {
                Utility.AssertNoErrors(unit.Compile());
                Assert.False(Directory.Exists(workspace.App.Files.OutputDirectory));
            },
            configure: workspace => workspace.App.NoEmit = true
        );

    /// <remarks>
    ///     Reaching out of one project and into another is what a package specifier is for; a relative
    ///     specifier that climbs past its own root would bind to a module the consumer cannot require.
    /// </remarks>
    [Fact]
    public void Rejects_ARelativeImport_ThatLeavesItsOwnRoot()
        => WithWorkspace((_, unit) =>
            {
                var result = unit.Compile();
                Utility.AssertDiagnostic(
                    result.Diagnostics,
                    InternalCodes.ModuleOutsideSourceDirectory,
                    "Module '../../packages/math/src/init' is outside the source directory."
                );
            },
            appFiles: [("main.loom", "import { pi } from \"../../packages/math/src/init\"\nprint(pi);")]
        );

    /// <remarks>
    ///     The entry project's Rojo tree names every module of the unit, dependencies included: it is the one
    ///     describing the place the compiled game runs in. Because a dependency's output lands inside that
    ///     project's own output directory, the mapping covering the project's code covers its packages too —
    ///     the package's entry module being the folder itself, since Rojo folds an <c>init</c> into its folder.
    /// </remarks>
    [Fact]
    public void Names_ADependencyModule_ThroughTheEntryProjectsRojoTree()
        => WithWorkspace((_, unit) =>
            {
                var main = unit.SourceFiles.First(file => file.Name == "main.loom");
                var package = unit.SourceFiles.First(file => file.Name == "init.loom");

                Assert.Equal(
                    new ModuleRequirePath(ModuleRequirePathStatus.Resolved, "@game/ReplicatedStorage/Shared/main"),
                    unit.ModuleRequirePaths.Resolve(main, main, "./main")
                );

                Assert.Equal(
                    new ModuleRequirePath(ModuleRequirePathStatus.Resolved, "@game/ReplicatedStorage/Shared/packages/math"),
                    unit.ModuleRequirePaths.Resolve(main, package, "math")
                );
            },
            rojoProject: AppRojoProject
        );

    [Fact]
    public void Requires_ADependencysModule_ByItsInstancePath()
        => WithWorkspace((_, unit) =>
            {
                var result = unit.Compile();
                Utility.AssertNoErrors(result);

                Assert.Equal(
                    """
                    const geometry = require("@game/ReplicatedStorage/Shared/packages/geometry")
                    const pi = geometry.pi
                    print(pi)

                    """.Replace(Environment.NewLine, "\n"),
                    result.Files.Single(file => file.SourceFile.Name == "main.loom").RenderedLuau.Replace(Environment.NewLine, "\n")
                );
            },
            appFiles: [("main.loom", "import { pi } from \"geometry\"\nprint(pi);")],
            appManifest: GeometryAppManifest,
            packageManifest: GeometryPackageManifest,
            rojoProject: AppRojoProject
        );

    /// <remarks>
    ///     <c>math</c> is an intrinsic global, and a local of that name would shadow it for the rest of the
    ///     file, so the require takes a name of its own — the numbering is for names nothing in the specifier
    ///     can tell apart.
    /// </remarks>
    [Fact]
    public void Names_ARequire_WithoutShadowing_AnIntrinsicOfTheSameName()
        => WithWorkspace((_, unit) =>
            {
                var result = unit.Compile();
                Utility.AssertNoErrors(result);

                var main = result.Files.Single(file => file.SourceFile.Name == "main.loom").RenderedLuau;
                Assert.Contains("const math_1 = require(\"@game/ReplicatedStorage/Shared/packages/math\")", main);
                Assert.Contains("math.floor", main);
            },
            appFiles: [("main.loom", "import { pi } from \"math\"\nprint(math.floor(pi));")],
            rojoProject: AppRojoProject
        );

    [Fact]
    public void Requires_AModuleInsideADependency_ByItsInstancePath()
        => WithWorkspace((_, unit) =>
            {
                var result = unit.Compile();
                Utility.AssertNoErrors(result);

                Assert.Contains(
                    "require(\"@game/ReplicatedStorage/Shared/packages/math/vector\")",
                    result.Files.Single(file => file.SourceFile.Name == "main.loom").RenderedLuau
                );
            },
            appFiles: [("main.loom", "import { zero } from \"math/vector\"\nprint(zero);")],
            packageFiles: [("init.loom", "export let pi = 3;"), ("vector.loom", "export let zero = 0;")],
            rojoProject: AppRojoProject
        );

    /// <remarks>
    ///     Within a project the fallback relative require works, because output mirrors source. Across a
    ///     package boundary it is a guess at where the consumer's Rojo project put two separate projects'
    ///     output, so emitting one would trade a build error for a runtime one.
    /// </remarks>
    [Fact]
    public void Rejects_ARequireIntoAPackage_ThatTheRojoProjectDoesNotMap()
        => WithWorkspace((_, unit) => Utility.AssertDiagnostic(
                unit.Compile().Diagnostics,
                InternalCodes.ModuleNotFoundInRojo,
                "Could not locate package 'math' through the Rojo project; its compiled output at 'dist/packages' is not mapped.",
                "add a $path mapping to your default.project.json covering 'dist/packages'"
            ),
            appFiles: [("main.loom", "import { pi } from \"math\"\nprint(pi);")],
            rojoProject: AppRojoProjectWithoutPackages
        );

    /// <remarks>A project with no Rojo tree at all has no instance path to name a package by either, so it fails the same way.</remarks>
    [Fact]
    public void Rejects_ARequireIntoAPackage_WhenThereIsNoRojoProject()
        => WithWorkspace((_, unit) => Utility.AssertDiagnostic(
                unit.Compile().Diagnostics,
                InternalCodes.ModuleNotFoundInRojo,
                "Could not locate package 'math' through the Rojo project; its compiled output at 'dist/packages' is not mapped."
            ),
            appFiles: [("main.loom", "import { pi } from \"math\"\nprint(pi);")]
        );

    /// <remarks>
    ///     A bare specifier names the package's entry module — the <c>init.loom</c> at the top of its source
    ///     directory — the way a relative specifier names the <c>init.loom</c> of a folder.
    /// </remarks>
    [Fact]
    public void Resolves_ABareSpecifier_ToTheDependencysEntryModule()
        => WithWorkspace((_, unit) =>
            {
                var result = unit.Compile();
                Utility.AssertNoErrors(result);

                Assert.Equal("init.loom", ResolvedModuleOf(unit, "main.loom")?.Name);
            },
            appFiles: [("main.loom", "import { pi } from \"math\"\nprint(pi);")],
            rojoProject: AppRojoProject
        );

    [Fact]
    public void Resolves_ABareSpecifier_WithASubpath_ToAModuleInsideTheDependency()
        => WithWorkspace((_, unit) =>
            {
                var result = unit.Compile();
                Utility.AssertNoErrors(result);

                Assert.Equal("vector.loom", ResolvedModuleOf(unit, "main.loom")?.Name);
            },
            appFiles: [("main.loom", "import { zero } from \"math/vector\"\nprint(zero);")],
            packageFiles: [("init.loom", "export let pi = 3;"), ("vector.loom", "export let zero = 0;")],
            rojoProject: AppRojoProject
        );

    /// <remarks>A package refers to its own modules by its own name too, without depending on itself to do it.</remarks>
    [Fact]
    public void Resolves_ABareSpecifier_NamingThePackageItIsWrittenIn()
        => WithWorkspace((_, unit) =>
            {
                var result = unit.Compile();
                Utility.AssertNoErrors(result);

                Assert.Equal("vector.loom", ResolvedModuleOf(unit, "init.loom")?.Name);
            },
            packageFiles: [("init.loom", "import { zero } from \"math/vector\"\nexport let pi = zero;"), ("vector.loom", "export let zero = 0;")]
        );

    /// <remarks>
    ///     Everything the build compiles is reachable by name, so a package pulled in only because something
    ///     else depends on it would otherwise be importable by a project that never asked for it — and would
    ///     vanish the day that other project stopped depending on it.
    /// </remarks>
    [Fact]
    public void Rejects_AnImport_OfAPackage_TheProjectDoesNotDependOn()
        => WithWorkspace((_, unit) => Utility.AssertDiagnostic(
                unit.Compile().Diagnostics,
                InternalCodes.UndeclaredDependency,
                "Package 'math' is not a dependency of this project.",
                "it is only in this build because something else depends on it; add 'math' to [dependencies] to import it yourself"
            ),
            appFiles: [("main.loom", "import { pi } from \"math\"\nprint(pi);")],
            appManifest: AppManifestWithoutDependencies
        );

    [Fact]
    public void Rejects_APackageSubpath_ThatClimbsOutOfThatPackage()
        => WithWorkspace((_, unit) => Utility.AssertDiagnostic(
                unit.Compile().Diagnostics,
                InternalCodes.ModuleOutsideSourceDirectory,
                "Module 'math/../../../app/src/main' is outside the source directory."
            ),
            appFiles: [("main.loom", "import { pi } from \"math/../../../app/src/main\"\nprint(pi);")]
        );

    /// <remarks>
    ///     No relative path reaches into another root, so the module a casing mistake meant has to be named the
    ///     only way the importing file could have written it: by its package.
    /// </remarks>
    [Fact]
    public void Names_ADependencysModule_ByItsPackage_WhenHintingAtACasingMistake()
        => WithWorkspace((_, unit) => Utility.AssertDiagnostic(
                unit.Compile().Diagnostics,
                InternalCodes.ModuleNotFound,
                "Could not find module 'math/Vector'.",
                "did you mean 'math/vector'? module paths are case-sensitive"
            ),
            appFiles: [("main.loom", "import { zero } from \"math/Vector\"\nprint(zero);")],
            packageFiles: [("init.loom", "export let pi = 3;"), ("vector.loom", "export let zero = 0;")]
        );

    /// <remarks>
    ///     A package's public surface is what it exports: exports are versioned, named at the import site and
    ///     shadowable, none of which is true of a name that simply turns up in scope. Its declaration files
    ///     furnish the package itself and stop there.
    /// </remarks>
    [Fact]
    public void Keeps_ADependencysAmbientDeclarations_OutOfTheConsumersScope()
        => WithWorkspace((_, unit) =>
            {
                var result = unit.Compile();
                Utility.AssertDiagnostic(result.Diagnostics, InternalCodes.CannotFindName, "Cannot find name 'physics_step'.");

                // the package's own files still see it, and no name of the package's leaked into the app
                var main = unit.SourceFiles.First(file => file.Name == "main.loom");
                var package = unit.SourceFiles.First(file => file.Name == "init.loom");

                Assert.Contains(unit.Globals.Of(package).Keys, symbol => symbol.Name == "physics_step");
                Assert.Empty(unit.Globals.Of(main));
            },
            appFiles: [("main.loom", "print(physics_step);")],
            packageFiles: [("init.loom", "export let pi = physics_step;"), ("globals.d.loom", "declare let physics_step: number;")]
        );

    /// <remarks>Each root's ambient scope is its own, so the same name in two of them is two declarations, not a collision.</remarks>
    [Fact]
    public void Compiles_TwoRoots_DeclaringTheSameAmbientName()
        => WithWorkspace((_, unit) =>
            {
                var result = unit.Compile();
                Utility.AssertNoErrors(result);

                var main = unit.SourceFiles.First(file => file.Name == "main.loom");
                var package = unit.SourceFiles.First(file => file.Name == "init.loom");

                var appGlobal = Assert.Single(unit.Globals.Of(main).Keys, symbol => symbol.Name == "version");
                var packageGlobal = Assert.Single(unit.Globals.Of(package).Keys, symbol => symbol.Name == "version");

                Assert.NotSame(appGlobal, packageGlobal);
                Assert.Equal("globals.d.loom", appGlobal.File.Name);
                Assert.Equal("package-globals.d.loom", packageGlobal.File.Name);
            },
            appFiles: [("main.loom", "print(version);"), ("globals.d.loom", "declare let version: string;")],
            packageFiles: [("init.loom", "export let pi = version;"), ("package-globals.d.loom", "declare let version: number;")]
        );

    /// <remarks>Intrinsics belong to the language rather than to a project, so partitioning globals by root does not reach them.</remarks>
    [Fact]
    public void Resolves_Intrinsics_FromEveryRoot()
        => WithWorkspace((_, unit) => Utility.AssertNoErrors(unit.Compile()),
            appFiles: [("main.loom", "print(\"app\");")],
            packageFiles: [("init.loom", "export let pi = 3;\nprint(\"package\");")]
        );

    /// <remarks>
    ///     An unused import in a package is a fact about the package's own code, which the reader of this
    ///     build cannot edit and did not write.
    /// </remarks>
    [Fact]
    public void Reports_NothingOfADependencysWarnings()
        => WithWorkspace((_, unit) =>
            {
                var result = unit.Compile();

                Assert.Empty(result.Diagnostics.Set);
                Assert.Empty(result.Files.Single(file => file.SourceFile.Name == "init.loom").Diagnostics.Set);
            },
            packageFiles:
            [
                ("init.loom", "import { zero } from \"./vector\"\nexport let pi = 3;"),
                ("vector.loom", "export let zero = 0;")
            ]
        );

    [Fact]
    public void Reports_ADependencysWarnings_WhenTheBuildAsksForThem()
        => WithWorkspace((_, unit) => Utility.AssertDiagnostic(
                unit.Compile().Diagnostics,
                InternalCodes.UnusedImport,
                "'zero' is imported but never used."
            ),
            packageFiles:
            [
                ("init.loom", "import { zero } from \"./vector\"\nexport let pi = 3;"),
                ("vector.loom", "export let zero = 0;")
            ],
            diagnosticOptions: new DiagnosticOptions { ReportDependencyDiagnostics = true }
        );

    /// <remarks>
    ///     The consumer cannot fix the package's error, but nothing can be built on a package that did not
    ///     compile — so it is reported, framed as the package's, while their own file still reports its own.
    /// </remarks>
    [Fact]
    public void Attributes_ADependencysError_ToThePackage_WhileTheConsumersOwnFilesReportTheirOwn()
        => WithWorkspace((_, unit) =>
            {
                var result = unit.Compile();

                var attributed = Assert.Single(result.Diagnostics.Set, diagnostic => diagnostic.Code == InternalCodes.PackageFailedToCompile);
                Assert.StartsWith("Package 'math' failed to compile: Cannot find name 'missing'.", attributed.Message);
                Assert.Equal("init.loom", attributed.Span.File.Name);

                // the consumer's own diagnostic is theirs, reported exactly as raised
                Utility.AssertDiagnostic(result.Diagnostics, InternalCodes.CannotFindName, "Cannot find name 'also_missing'.");
            },
            appFiles: [("main.loom", "print(also_missing);")],
            packageFiles: [("init.loom", "export let pi = missing;")]
        );

    /// <remarks>Read with a build asking for its dependencies' diagnostics in full, since a consumer is only told the package failed.</remarks>
    [Fact]
    public void Names_ADependencysFiles_ByItsPackage_WhenReportingACycle()
        => WithWorkspace((_, unit) =>
            {
                var result = unit.Compile();
                var cycle = result.Diagnostics.Find(diagnostic => diagnostic.Code == InternalCodes.CircularModuleDependency);

                Assert.NotNull(cycle);
                Assert.Contains("math/init.loom", cycle.Message);
                Assert.Contains("math/util.loom", cycle.Message);
            },
            packageFiles:
            [
                ("init.loom", "import { helper } from \"./util\"\nexport let pi = helper;"),
                ("util.loom", "import { pi } from \"./init\"\nexport let helper = pi;")
            ],
            diagnosticOptions: new DiagnosticOptions { ReportDependencyDiagnostics = true }
        );

    /// <remarks>
    ///     A vendored package sits under the source directory of the project depending on it, so both roots
    ///     load its files. The innermost root owns them, and no file is left compiled twice into two places.
    ///     Where its output lands does not depend on that: a package is written to the same place whether the
    ///     package manager vendored its sources or left them in a cache elsewhere.
    /// </remarks>
    [Fact]
    public void Owns_AVendoredPackage_OverTheProjectItSitsInside()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "loom-test-" + Guid.NewGuid());
        try
        {
            var app = WriteProject(Path.Combine(workspace, "app"), AppManifest, [("main.loom", "let x = 1;")]);
            var package = WriteProject(Path.Combine(workspace, "app", "src", "packages", "math"), PackageManifest, [("init.loom", "export let pi = 3;")]);
            package.NoEmit = app.NoEmit = true;

            var unit = new CompilationUnit(new SourceRootSet(new SourceRoot(app), new SourceRoot(package)));
            var vendored = Assert.Single(unit.SourceFiles, file => file.Name == "init.loom");

            Assert.Same(unit.Roots[1], unit.Roots.Of(vendored));
            Assert.DoesNotContain(unit.Roots.Entry.Files, file => file.Name == "init.loom");

            var result = unit.Compile();
            Utility.AssertNoErrors(result);
            Assert.Equal(2, result.Files.Count);
            Assert.Equal(
                Path.Combine(app.Files.OutputDirectory, "packages", "math", "init.luau"),
                result.Files.Single(file => file.SourceFile.Name == "init.loom").Path
            );
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    /// <summary>The module the single import of <paramref name="fileName" /> resolved to.</summary>
    private static SourceFile? ResolvedModuleOf(CompilationUnit unit, string fileName)
    {
        var graph = Assert.IsType<ModuleGraph>(unit.ModuleGraph);
        var file = graph.Order.First(parsed => parsed.File.Name == fileName);

        return graph.GetResolvedModule(Assert.Single(file.Imports));
    }

    /// <summary>
    ///     Runs <paramref name="assert" /> against a unit spanning a throwaway workspace's two projects: the
    ///     entry app in <c>app/</c>, and the <c>math</c> package it depends on in <c>packages/math/</c>.
    /// </summary>
    private static void WithWorkspace(
        Action<Workspace, CompilationUnit> assert,
        IEnumerable<(string Path, string Source)>? appFiles = null,
        IEnumerable<(string Path, string Source)>? packageFiles = null,
        string? rojoProject = null,
        string? appManifest = null,
        string? packageManifest = null,
        DiagnosticOptions? diagnosticOptions = null,
        Action<Workspace>? configure = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "loom-test-" + Guid.NewGuid());
        try
        {
            var appDirectory = Path.Combine(directory, "app");
            var app = WriteProject(appDirectory, appManifest ?? AppManifest, appFiles ?? [("main.loom", "let x = 1;")]);
            var package = WriteProject(
                Path.Combine(directory, "packages", "math"),
                packageManifest ?? PackageManifest,
                packageFiles ?? [("init.loom", "export let pi = 3;")]
            );

            if (rojoProject != null)
                File.WriteAllText(Path.Combine(appDirectory, RojoResolver.ProjectFileName), rojoProject);

            var workspace = new Workspace(directory, app, package);
            configure?.Invoke(workspace);

            assert(workspace, new CompilationUnit(new SourceRootSet(new SourceRoot(app), new SourceRoot(package)), diagnosticOptions));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>Writes a project directory - its manifest and its source files - and returns the config located from it.</summary>
    private static LoomConfig WriteProject(string directory, string manifest, IEnumerable<(string Path, string Source)> files)
    {
        var sourceDirectory = Path.Combine(directory, "src");
        Directory.CreateDirectory(sourceDirectory);
        File.WriteAllText(
            Path.Combine(directory, "loom-config.toml"),
            manifest + "[files]\nsource_directory = \"src\"\noutput_directory = \"dist\"\n"
        );

        foreach (var (path, source) in files)
        {
            var filePath = Path.Combine(sourceDirectory, path);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, source);
        }

        var config = ConfigReader.LocateFromDirectory(directory);
        Assert.NotNull(config);

        return config;
    }
}
