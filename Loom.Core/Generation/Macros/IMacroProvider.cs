using System.Diagnostics.CodeAnalysis;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Luau.AST;
using ElementAccess = Loom.Luau.AST.ElementAccess;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.Core.Generation.Macros;

internal interface IMacroProvider
{
    // Classification (does this type/expression have a provider at all?) only ever needs the semantic
    // model, unlike the Try* codegen methods below which need the full MacroContext - see
    // InvocationMacroReference, which classifies during type checking with no MacroContext available.
    public bool Supports(SemanticModel semanticModel, Type type);
    public bool Supports(SemanticModel semanticModel, Expression expression);

    public bool IsInvocationOnlyMember(string memberName);

    public bool TryProperty(MacroContext context, string name, LuauExpression target, [MaybeNullWhen(false)] out LuauExpression expression)
    {
        expression = null;
        return false;
    }

    public bool TryInvocation(
        MacroContext context,
        string name,
        TypeArguments? typeArguments,
        Call call,
        [MaybeNullWhen(false)] out LuauExpression expression)
    {
        expression = null;
        return false;
    }

    public bool TryElementAccess(MacroContext context, ElementAccess access, Type targetType, [MaybeNullWhen(false)] out LuauExpression expression)
    {
        expression = null;
        return false;
    }
}