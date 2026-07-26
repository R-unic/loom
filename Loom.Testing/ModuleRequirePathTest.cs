using Loom.Core;
using Loom.Core.Diagnostics;

namespace Loom.Testing;

[Collection("Assembly")]
public class ModuleRequirePathTest
{
    private const string OutputMappedProject = """
        {
          "tree": {
            "$className": "DataModel",
            "ReplicatedStorage": {
              "Shared": { "$path": "dist" }
            }
          }
        }
        """;

    private const string OutputUnmappedProject = """
        {
          "tree": {
            "$className": "DataModel",
            "ReplicatedStorage": {
              "include": { "$path": "include" }
            }
          }
        }
        """;

    private static (string Path, string Source)[] MathAndMain =>
    [
        ("main.loom", "import { square } from \"./math\"\nprint(square(2));"), ("math.loom", "export fn square(x: number): number -> x * x;")
    ];

    [Fact]
    public void Requires_ByInstancePath_WhenRojoMapsTheOutput() =>
        AssertMainRequires(
            OutputMappedProject,
            """
            const math = require("@game/ReplicatedStorage/Shared/math")
            const square = math.square
            print(square(2))
            """
        );

    [Fact]
    public void Requires_ADirectoryModule_ByItsFoldersInstancePath() =>
        Utility.WithTempProject(
            [("main.loom", "import { helper } from \"./util\"\nprint(helper);"), (Path.Combine("util", "init.loom"), "export let helper = 1;")],
            (_, result) =>
            {
                Utility.AssertNoErrors(result);
                Assert.Contains("require(\"@game/ReplicatedStorage/Shared/util\")", MainOutput(result));
            },
            OutputMappedProject
        );

    [Fact]
    public void FallsBack_ToTheSpecifier_AndWarns_WhenRojoDoesNotMapTheOutput() =>
        Utility.WithTempProject(
            MathAndMain,
            (_, result) =>
            {
                Assert.Contains("require(\"./math\")", MainOutput(result));
                Utility.AssertDiagnostic(
                    result.Diagnostics,
                    InternalCodes.ModuleNotFoundInRojo,
                    "Could not locate module './math' through the Rojo project; falling back to a relative require.",
                    "add a $path mapping to your default.project.json that includes the output directory"
                );
            },
            OutputUnmappedProject
        );

    [Fact]
    public void FallsBack_Silently_WhenThereIsNoRojoProject() =>
        Utility.WithTempProject(
            MathAndMain,
            (_, result) =>
            {
                Assert.Contains("require(\"./math\")", MainOutput(result));

                // nothing to consult means nothing to warn about
                Assert.DoesNotContain(result.Diagnostics.Set, diagnostic => diagnostic.Code == InternalCodes.ModuleNotFoundInRojo);
            }
        );

    private static void AssertMainRequires(string rojoProject, string expected) =>
        Utility.WithTempProject(
            MathAndMain,
            (_, result) =>
            {
                Utility.AssertNoErrors(result);
                Assert.Equal(
                    expected.Replace(Environment.NewLine, "\n") + '\n',
                    MainOutput(result).Replace(Environment.NewLine, "\n")
                );
            },
            rojoProject
        );

    private static string MainOutput(CompilationResult result) => result.Files.Single(file => file.SourceFile.Name == "main.loom").RenderedLuau;
}