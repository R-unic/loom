using System.Diagnostics.CodeAnalysis;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Core.TypeChecking.Types;
using Loom.Luau.AST;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.Core.Generation.Macros.Providers;

internal sealed class ResultStaticMacroProvider : IMacroProvider
{
    public bool Supports(SemanticModel _, Type type) => type is InterfaceType { Name: "Result", IsIntrinsic: true };
    public bool Supports(SemanticModel _, Expression __) => false;

    public bool IsInvocationOnlyMember(string memberName) => memberName is "ok" or "err";

    public bool TryInvocation(
        MacroContext context,
        string name,
        TypeArguments? typeArguments,
        Call call,
        [MaybeNullWhen(false)] out LuauExpression expression)
    {
        switch (name)
        {
            case "ok":
                expression = CreateResultConstructor(call, true);
                return true;
            case "err":
                expression = CreateResultConstructor(call, false);
                return true;
        }

        expression = null;
        return false;
    }

    private static Table CreateResultConstructor(Call call, bool isOk) =>
        new([new PropertyTableInitializer("ok", new BooleanLiteral(isOk)), new PropertyTableInitializer(isOk ? "value" : "error", call.Arguments.Single())]);
}