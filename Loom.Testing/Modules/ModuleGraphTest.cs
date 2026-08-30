using Loom.Core.Diagnostics;
using Loom.Core.Modules;

namespace Loom.Testing.Modules;

[Collection("Assembly")]
public class ModuleGraphTest
{
    [Fact]
    public void Orders_Dependencies_BeforeTheirImporters() =>
        Utility.WithTempProject(
            [("a.loom", "import { b } from \"./b\"\nlet used_a = 1;"), ("b.loom", "import { c } from \"./c\"\nexport let b = 2;"), ("c.loom", "export let c = 3;")],
            (unit, _) =>
            {
                var graph = Assert.IsType<ModuleGraph>(unit.ModuleGraph);
                Assert.Equal(["c.loom", "b.loom", "a.loom"], graph.Order.Select(parsed => parsed.File.Name));
            }
        );

    [Fact]
    public void Resolves_Import_ToModuleFile() =>
        Utility.WithTempProject(
            [("main.loom", "import { pi } from \"./math\""), ("math.loom", "export let pi = 3;")],
            (unit, _) =>
            {
                var graph = Assert.IsType<ModuleGraph>(unit.ModuleGraph);
                var main = graph.Order.First(parsed => parsed.File.Name == "main.loom");
                var import = Assert.Single(main.Imports);

                Assert.Equal("math.loom", graph.GetResolvedModule(import)?.Name);
            }
        );

    [Fact]
    public void Resolves_Import_ToInitFileOfDirectory() =>
        Utility.WithTempProject(
            [("main.loom", "import { helper } from \"./util\""), (Path.Combine("util", "init.loom"), "export let helper = 1;")],
            (unit, result) =>
            {
                var graph = Assert.IsType<ModuleGraph>(unit.ModuleGraph);
                var main = graph.Order.First(parsed => parsed.File.Name == "main.loom");
                var import = Assert.Single(main.Imports);

                Assert.Equal("init.loom", graph.GetResolvedModule(import)?.Name);
                Assert.DoesNotContain(result.Diagnostics.Set, diagnostic => diagnostic.Code == InternalCodes.ModuleNotFound);
            }
        );

    [Fact]
    public void Reports_ModuleNotFound() =>
        AssertModuleDiagnostic(
            "import { x } from \"./nope\"",
            InternalCodes.ModuleNotFound,
            "Could not find module './nope'."
        );

    [Fact]
    public void Reports_ModuleNotFound_HintingAtTheExtension() =>
        Utility.WithTempProject(
            [("main.loom", "import { pi } from \"./math.loom\""), ("math.loom", "export let pi = 3;")],
            (_, result) => Utility.AssertDiagnostic(
                result.Diagnostics,
                InternalCodes.ModuleNotFound,
                "Could not find module './math.loom'.",
                "drop the '.loom' extension from the path"
            )
        );

    [Fact]
    public void Reports_DeclarationFiles_AsNotImportable() =>
        Utility.WithTempProject(
            [("main.loom", "import { thing } from \"./types\""), ("types.d.loom", "declare let thing: number;")],
            (_, result) => Utility.AssertDiagnostic(
                result.Diagnostics,
                InternalCodes.ModuleNotFound,
                "Could not find module './types'."
            )
        );

    /// <remarks>A bare specifier names a package, so in a build compiling no packages there is nothing for it to name.</remarks>
    [Fact]
    public void Reports_BareSpecifier_NamingAPackageOutsideTheBuild() =>
        AssertModuleDiagnostic(
            "import { x } from \"math\"",
            InternalCodes.PackageNotFound,
            "Cannot find package 'math'.",
            "add 'math' to [dependencies] and install it before importing from it"
        );

    /// <remarks>The package name is the first segment, so this reports the package rather than the whole specifier.</remarks>
    [Fact]
    public void Reports_BareSpecifier_WithASubpath_ByItsPackage() =>
        AssertModuleDiagnostic(
            "import { x } from \"math/vector\"",
            InternalCodes.PackageNotFound,
            "Cannot find package 'math'."
        );

    [Fact]
    public void Reports_ASpecifier_ThatIsNeitherAPathNorAPackageName() =>
        AssertModuleDiagnostic(
            "import { x } from \"math!\"",
            InternalCodes.UnsupportedModuleSpecifier,
            "Module 'math!' is neither a relative path nor a package name.",
            "start the path with './' or '../', or name a package you depend on"
        );

    [Fact]
    public void Reports_ABareSpecifier_CarryingTheLoomExtension() =>
        AssertModuleDiagnostic(
            "import { x } from \"math.loom\"",
            InternalCodes.UnsupportedModuleSpecifier,
            "Module 'math.loom' is neither a relative path nor a package name.",
            "drop the '.loom' extension from the path"
        );

    [Fact]
    public void Reports_SelfImport() =>
        AssertModuleDiagnostic(
            "import { x } from \"./main\"",
            InternalCodes.SelfImport,
            "A module cannot import itself."
        );

    [Fact]
    public void Reports_ModuleOutsideSourceDirectory() =>
        AssertModuleDiagnostic(
            "import { x } from \"../outside\"",
            InternalCodes.ModuleOutsideSourceDirectory,
            "Module '../outside' is outside the source directory."
        );

    [Fact]
    public void Reports_ImportInDeclarationFile() =>
        Utility.WithTempProject(
            [("types.d.loom", "import { x } from \"./math\""), ("math.loom", "export let x = 1;")],
            (_, result) => Utility.AssertDiagnostic(
                result.Diagnostics,
                InternalCodes.ImportInDeclarationFile,
                "Declaration files cannot import modules.",
                "declare the symbol ambiently instead"
            )
        );

    [Fact]
    public void Reports_CircularDependency_AndStillOrdersEveryFile() =>
        Utility.WithTempProject(
            [("a.loom", "import { b } from \"./b\"\nexport let a = 1;"), ("b.loom", "import { a } from \"./a\"\nexport let b = 2;")],
            (unit, result) =>
            {
                var diagnostic = result.Diagnostics.Find(d => d.Code == InternalCodes.CircularModuleDependency);
                Assert.NotNull(diagnostic);
                Assert.Contains("a.loom", diagnostic.Message);
                Assert.Contains("b.loom", diagnostic.Message);
                Assert.Equal("Luau requires cannot be cyclic; move the shared code into a third module", diagnostic.Hint);

                // the cycle is reported and then ignored, so ordering still completes for every file
                var graph = Assert.IsType<ModuleGraph>(unit.ModuleGraph);
                Assert.Equal(2, graph.Order.Count);
                Assert.Equal(2, result.Files.Count);
            }
        );

    private static void AssertModuleDiagnostic(string source, string code, string message, string? hint = null) =>
        Utility.WithTempProject(
            [("main.loom", source)],
            (_, result) => Utility.AssertDiagnostic(result.Diagnostics, code, message, hint)
        );
}