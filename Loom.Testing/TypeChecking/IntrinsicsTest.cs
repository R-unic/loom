using Loom.Config;
using Loom.Core.Diagnostics;
using Loom.Testing;

namespace Loom.Testing.TypeChecking;

[Collection("Assembly")]
public class IntrinsicsTest
{
    /// <summary>
    ///     Intrinsics.Register caches the ambient symbol set per <see cref="ProjectType" />, because
    ///     PluginSecurity.loom and None.loom are only included for some project types. Compiling one
    ///     project type after another in the same process must not leak one's cached ambient globals into
    ///     the other's - if the cache were keyed globally instead, whichever project type compiled first
    ///     in the process would silently decide what every later project type sees.
    /// </summary>
    [Theory]
    [InlineData(ProjectType.Plugin, "ChangeHistoryService", "Backpack")]
    [InlineData(ProjectType.Game, "Backpack", "ChangeHistoryService")]
    public void Registers_ProjectTypeSpecificIntrinsics_Independently(ProjectType projectType, string included, string excluded)
    {
        Utility.AssertNoErrors(Utility.GetAnalysisDiagnostics($"type X = {included};", projectType));

        var diagnostics = Utility.GetAnalysisDiagnostics($"type X = {excluded};", projectType);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, $"Cannot find type '{excluded}'.");
    }
}
