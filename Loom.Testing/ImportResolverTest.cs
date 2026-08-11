using Loom.Core.Diagnostics;
using Loom.Core.Pipeline;
using Loom.Core.Resolving;
using Loom.Core.Resolving.Symbols;

namespace Loom.Testing;

[Collection("Assembly")]
public class ImportResolverTest
{
    private const string MathModule = """
        export let pi = 3;
        export fn square(x: number): number -> x * x;
        export type Scalar = number;
        export interface Point { x: number y: number }
        """;

    [Fact]
    public void Binds_ImportedValue_ToTheExportingModulesSymbol() =>
        WithImportingModule(
            "import { square } from \"./math\"",
            (result, bindings) =>
            {
                var binding = Assert.Single(bindings);
                Assert.Equal("square", binding.LocalName);
                Assert.Equal("square", binding.ExportedName);
                Assert.Equal("math.loom", binding.Module.Name);
                Assert.False(binding.IsTypeOnly);
                Assert.True(binding.RequiresModuleAtRuntime);

                // the symbol is the exporting module's own instance, not a copy
                var math = result.Files.Single(file => file.SourceFile.Name == "math.loom");
                Assert.Contains(math.SemanticModel.Exports, export => ReferenceEquals(export.Symbol, binding.Symbol));
            }
        );

    [Fact]
    public void Binds_AliasedImport_UnderItsLocalName() =>
        WithImportingModule(
            "import { pi as PI } from \"./math\"",
            (_, bindings) =>
            {
                var binding = Assert.Single(bindings);
                Assert.Equal("PI", binding.LocalName);
                Assert.Equal("pi", binding.ExportedName);

                // the symbol keeps the exported name; only the importing scope knows it as PI
                Assert.Equal("pi", binding.Symbol.Name);
            }
        );

    [Fact]
    public void Binds_ImportedInterface_InBothNamespaces() =>
        WithImportingModule(
            "import { Point } from \"./math\"",
            (_, bindings) =>
            {
                Assert.Equal([SymbolKind.Variable, SymbolKind.Interface], bindings.Select(binding => binding.Symbol.Kind));

                // an interface resolves to an InterfaceSymbol even when imported, which is what lets
                // 'new Point { ... }' generate a table in the importing module
                Assert.Contains(bindings, binding => binding.Symbol is InterfaceSymbol);
                Assert.DoesNotContain(bindings, binding => binding.RequiresModuleAtRuntime);
            }
        );

    [Fact]
    public void Binds_TypeOnlyImport_InTheTypeNamespaceOnly() =>
        WithImportingModule(
            "import type { Point } from \"./math\"",
            (_, bindings) =>
            {
                var binding = Assert.Single(bindings);
                Assert.Equal(SymbolKind.Interface, binding.Symbol.Kind);
                Assert.True(binding.IsTypeOnly);
                Assert.False(binding.RequiresModuleAtRuntime);
            }
        );

    [Fact]
    public void TypeChecks_AcrossModules() =>
        WithImportingModule(
            """
            import { square, pi as PI, Point } from "./math"
            import type { Scalar } from "./math"
            let total: Scalar = square(PI);
            fn describe(p: Point): number -> p.x + total;
            print(describe(new Point { x: 1, y: 2 }));
            """,
            (result, bindings) =>
            {
                Utility.AssertNoErrors(result);
                Assert.Equal(5, bindings.Count);
            }
        );

    [Fact]
    public void Tracks_WhichImportsAreUsed() =>
        WithImportingModule(
            "import { pi, square } from \"./math\"\nprint(square(1));",
            (result, bindings) =>
            {
                Assert.Equal([("pi", false), ("square", true)], bindings.Select(binding => (binding.LocalName, binding.IsUsed)));
                Utility.AssertDiagnostic(
                    result.Diagnostics,
                    InternalCodes.UnusedImport,
                    "'pi' is imported but never used.",
                    "remove it from the import clause"
                );

                Assert.DoesNotContain(
                    result.Diagnostics.Set,
                    diagnostic => diagnostic.Code == InternalCodes.UnusedImport && diagnostic.Message.Contains("square")
                );
            }
        );

    [Fact]
    public void Counts_UseInATypeAnnotation_AsUse() =>
        WithImportingModule(
            "import type { Point } from \"./math\"\nfn area(p: Point): number -> p.x * p.y;\nprint(area);",
            (result, bindings) =>
            {
                Assert.All(bindings, binding => Assert.True(binding.IsUsed));
                Assert.DoesNotContain(result.Diagnostics.Set, diagnostic => diagnostic.Code == InternalCodes.UnusedImport);
            }
        );

    [Fact]
    public void Reports_NoExportedMember_ListingWhatTheModuleExports() =>
        WithImportingModule(
            "import { cube } from \"./math\"",
            (result, _) => Utility.AssertDiagnostic(
                result.Diagnostics,
                InternalCodes.NoExportedMember,
                "Module 'math.loom' does not export 'cube'.",
                "it exports 'pi', 'square', 'Scalar', 'Point'"
            )
        );

    [Fact]
    public void Reports_TypeOnlyImport_OfAValue() =>
        WithImportingModule(
            "import type { square } from \"./math\"",
            (result, _) => Utility.AssertDiagnostic(
                result.Diagnostics,
                InternalCodes.TypeOnlyImportOfValue,
                "'square' is a value, not a type.",
                "remove 'type' from the import"
            )
        );

    [Fact]
    public void Reports_DuplicateImport() =>
        WithImportingModule(
            "import { pi, pi } from \"./math\"",
            (result, _) => Utility.AssertDiagnostic(result.Diagnostics, InternalCodes.DuplicateImport, "'pi' is imported more than once.")
        );

