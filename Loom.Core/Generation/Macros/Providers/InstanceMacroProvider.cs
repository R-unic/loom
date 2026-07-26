using System.Diagnostics.CodeAnalysis;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Core.TypeChecking.Types;
using Loom.Luau.AST;
using Identifier = Loom.Luau.AST.Identifier;
using PropertyAccess = Loom.Luau.AST.PropertyAccess;
using Type = Loom.Core.TypeChecking.Types.Type;
using TypeName = Loom.Luau.AST.TypeName;

namespace Loom.Core.Generation.Macros.Providers;

internal sealed class InstanceMacroProvider : IMacroProvider
{
    public bool Supports(SemanticModel _, Type type) =>
        type is InterfaceType interfaceType && interfaceType.MatchOrMatchConstraint(i => i.Name is "Instance" or "Object");

    public bool Supports(SemanticModel _, Expression __) => false;

    public bool IsInvocationOnlyMember(string memberName) =>
        memberName is "is_a" or "find_first_child_of_class" or "find_first_child_which_is_a" or "find_first_ancestor_of_class" or "find_first_ancestor_which_is_a";

    public bool TryInvocation(
        MacroContext context,
        string name,
        TypeArguments? typeArguments,
        Call call,
        [MaybeNullWhen(false)] out LuauExpression expression)
    {
        expression = null;
        var instance = MacroContext.GetCallObject(call);
        switch (name)
        {
            case "get_children":
                expression = filterDescendants("GetChildren");
                return expression != null;
            case "get_descendants":
                expression = filterDescendants("GetDescendants");
                return expression != null;
            case "is_a":
                expression = context.TypeArgumentAsStringCall(name, "IsA", typeArguments, instance);
                return true;
            case "find_first_child_of_class":
                expression = context.TypeArgumentAsStringCall(name, "FindFirstChildOfClass", typeArguments, instance);
                return true;
            case "find_first_child_which_is_a":
                expression = context.TypeArgumentAsStringCall(name, "FindFirstChildWhichIsA", typeArguments, instance);
                return true;
            case "find_first_ancestor_of_class":
                expression = context.TypeArgumentAsStringCall(name, "FindFirstAncestorOfClass", typeArguments, instance);
                return true;
            case "find_first_ancestor_which_is_a":
                expression = context.TypeArgumentAsStringCall(name, "FindFirstAncestorWhichIsA", typeArguments, instance);
                return true;
        }

        return false;

        Identifier? filterDescendants(string getterName)
        {
            var ofType = context.MaybeGetTextOfOnlyTypeArgument(typeArguments, name);
            if (ofType == null)
                return null;

            var instanceChildren = new Call(new PropertyAccess(instance, [getterName]), [], true);
            return context.FilterResults(
                instanceChildren,
                new TypeName(ofType),
                child => new Call(new PropertyAccess(child, ["IsA"]), [new StringLiteral(ofType)], true)
            );
        }
    }
}