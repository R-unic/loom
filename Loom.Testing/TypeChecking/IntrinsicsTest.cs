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

    /// <remarks>
    ///     'script' answers the same question 'game' does for every project type - every Script,
    ///     LocalScript and ModuleScript has one at runtime, plugins included - so it lives in the
    ///     ambient globals both security levels share rather than either one alone.
    /// </remarks>
    [Theory]
    [InlineData(ProjectType.Game)]
    [InlineData(ProjectType.Plugin)]
    public void Registers_Script_ForEveryProjectType(ProjectType projectType) =>
        Utility.AssertNoErrors(Utility.GetAnalysisDiagnostics("let x: LuaSourceContainer = script;", projectType));

    /// <remarks>'plugin' only exists inside a script a plugin is running, so a game never sees it.</remarks>
    [Fact]
    public void Registers_Plugin_OnlyForThePluginProjectType()
    {
        Utility.AssertNoErrors(Utility.GetAnalysisDiagnostics("let x: Plugin = plugin;", ProjectType.Plugin));

        var diagnostics = Utility.GetAnalysisDiagnostics("let x = plugin;", ProjectType.Game);
        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find name 'plugin'.");
    }

    /// <remarks>
    ///     Ambient names live in a scope below the file's own, so a project is free to use 'script' and
    ///     'plugin' as ordinary identifiers - the same shadowing 'game' already gets - rather than having
    ///     two more reserved words.
    /// </remarks>
    [Fact]
    public void Allows_ScriptAndPlugin_ToBeShadowedByALocalDeclaration() =>
        Utility.AssertNoErrors(
            Utility.GetAnalysisDiagnostics(
                "let script: string = \"not a script\"; let plugin: string = \"not a plugin\"; print(script); print(plugin);",
                ProjectType.Plugin
            )
        );
}
