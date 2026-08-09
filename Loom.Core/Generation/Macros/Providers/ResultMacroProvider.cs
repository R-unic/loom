using System.Diagnostics.CodeAnalysis;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Core.TypeChecking.Types;
using Loom.Luau.AST;
using Type = Loom.Core.TypeChecking.Types.Type;
using UnionType = Loom.Core.TypeChecking.Types.UnionType;
using PropertyAccess = Loom.Luau.AST.PropertyAccess;
using UnaryOperator = Loom.Luau.AST.UnaryOperator;
using ExpressionStatement = Loom.Luau.AST.ExpressionStatement;
using Identifier = Loom.Luau.AST.Identifier;

namespace Loom.Core.Generation.Macros.Providers;

/// <summary>
///     Lowers the combinators declared on <c>ResultOk</c>/<c>ResultError</c> in <c>runtime.loom</c>
///     inline, so a <c>Result</c> stays the two-field table the constructors build rather
///     than a table carrying six closures.
/// </summary>
/// <remarks>
///     Every lowering reads the receiver more than once, so it is cached into a temporary first -
///     otherwise <c>fetch().unwrap()</c> would call <c>fetch</c> twice, once for the check and once
///     for the value.
/// </remarks>
internal sealed class ResultMacroProvider : IMacroProvider
{
    private static readonly HashSet<string> _members = ["unwrap", "expect", "unwrap_or", "unwrap_or_else", "map", "and_then"];

    public bool Supports(SemanticModel _, Type type) => IsResult(type);
    public bool Supports(SemanticModel semanticModel, Expression expression) => IsResult(semanticModel.GetType(expression));

    public bool IsInvocationOnlyMember(string memberName) => _members.Contains(memberName);

    public bool TryInvocation(
        MacroContext context,
        string name,
        TypeArguments? typeArguments,
        Call call,
        [MaybeNullWhen(false)] out LuauExpression expression)
    {
        if (!_members.Contains(name))
        {
            expression = null;
            return false;
        }

        var result = context.State.PushToVariable("_result", MacroContext.GetCallObject(call));
        var isOk = new PropertyAccess(result, ["ok"]);
        var value = new PropertyAccess(result, ["value"]);
        var error = new PropertyAccess(result, ["error"]);

        switch (name)
        {
            case "unwrap":
                expression = Raise(context, isOk, error, value);
                return true;
            case "expect":
                expression = Raise(context, isOk, call.Arguments.Single(), value);
                return true;
            case "unwrap_or":
                expression = new IfExpression(isOk, value, [], call.Arguments.Single());
                return true;
            case "unwrap_or_else":
                expression = new IfExpression(isOk, value, [], new Call(call.Arguments.Single(), [error]));
                return true;
            case "map":
                expression = new IfExpression(
                    isOk,
                    new Table([new PropertyTableInitializer("ok", new BooleanLiteral(true)), new PropertyTableInitializer("value", new Call(call.Arguments.Single(), [value]))]),
                    [],
                    result
                );

                return true;
            case "and_then":
                expression = new IfExpression(isOk, new Call(call.Arguments.Single(), [value]), [], result);
                return true;
        }

        expression = null;
        return false;
    }

    private static LuauExpression Raise(MacroContext context, LuauExpression isOk, LuauExpression raised, LuauExpression value)
    {
        context.State.Prereq(
            new IfStatement(
                new UnaryOperator("not ", isOk),
                new Chunk([new ExpressionStatement(new Call(new Identifier("error"), [raised]))]),
                [],
                null
            )
        );

        return value;
    }

    // 'Result<T, Error>' reaches here as an InstantiatedType over the alias, not as the union it
    // expands to, so the alias has to be expanded before the arms are recognisable.
    private static bool IsResult(Type type) => IsResult(type, 0);

    private static bool IsResult(Type type, int depth) =>
        depth < 4
        && type switch
        {
            InterfaceType { Name: "ResultOk" or "ResultError" } => true,
            InstantiatedType instantiated => IsResult(instantiated.Expand(), depth + 1),
            UnionType union => union.Types.Count > 0 && union.Types.TrueForAll(member => IsResult(member, depth + 1)),
            _ => false
        };
}
