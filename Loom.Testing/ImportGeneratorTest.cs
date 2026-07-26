using Loom.Core;
using Loom.Core.Pipeline;

namespace Loom.Testing;

[Collection("Assembly")]
public class ImportGeneratorTest
{
    private const string MathModule = """
        export let pi = 3;
        export fn square(x: number): number -> x * x;
        export type Scalar = number;
        export type Box<T> = T[];
        export enum Color { Red = 1, Blue = 2 }
        export interface Point { x: number y: number }
        """;

    [Fact]
    public void Requires_TheModule_AndBindsEachValue() =>
        AssertGenerated(
            "import { square, pi } from \"./math\"\nprint(square(pi));",
            """
            const math = require("./math")
            const square = math.square
            const pi = math.pi
            print(square(pi))
            """
        );

    [Fact]
    public void Binds_AnAlias_ToItsExportedName() =>
        AssertGenerated(
            "import { pi as PI } from \"./math\"\nprint(PI);",
            """
            const math = require("./math")
            const PI = math.pi
            print(PI)
            """
        );

    [Fact]
    public void Aliases_ImportedTypes_OntoTheRequiredModule() =>
        AssertGenerated(
            "import type { Scalar } from \"./math\"\nlet total: Scalar = 1;\nprint(total);",
            """
            const math = require("./math")
            type Scalar = math.Scalar
            const total: Scalar = 1
            print(total)
            """
        );

    [Fact]
    public void Aliases_GenericImportedTypes_WithTheirParameters() =>
        AssertGenerated(
            "import type { Box } from \"./math\"\nlet items: Box<number> = mut [1];\nprint(items);",
            """
            const math = require("./math")
            type Box<T> = math.Box<T>
            const items: Box<number> = {1}
            print(items)
            """
        );

    [Fact]
    public void Aliases_ImportedEnum_WithoutBindingAValue() =>
        AssertGenerated(
            "import { Color } from \"./math\"\nlet c: Color = Color.Blue;\nprint(c);",
            """
            const math = require("./math")
            type Color = math.Color
            const c: Color = 2
            print(c)
            """
        );

    [Fact]
    public void Aliases_ImportedInterface_UsedAsBothTypeAndValue() =>
        AssertGenerated(
            "import { Point } from \"./math\"\nlet p: Point = new Point { x: 1, y: 2 };\nprint(p.x);",
            """
            const math = require("./math")
            type Point = math.Point
            const p: Point = { x = 1, y = 2 }
            print(p.x)
            """
        );

    [Fact]
    public void Requires_AModule_OnceForEveryImportDeclarationNamingIt() =>
        AssertGenerated(
            """
            import { pi } from "./math"
            import type { Scalar } from "./math"
            let total: Scalar = pi;
            print(total);
            """,
            """
            const math = require("./math")
            const pi = math.pi
            type Scalar = math.Scalar
            const total: Scalar = pi
            print(total)
            """
        );

    [Fact]
    public void Suffixes_TheModuleLocal_WhenTheNameIsAlreadyDeclared() =>
        AssertGenerated(
            "import { pi } from \"./math\"\nlet math = 1;\nprint(math, pi);",
            """
            const math_1 = require("./math")
            const pi = math_1.pi
            const math = 1
            print(math, pi)
            """
        );

    [Fact]
    public void Requires_TheRuntimeLibrary_BeforeAnyModule() =>
        AssertGenerated(
            "import { pi } from \"./math\"\nevent tick;\nprint(pi);",
            """
            const Loom = require("@game/ReplicatedStorage/include/loom_runtime")
            const math = require("./math")
            const pi = math.pi
            const tick: Loom.Event = Loom.Event.new()
            print(pi)
            """
        );

    [Fact]
    public void Requires_ADirectoryModule_ByItsFolderName() =>
        Utility.WithTempProject(
            [("main.loom", "import { helper } from \"./util\"\nprint(helper);"), (Path.Combine("util", "init.loom"), "export let helper = 2;")],
            (_, result) => AssertRendered(
                result,
                """
                const util = require("./util")
                const helper = util.helper
                print(helper)
                """
            )
        );

    private static void AssertGenerated(string source, string expected) =>
        Utility.WithTempProject(
            [("main.loom", source), ("math.loom", MathModule)],
            (_, result) => AssertRendered(result, expected)
        );

    private static void AssertRendered(CompilationResult result, string expected)
    {
        Utility.AssertNoErrors(result);

        var main = result.Files.Single(file => file.SourceFile.Name == "main.loom");
        Assert.Equal(
            expected.Replace(Environment.NewLine, "\n") + '\n',
            main.RenderedLuau.Replace(Environment.NewLine, "\n")
        );
    }
}