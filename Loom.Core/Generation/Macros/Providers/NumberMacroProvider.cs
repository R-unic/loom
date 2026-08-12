using System.Diagnostics.CodeAnalysis;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Luau;
using Loom.Luau.AST;
using ElementAccess = Loom.Luau.AST.ElementAccess;
using PrimitiveType = Loom.Core.TypeChecking.Types.PrimitiveType;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.Core.Generation.Macros.Providers;

internal sealed class NumberMacroProvider : IMacroProvider
{
    public bool Supports(SemanticModel _, Type type) => type.IsAssignableTo(PrimitiveType.Number);
    public bool Supports(SemanticModel _, Expression __) => false;

    public bool IsInvocationOnlyMember(string _) => false;

    public bool TryElementAccess(MacroContext context, ElementAccess access, Type targetType, [MaybeNullWhen(false)] out LuauExpression expression)
    {
        if (targetType.IsAssignableTo(PrimitiveType.String))
        {
            // 'sub' takes the same position twice, so an index that does something - 's[next()]' - has
            // to be named first or it would do it once per argument, and with two different answers.
            var index = context.State.PushIfRepeated("_index", access.Index);
            expression = LuauFactory.StringCall("sub", [access.Target, index, index]);

            return true;
        }

        expression = null;
        return false;
    }
}