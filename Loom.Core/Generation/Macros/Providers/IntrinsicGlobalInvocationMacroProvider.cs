using System.Diagnostics.CodeAnalysis;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Luau;
using Loom.Luau.AST;
using Identifier = Loom.Luau.AST.Identifier;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.Core.Generation.Macros.Providers;

internal sealed class IntrinsicGlobalInvocationMacroProvider : IMacroProvider
{
    public bool Supports(SemanticModel _, Type __) => false;

    public bool Supports(SemanticModel semanticModel, Expression expression) =>
        expression is Parsing.AST.Identifier && semanticModel.GetSymbol(expression) is { IsIntrinsic: true };

    public bool IsInvocationOnlyMember(string memberName) => memberName is "string" or "number" or "new_instance" or "get_service";

    public bool TryInvocation(
        MacroContext context,
        string name,
        TypeArguments? typeArguments,
        Call call,
        [MaybeNullWhen(false)] out LuauExpression expression)
    {
        switch (name)
        {
            case "get_service":
                var serviceName = context.GetTextOfOnlyTypeArgument(typeArguments, name);
                expression = LuauFactory.LibraryCall("game", ["GetService"], [new StringLiteral(serviceName)], true);

                return true;
            case "new_instance":
                var instanceName = context.GetTextOfOnlyTypeArgument(typeArguments, name);
                expression = LuauFactory.LibraryCall("Instance", ["new"], [new StringLiteral(instanceName)]);

                return true;
            case "string":
                expression = new Call(new Identifier("tostring"), call.Arguments);
                return true;
            case "number":
                expression = new Call(new Identifier("tonumber"), call.Arguments);
                return true;
        }

        expression = null;
        return false;
    }
}