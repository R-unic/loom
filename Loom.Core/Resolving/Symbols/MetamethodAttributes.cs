using Loom.Core.Parsing.AST;
using Attribute = Loom.Core.Parsing.AST.Attribute;

namespace Loom.Core.Resolving.Symbols;

internal static class MetamethodAttributes
{
    public static Dictionary<string, string> Collect<TMember>(
        IEnumerable<TMember> members,
        Func<TMember, string> methodName,
        Func<TMember, Attributes?> attributes
    )
    {
        var metamethods = new Dictionary<string, string>();
        foreach (var member in members)
        {
            var attribute = attributes(member)?.AttributeList.Find(IsMetamethodAttribute);
            if (attribute != null && TryGetMetamethodName(attribute, out var metamethodName))
                metamethods[metamethodName] = methodName(member);
        }

        return metamethods;
    }

    private static bool IsMetamethodAttribute(Attribute attribute) =>
        attribute.Name == "luau_metamethod";

    private static bool TryGetMetamethodName(Attribute attribute, out string metamethodName)
    {
        if (attribute.Arguments.ArgumentList is [Literal { Value: string value }])
        {
            metamethodName = value;
            return true;
        }

        metamethodName = "";
        return false;
    }
}