    [Fact]
    public void Reports_ImportedName_CollidingWithALocalDeclaration() =>
        WithImportingModule(
            "let pi = 4;\nimport { pi } from \"./math\"",
            (result, _) => Utility.AssertDiagnostic(
                result.Diagnostics,
                InternalCodes.DuplicateName,
                "Variable 'pi' is already declared in this scope."
            )
        );

    [Fact]
    public void Binds_TheNamesOfAnUnresolvableImport_SoOnlyTheModuleErrorIsReported() =>
        Utility.WithTempProject(
            [("main.loom", "import { pi } from \"./nope\"\nprint(pi);")],
            (_, result) =>
            {
                var error = Assert.Single(result.Diagnostics.Errors().Set);
                Assert.Equal(InternalCodes.ModuleNotFound, error.Code);
            }
        );

    [Fact]
    public void Binds_AnUnresolvableImport_InBothNamespaces() =>
        Utility.WithTempProject(
            [("main.loom", "import { Thing } from \"./nope\"\nlet v: Thing = 1;\nprint(v);")],
            (_, result) =>
            {
                // the name resolves in type position too, so the module error is all that is left
                var error = Assert.Single(result.Diagnostics.Errors().Set);
                Assert.Equal(InternalCodes.ModuleNotFound, error.Code);
                Assert.Contains("const v", Assert.Single(result.Files).RenderedLuau);
            }
        );

    [Fact]
    public void Binds_ATypeOnlyUnresolvableImport_InTheTypeNamespaceOnly() =>
        Utility.WithTempProject(
            [("main.loom", "import type { Thing } from \"./nope\"\nprint(Thing);")],
            (_, result) =>
            {
                var codes = result.Diagnostics.Set.Select(diagnostic => diagnostic.Code).ToList();

                // the value namespace is left alone, so using it as a value is still an error
                Assert.Contains(InternalCodes.ModuleNotFound, codes);
                Assert.Contains(InternalCodes.CannotFindName, codes);
            }
        );

    [Fact]
    public void Binds_TheNameOfAnUnresolvableNamespaceImport() =>
        Utility.WithTempProject(
            [("main.loom", "import * as vec from \"./nope\"\nprint(vec);")],
            (_, result) =>
            {
                var error = Assert.Single(result.Diagnostics.Errors().Set);
                Assert.Equal(InternalCodes.ModuleNotFound, error.Code);
            }
        );

    [Fact]
    public void Reports_OnlyTheCycle_WhenAModuleImportsThroughOne() =>
        Utility.WithTempProject(
            [("a.loom", "import { b } from \"./b\"\nexport let a = 1;\nprint(b);"), ("b.loom", "import { a } from \"./a\"\nexport let b = 2;\nprint(a);")],
            (_, result) =>
            {
                var error = Assert.Single(result.Diagnostics.Errors().Set);
                Assert.Equal(InternalCodes.CircularModuleDependency, error.Code);
            }
        );

    [Fact]
    public void KeepsALocalDeclaration_WhenAnUnresolvableImport_SharesItsName() =>
        Utility.WithTempProject(
            [("main.loom", "let pi = 1;\nimport { pi } from \"./nope\"\nprint(pi);")],
            (_, result) =>
            {
                // the module error is the actionable one; a duplicate-name error on top of it is not
                var error = Assert.Single(result.Diagnostics.Errors().Set);
                Assert.Equal(InternalCodes.ModuleNotFound, error.Code);
                Assert.Contains("const pi = 1", Assert.Single(result.Files).RenderedLuau);
            }
        );

    [Fact]
    public void Binds_Imports_BeforeTheStatementsAboveThem() =>
        WithImportingModule(
            "print(square(2));\nimport { square } from \"./math\"",
            (result, bindings) =>
            {
                Utility.AssertNoErrors(result);
                Assert.True(Assert.Single(bindings).IsUsed);
            }
        );

    [Fact]
    public void Binds_NamespaceImports_BeforeTheStatementsAboveThem() =>
        WithImportingModule("print(math.pi);\nimport * as math from \"./math\"", (result, _) => Utility.AssertNoErrors(result));

    [Fact]
    public void Reports_ASpecifierThatDiffersOnlyInCase_WithTheModuleItMeant() =>
        WithImportingModule(
            "import { pi } from \"./Math\"",
            (result, _) => Utility.AssertDiagnostic(
                result.Diagnostics,
                InternalCodes.ModuleNotFound,
                "Could not find module './Math'.",
                "did you mean './math'? module paths are case-sensitive"
            )
        );

    [Fact]
    public void Reports_ADirectoryModuleThatDiffersOnlyInCase_ByItsDirectory() =>
        Utility.WithTempProject(
            [("main.loom", "import { helper } from \"./Util\"\nprint(helper);"), (Path.Combine("util", "init.loom"), "export let helper = 1;")],
            (_, result) => Utility.AssertDiagnostic(
                result.Diagnostics,
                InternalCodes.ModuleNotFound,
                "Could not find module './Util'.",
                "did you mean './util'? module paths are case-sensitive"
            )
        );

    [Fact]
    public void Reports_ImportOutsideModuleScope()
    {
        var diagnostics = Utility.GetSemanticModel("fn f() { import { x } from \"./math\" }").Diagnostics;
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.ImportOutsideModuleScope,
            "Modules can only be imported at the top level of a module.",
            "move the 'import' declaration out of the enclosing block"
        );
    }

    private static void WithImportingModule(string source, Action<CompilationResult, List<ImportBinding>> assert) =>
        Utility.WithTempProject(
            [("main.loom", source), ("math.loom", MathModule)],
            (_, result) =>
            {
                var main = result.Files.Single(file => file.SourceFile.Name == "main.loom");
                assert(result, main.SemanticModel.ImportBindings);
            }
        );
}