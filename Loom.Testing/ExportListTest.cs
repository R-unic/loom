using Loom.Core;
using Loom.Core.Diagnostics;
using Loom.Core.Pipeline;
using Loom.Core.Resolving;
using Loom.Core.Resolving.Symbols;

namespace Loom.Testing;

[Collection("Assembly")]
public class ExportListTest
{
    private const string MathModule = """
        export let pi = 3;
        export type Scalar = number;
        export interface Point { x: number y: number }
        """;

    [Fact]
    public void Exports_LocalNames_UnderTheirAliases() =>
        WithModule(
            """
            let tau = 6;
            export { tau, tau as TAU }
            """,
            (_, exports) =>
            {
                Assert.Equal(["tau", "TAU"], exports.Select(export => export.Name));
                Assert.Equal(["tau", "tau"], exports.Select(export => export.SourceName));
                Assert.DoesNotContain(exports, export => export.IsReExport);
            }
        );

    [Fact]
    public void Exports_ATypeOnlyList_WithoutItsValueNamespace() =>
        WithModule(
            """
            interface Shape { size: number }
            export type { Shape }
            """,
            (_, exports) =>
            {
                var export = Assert.Single(exports);
                Assert.Equal(SymbolKind.Interface, export.Symbol.Kind);
            }
        );

    [Fact]
    public void ReExports_AnotherModulesExport() =>
        WithModule(
            "export { pi, Scalar as Num } from \"./math\"",
            (_, exports) =>
            {
                Assert.Equal(["pi", "Num"], exports.Select(export => export.Name));
                Assert.Equal(["pi", "Scalar"], exports.Select(export => export.SourceName));
                Assert.All(exports, export => Assert.Equal("math.loom", export.Module?.Name));

                // a re-export forwards the name without binding it in this module's scope
                Assert.Empty(exports[0].Symbol.File.Name == "math.loom" ? [] : new[] { "bound locally" });
            }
        );

    [Fact]
    public void Generates_ReturnTable_AndExportedTypeAliases() =>
        Utility.WithTempProject(
            [
                ("math.loom", MathModule),
                (
                    "index.loom",
                    """
                    let tau = 6;
                    type Local = string;
                    export { tau, tau as TAU }
                    export { Local }
                    export { pi, Scalar as Num } from "./math"
                    export type { Point } from "./math"
                    """
                )
            ],
            (_, result) =>
            {
                Utility.AssertNoErrors(result);
                AssertRendered(
                    result,
                    "index.loom",
                    """
                    const math = require("./math")
                    const tau = 6
                    export type Local = string
                    export type Num = math.Scalar
                    export type Point = math.Point
                    return { tau = tau, TAU = tau, pi = math.pi }
                    """
                );
            }
        );

    [Fact]
    public void Imports_ThroughAReExportingModule() =>
        Utility.WithTempProject(
            [
                ("math.loom", MathModule),
                ("index.loom", "export { pi } from \"./math\"\nexport { Point } from \"./math\""),
                ("main.loom", "import { pi, Point } from \"./index\"\nlet p: Point = new Point { x: pi, y: pi };\nprint(p);")
            ],
            (_, result) =>
            {
                Utility.AssertNoErrors(result);

                // the require goes through the module that was imported from, not the original declaration's
                AssertRendered(
                    result,
                    "main.loom",
                    """
                    const index = require("./index")
                    const pi = index.pi
                    type Point = index.Point
                    const p: Point = { x = pi, y = pi }
                    print(p)
                    """
                );
            }
        );

    [Fact]
    public void Reports_ExportOfAnUndeclaredName() =>
        WithModule(
            "export { nope }",
            (result, _) => Utility.AssertDiagnostic(result.Diagnostics, InternalCodes.CannotFindSymbol, "Cannot find symbol 'nope'.")
        );

    [Fact]
    public void Reports_TypeOnlyExport_OfAValue() =>
        WithModule(
            "let tau = 6;\nexport type { tau }",
            (result, _) => Utility.AssertDiagnostic(
                result.Diagnostics,
                InternalCodes.TypeOnlyExportOfValue,
                "'tau' is a value, not a type.",
                "remove 'type' from the export"
            )
        );

    [Fact]
    public void Reports_DuplicateExport() =>
        WithModule(
            "export let pi = 3;\nlet tau = 6;\nexport { tau as pi }",
            (result, _) => Utility.AssertDiagnostic(result.Diagnostics, InternalCodes.DuplicateExport, "'pi' is already exported.")
        );

    [Fact]
    public void Reports_ReExportOfAMemberTheModuleDoesNotExport() =>
        WithModule(
            "export { cube } from \"./math\"",
            (result, _) => Utility.AssertDiagnostic(result.Diagnostics, InternalCodes.NoExportedMember, "Module 'math.loom' does not export 'cube'.")
        );

    [Fact]
    public void Reports_ExportListOutsideModuleScope()
    {
        var diagnostics = Utility.GetSemanticModel("let x = 1; fn f() { export { x } }").Diagnostics;
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.ExportOutsideModuleScope,
            "Declarations can only be exported at the top level of a module.",
            "move the 'export' declaration out of the enclosing block"
        );
    }

    private static void WithModule(string source, Action<CompilationResult, List<ExportBinding>> assert) =>
        Utility.WithTempProject(
            [("index.loom", source), ("math.loom", MathModule)],
            (_, result) =>
            {
                var index = result.Files.Single(file => file.SourceFile.Name == "index.loom");
                assert(result, index.SemanticModel.Exports);
            }
        );

    private static void AssertRendered(CompilationResult result, string fileName, string expected)
    {
        var file = result.Files.Single(compiled => compiled.SourceFile.Name == fileName);
        Assert.Equal(
            expected.Replace(Environment.NewLine, "\n") + '\n',
            file.RenderedLuau.Replace(Environment.NewLine, "\n")
        );
    }
}