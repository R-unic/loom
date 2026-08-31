using Loom.Config;
using Loom.Core.Diagnostics;
using Loom.Core.FlowAnalysis;
using Loom.Core.Resolving;

namespace Loom.Testing;

internal static partial class Utility
{
    public static (FlowAnalyzerResult AnalyzerResult, SemanticModel SemanticModel, FlowAnalyzer Analyzer) FlowAnalyze(
        string source,
        bool disableRuntimeLib = true,
        ProjectType projectType = ProjectType.Game) =>
        FlowAnalyze(source, out _, disableRuntimeLib, projectType);

    private static (FlowAnalyzerResult AnalyzerResult, SemanticModel SemanticModel, FlowAnalyzer Analyzer) FlowAnalyze(
        string source,
        out DiagnosticBag upstream,
        bool disableRuntimeLib = true,
        ProjectType projectType = ProjectType.Game)
    {
        var semanticModel = GetSemanticModel(source, out upstream, disableRuntimeLib: disableRuntimeLib, projectType: projectType);
        var flowAnalyzer = new FlowAnalyzer(semanticModel);
        var result = flowAnalyzer.Analyze();
        return (result, semanticModel, flowAnalyzer);
    }
}
