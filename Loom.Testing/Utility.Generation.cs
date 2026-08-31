using Loom.Config;
using Loom.Core.Diagnostics;
using Loom.Core.Generation;
using Loom.Core.TypeChecking;
using Loom.Luau.AST;

namespace Loom.Testing;

internal static partial class Utility
{
    public static LuauTree GetLuauAST(string source, bool typeCheck = false, bool disableRuntimeLib = true) =>
        Generate(source, typeCheck, disableRuntimeLib).LuauTree;

    public static DiagnosticBag GetGeneratorDiagnostics(string source, bool typeCheck = false) => Generate(source, typeCheck).Diagnostics;

    /// <summary>
    ///     Renders <paramref name="source" /> with an ambient 'world' in scope, for the generator cases that
    ///     need a real Instance to hang the instance macros off.
    /// </summary>
    public static string GenerateAgainstWorkspace(string source)
    {
        const string declaration = "declare let world: Workspace;\n";
        AssertNoErrors(GetGeneratorDiagnostics(declaration + source, typeCheck: true));

        return GetLuauAST(declaration + source, typeCheck: true).Render();
    }

    private static LuauGeneratorResult Generate(string source, bool typeCheck = false, bool disableRuntimeLib = true)
    {
        var (_, semanticModel, flowAnalyzer) = FlowAnalyze(source, out var upstream, disableRuntimeLib);
        if (typeCheck)
        {
            var typeChecker = new TypeChecker(semanticModel, flowAnalyzer);
            upstream = DiagnosticBag.Concat([upstream, typeChecker.Check().Diagnostics]);
        }

        var result = new LuauGenerator(semanticModel).Generate();
        return result with { Diagnostics = DiagnosticBag.Concat([upstream, result.Diagnostics]) };
    }

    /// <summary>
    ///     Everything the stages after the parser report for <paramref name="source" />, merged into one bag
    ///     the way a build shows them. A name every stage looks up is only reported once from any single
    ///     stage's bag, so proving it is not reported twice takes all of them together.
    /// </summary>
    public static DiagnosticBag GetAnalysisDiagnostics(string source, ProjectType projectType = ProjectType.Game)
    {
        var (analyzerResult, semanticModel, flowAnalyzer) = FlowAnalyze(source, projectType: projectType);
        var typeCheckerDiagnostics = new TypeChecker(semanticModel, flowAnalyzer).Check().Diagnostics;
        var generatorDiagnostics = new LuauGenerator(semanticModel).Generate().Diagnostics;

        return DiagnosticBag.Concat([semanticModel.Diagnostics, analyzerResult.Diagnostics, typeCheckerDiagnostics, generatorDiagnostics]);
    }
}
