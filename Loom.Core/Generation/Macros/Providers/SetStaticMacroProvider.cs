using System.Diagnostics.CodeAnalysis;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Core.TypeChecking.Types;
using Loom.Luau.AST;
using BinaryOperator = Loom.Luau.AST.BinaryOperator;
using ElementAccess = Loom.Luau.AST.ElementAccess;
using ExpressionStatement = Loom.Luau.AST.ExpressionStatement;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.Core.Generation.Macros.Providers;

/// <summary>
///     Lowers the <c>Set</c>/<c>MutSet</c> constructors declared in <c>loom.loom</c>. A set is a plain
///     table whose keys are its members, so <c>Set::of(1, 2)</c> is a table literal and needs no runtime
///     support - the same trade the <c>Result</c> constructors make.
/// </summary>
internal sealed class SetStaticMacroProvider : IMacroProvider
{
    public bool Supports(SemanticModel _, Type type) =>
        type is InterfaceType { Name: "Set" or "MutSet", IsIntrinsic: true } or GenericType { UnderlyingType: InterfaceType { Name: "Set" or "MutSet", IsIntrinsic: true } };
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
                expression = call.Arguments.Exists(argument => argument is Spread)
                    ? GenerateSpreadOf(context.State, call.Arguments)
                    : new Table(call.Arguments.ConvertAll(TableInitializer (value) => new ComputedPropertyTableInitializer(value, new BooleanLiteral(true))));

                return true;
            case "empty":
                expression = new Table([]);
                return true;
        }

        expression = null;
        return false;
    }

    /// <summary>
    ///     Builds the set a statement at a time, because a spread carries a count nobody knows until it
    ///     runs and a table literal has to name every key it holds. The members written out still go in
    ///     the literal; each spread becomes a loop adding its elements as keys.
    /// </summary>
    private static LuauExpression GenerateSpreadOf(LuauState state, List<LuauExpression> arguments)
    {
        var members = arguments
            .TakeWhile(argument => argument is not Spread)
            .Select(TableInitializer (value) => new ComputedPropertyTableInitializer(value, new BooleanLiteral(true)))
            .ToList();

        var result = state.PushToVariable(ArrayLowering.ResultName, new Table(members));
        var statements = new List<LuauStatement>();
        foreach (var argument in arguments.Skip(members.Count))
        {
            if (argument is Spread spread)
            {
                ArrayLowering.AddElementsToSet(state, statements, result, spread.Operand);
                continue;
            }

            statements.Add(new ExpressionStatement(new BinaryOperator(new ElementAccess(result, argument), "=", new BooleanLiteral(true))));
        }

        state.Prereq([.. statements]);
        return result;
    }
}
