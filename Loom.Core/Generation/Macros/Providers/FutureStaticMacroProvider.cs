using System.Diagnostics.CodeAnalysis;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Core.TypeChecking.Types;
using Loom.Luau;
using Loom.Luau.AST;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.Core.Generation.Macros.Providers;

/// <summary>
///     Routes the <c>Future</c> statics declared in <c>runtime.loom</c> to the runtime functions that
///     implement them.
/// </summary>
/// <remarks>
///     Unlike <see cref="ResultMacroProvider" />, nothing here is inlined: settling a future has to release
///     the threads parked on it, so there is real work behind each of these rather than a table shape a
///     macro could build at the call site. The macro only renames.
/// </remarks>
internal sealed class FutureStaticMacroProvider : IMacroProvider
{
    private static readonly Dictionary<string, string> _members = new()
    {
        ["all"] = "future_all",
        ["race"] = "future_race",
        ["resolved"] = "future_resolved",
        ["rejected"] = "future_rejected"
    };

    public bool Supports(SemanticModel _, Type type) => type is InterfaceType { Name: "FutureStatic" };
    public bool Supports(SemanticModel _, Expression __) => false;

    public bool IsInvocationOnlyMember(string memberName) => _members.ContainsKey(memberName);

    public bool TryInvocation(
        MacroContext context,
        string name,
        TypeArguments? typeArguments,
        Call call,
        [MaybeNullWhen(false)] out LuauExpression expression)
    {
        if (!_members.TryGetValue(name, out var runtimeName))
        {
            expression = null;
            return false;
        }

        context.SemanticModel.RuntimeReferences++;
        expression = LuauFactory.RuntimeLibraryCall([runtimeName], call.Arguments);

        return true;
    }
}
