using Loom.Core;
using Loom.Core.Diagnostics;
using Loom.Core.Resolving;

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