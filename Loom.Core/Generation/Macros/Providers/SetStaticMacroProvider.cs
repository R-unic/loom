using System.Diagnostics.CodeAnalysis;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Core.TypeChecking.Types;
using Loom.Luau.AST;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.Core.Generation.Macros.Providers;

/// <summary>
///     Lowers the <c>Set</c>/<c>MutSet</c> constructors declared in <c>loom.loom</c>. A set is a plain
///     table whose keys are its members, so <c>Set.of(1, 2)</c> is a table literal and needs no runtime
///     support - the same trade the <c>Result</c> constructors make.
/// </summary>
internal sealed class SetStaticMacroProvider : IMacroProvider
{
    public bool Supports(SemanticModel _, Type type) => type is InterfaceType { Name: "SetStatic" or "MutSetStatic" };
    public bool Supports(SemanticModel _, Expression __) => false;

    public bool IsInvocationOnlyMember(string memberName) => memberName is "of" or "empty";

    public bool TryInvocation(
        MacroContext context,
        string name,
        TypeArguments? typeArguments,
        Call call,
        [MaybeNullWhen(false)] out LuauExpression expression)
    {
        switch (name)
        {
            case "of":
                expression = new Table(call.Arguments.ConvertAll(TableInitializer (value) => new ComputedPropertyTableInitializer(value, new BooleanLiteral(true))));
                return true;
            case "empty":
                expression = new Table([]);
                return true;
        }

        expression = null;
        return false;
    }
}
