using Loom.TypeGenerator;
using Loom.TypeGenerator.ApiTypes;
using Loom.TypeGenerator.Generators;

namespace Loom.Testing.FlowAnalysis;

/// <summary>
///     <see cref="ClassUtility.GetSecurity" /> hardcodes a <c>Callback</c> member's <c>Read</c> security to
///     <c>NotAccessibleSecurity</c> - a level nothing is ever generated at - because a callback is assigned
///     to, never read from, so its real security lives on <c>Write</c> instead. Before
///     <see cref="ClassGenerator.IsAccessible" /> existed, every callsite gating member generation asked
///     <c>CanRead</c> regardless of member kind, which made every <c>Callback</c>-typed member in the whole
///     API invisible - <c>RemoteFunction.OnServerInvoke</c>/<c>OnClientInvoke</c>,
///     <c>BindableFunction.OnInvoke</c>, and so on - none of them were ever emitted.
/// </summary>
[Collection("Assembly")]
public class ClassGeneratorTest
{
    private static Function FunctionNamed(string name, string readSecurity = "None") =>
        new() { Name = name, MemberType = "Function", Parameters = [], Security = readSecurity };

    private static Callback CallbackNamed(string name, string writeSecurity = "None") =>
        new() { Name = name, MemberType = "Callback", Parameters = [], Security = writeSecurity };

    private static ClassGenerator GeneratorAt(string security) =>
        new(Path.Combine(Path.GetTempPath(), "loom-generator-test.loom"), new ReflectionMetadataReader("<roblox/>"), [], security);

    [Fact]
    public void ACallbackVisibleAtThisSecurityLevel_IsAccessible()
    {
        var generator = GeneratorAt("None");

        Assert.True(generator.IsAccessible("RemoteFunction", CallbackNamed("OnServerInvoke")));
    }

    [Fact]
    public void ACallbackFromAHigherSecurityLevel_IsNotAccessible()
    {
        var generator = GeneratorAt("None");

        Assert.False(generator.IsAccessible("SomeClass", CallbackNamed("Restricted", "PluginSecurity")));
    }

    /// <summary>Regression: a callback is gated by its own (write) security, not the always-inaccessible read security every callback carries.</summary>
    [Fact]
    public void ACallbackIsGatedByWriteSecurity_NotTheHardcodedUnreadableReadSecurity()
    {
        var generator = GeneratorAt("None");
        var callback = CallbackNamed("OnServerInvoke");

        Assert.True(generator.IsAccessible("RemoteFunction", callback));
        Assert.NotEqual("None", ClassUtility.GetSecurity("RemoteFunction", callback).Read);
    }

    [Fact]
    public void AFunctionIsStillGatedByReadSecurity_AsBefore()
    {
        var generator = GeneratorAt("None");

        Assert.True(generator.IsAccessible("Players", FunctionNamed("GetPlayers")));
        Assert.False(generator.IsAccessible("Players", FunctionNamed("Restricted", "PluginSecurity")));
    }
}
