using System.Diagnostics.CodeAnalysis;
using Loom.Core.Generation.Macros.Providers;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Core.Resolving.Symbols;
using Loom.Core.TypeChecking;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.Core.Generation.Macros;

internal static class InvocationMacroReference
{
    public static bool IsValidReferenceContext(Expression expression, SemanticModel semanticModel)
    {
        if (expression.IsDescendantOf<ArrayLiteral>())
            return false;

        for (Node? node = expression; node is not null; node = node.Parent)
            if (node.Parent is AssignmentOperator assignmentOperator && semanticModel.GetSymbol(assignmentOperator.Left) is { Kind: SymbolKind.Event }
                || node.Parent is Arguments && node.Parent.Parent is Invocation)
                return true;

        return false;
    }

    public static bool IsDirectInvocationCallee(Expression expression) => expression.Parent is Invocation invocation && invocation.Expression == expression;

    public static bool TryClassify(
        SemanticModel semanticModel,
        Expression expression,
        [NotNullWhen(true)] out IMacroProvider? provider,
        [NotNullWhen(true)] out string? memberName)
    {
        provider = null;
        memberName = null;

        return expression switch
        {
            Identifier identifier => TryClassifyIdentifier(identifier, out provider, out memberName),
            QualifiedName qualified => TryClassifyNamedAccess(
                semanticModel,
                qualified.Identifier,
                qualified.Names,
                qualified.Names.Count - 1,
                out provider,
                out memberName
            ),
            PropertyAccess property => TryClassifyNamedAccess(
                semanticModel,
                property.Expression,
                property.Names,
                property.Names.Count - 1,
                out provider,
                out memberName
            ),
            ElementAccess element when semanticModel.GetConstantValue(element.IndexExpression) is string name =>
                TryClassifyElementAccess(semanticModel, element, name, out provider, out memberName),
            _ => false
        };
    }

    private static bool TryClassifyIdentifier(Identifier identifier, out IMacroProvider? provider, out string? memberName)
    {
        provider = null;
        memberName = null;

        var name = identifier.Name.Text;
        if (name is not ("string" or "number"))
            return false;

        provider = MacroExpander.Providers.OfType<IntrinsicGlobalInvocationMacroProvider>().First();
        memberName = name;
        return true;
    }

    private static bool TryClassifyElementAccess(
        SemanticModel semanticModel,
        ElementAccess element,
        string name,
        out IMacroProvider? provider,
        out string? memberName)
    {
        provider = GetProvider(semanticModel, element.Expression);
        if (provider is null || !provider.IsInvocationOnlyMember(name))
        {
            provider = null;
            memberName = null;
            return false;
        }

        memberName = name;
        return true;
    }

    private static bool TryClassifyNamedAccess(
        SemanticModel semanticModel,
        Expression rootExpression,
        List<DotName> names,
        int memberIndex,
        out IMacroProvider? provider,
        out string? memberName)
    {
        provider = null;
        memberName = null;

        if (names.Count == 0)
            return false;

        var currentType = semanticModel.GetType(rootExpression);
        IMacroProvider? foundProvider = null;
        var foundIndex = -1;

        for (var i = 0; i < names.Count; i++)
        {
            if (GetProvider(semanticModel, currentType) is { } macroProvider)
            {
                foundProvider = macroProvider;
                foundIndex = i;
            }

            currentType = TypeSimplifier.GetMemberPropertyType(currentType, names[i].Name.Text);
            if (currentType is null)
                return false;
        }

        if (foundProvider is null || foundIndex != memberIndex)
            return false;

        memberName = names[foundIndex].Name.Text;
        if (!foundProvider.IsInvocationOnlyMember(memberName))
            return false;

        provider = foundProvider;
        return true;
    }

    private static IMacroProvider? GetProvider(SemanticModel semanticModel, Expression receiver) =>
        GetProvider(semanticModel, semanticModel.GetType(receiver)) ?? MacroExpander.Providers.FirstOrDefault(provider => provider.Supports(semanticModel, receiver));

    private static IMacroProvider? GetProvider(SemanticModel semanticModel, Type? type) =>
        type is not null ? MacroExpander.Providers.FirstOrDefault(provider => provider.Supports(semanticModel, type)) : null;
}