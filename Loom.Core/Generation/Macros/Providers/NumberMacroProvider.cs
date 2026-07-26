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
            expression = LuauFactory.StringCall(
                "sub",
                [access.Target, access.Index, access.Index]
            );

            return true;
        }

        expression = null;
        return false;
    }
}