using Loom.Config;
using Loom.Core.Diagnostics;
using Loom.Core.Pipeline;
using Loom.Core.Resolving;
using Resolver = Loom.Core.Resolving.Resolver;

namespace Loom.Testing;

internal static partial class Utility
{
    public static SemanticModel GetSemanticModel(
        string source,
        bool isDeclaration = false,
        bool disableRuntimeLib = true,
        ProjectType projectType = ProjectType.Game) =>
        GetSemanticModel(source, out _, isDeclaration, disableRuntimeLib, projectType);

    private static SemanticModel GetSemanticModel(
        string source,
        out DiagnosticBag upstream,
        bool isDeclaration = false,
        bool disableRuntimeLib = true,
        ProjectType projectType = ProjectType.Game)
    {
        var parserResult = Parse(source, out upstream);
        if (isDeclaration)
            parserResult.Tree.File.IsDeclaration = true;

        var compilationUnit = new CompilationUnit(new LoomConfig { ProjectType = projectType });
        var semanticModel = new Resolver(parserResult, compilationUnit).Resolve();
        semanticModel.DisableRuntimeLibraryImport = disableRuntimeLib;

        return semanticModel;
    }
}
