using Loom.Config;
using Loom.Core.Diagnostics;
using Loom.Core.TypeChecking;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.Testing;

internal static partial class Utility
{
    /// <summary>
    ///     The type of <paramref name="source" />'s last statement. Source that does not lex and parse
    ///     cleanly fails here rather than answering: the parser's recovery node types as 'never', which a
    ///     test asserting only the type would otherwise read as a real inference result.
    /// </summary>
    public static Type GetLastStatementType(string source)
    {
        var result = TypeCheck(source, out var upstream);
        AssertNoErrors(upstream);

        return result.ReturnType;
    }

    internal static TypeCheckerResult TypeCheck(string source, ProjectType projectType = ProjectType.Game) => TypeCheck(source, out _, projectType);

    private static TypeCheckerResult TypeCheck(string source, out DiagnosticBag upstream, ProjectType projectType = ProjectType.Game)
    {
        var (_, semanticModel, flowAnalyzer) = FlowAnalyze(source, out upstream, projectType: projectType);
        var result = new TypeChecker(semanticModel, flowAnalyzer).Check();

        return result with { Diagnostics = DiagnosticBag.Concat([upstream, result.Diagnostics]) };
    }

    public static DiagnosticBag GetTypeCheckerDiagnostics(string source, ProjectType projectType = ProjectType.Game) =>
        TypeCheck(source, projectType).Diagnostics;
}
