using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Luau;
using Loom.Luau.AST;
using BinaryOperator = Loom.Luau.AST.BinaryOperator;
using Continue = Loom.Luau.AST.Continue;
using ElementAccess = Loom.Luau.AST.ElementAccess;
using ExpressionStatement = Loom.Luau.AST.ExpressionStatement;
using Identifier = Loom.Luau.AST.Identifier;
using PropertyAccess = Loom.Luau.AST.PropertyAccess;
using TypeParameter = Loom.Core.TypeChecking.Types.TypeParameter;
using UnaryOperator = Loom.Luau.AST.UnaryOperator;

namespace Loom.Core.Generation.Macros;

internal record MacroContext(SemanticModel SemanticModel, LuauState State, DiagnosticBag Diagnostics)
{
    public Node Node { get; set; } = null!;

    public static LuauExpression GetCallObject(Call call) =>
        call.Callee switch
        {
            ElementAccess elementAccess => LuauFactory.UnwrapParentheses(elementAccess.Target),
            PropertyAccess propertyAccess =>
                propertyAccess.Names.Count > 1
                    ? new PropertyAccess(LuauFactory.UnwrapParentheses(propertyAccess.Target), [.. propertyAccess.Names.SkipLast(1)])
                    : LuauFactory.UnwrapParentheses(propertyAccess.Target),

            var callee => LuauFactory.UnwrapParentheses(callee)
        };

    public static bool TryComputeConstantArithmetic(LuauExpression expression, out double computed)
    {
        computed = -1;
        switch (expression)
        {
            case NumberLiteral literal:
                computed = literal.Value;
                return true;

            case UnaryOperator { Operator: "-" } unary:
            {
                if (!TryComputeConstantArithmetic(unary.Operand, out var operand))
                    return false;

                computed = -operand;
                return true;
            }
            case BinaryOperator { Operator: "+" or "-" or "*" or "/" or "//" or "^" or "%" } binary:
            {
                if (!TryComputeConstantArithmetic(binary.Left, out var left))
                    return false;

                if (!TryComputeConstantArithmetic(binary.Right, out var right))
                    return false;

                computed = binary.Operator switch
                {
                    "+" => left + right,
                    "-" => left - right,
                    "*" => left * right,
                    "/" => left / right,
                    "//" => Math.Floor(left / right),
                    "^" => Math.Pow(left, right),
                    "%" => left % right,
                    _ => -1
                };

                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Builds the filtered array the instance macros return.
    /// </summary>
    /// <remarks>
    ///     The source is hoisted so its length is available, which lets the result be allocated at
    ///     the only size it can possibly need - the filter keeps at most every element - instead of
    ///     growing a table one <c>table.insert</c> at a time. Writing through a counter rather than
    ///     <c>table.insert</c> also avoids re-deriving the append position on each hit. Neither is
    ///     something Luau can do for itself: it cannot know the bound, and cannot know the result
    ///     table does not escape before the loop ends.
    /// </remarks>
    public Identifier FilterResults(LuauExpression collection, LuauType resultType, Func<Identifier, LuauExpression> condition)
    {
        const string childName = "child";
        var childIdentifier = new Identifier(childName);
        var sourceIdentifier = State.PushToVariable("_source", collection);
        var resultIdentifier = State.PushToVariable(
            "_result",
            LuauFactory.TableCall("create", [new UnaryOperator("#", sourceIdentifier)]),
            TableType.Array(resultType)
        );

        var countIdentifier = State.PushToVariable("_count", new NumberLiteral(0), isConst: false);
        State.Prereq(
            new ForStatement(
                ["_", childName],
                sourceIdentifier,
                new Chunk(
                    [
                        new IfStatement(
                            new UnaryOperator("not ", condition(childIdentifier)),
                            new Chunk([new Continue()]),
                            [],
                            null
                        ),
                        new ExpressionStatement(new BinaryOperator(countIdentifier, "+=", new NumberLiteral(1))),
                        new ExpressionStatement(new BinaryOperator(new ElementAccess(resultIdentifier, countIdentifier), "=", childIdentifier))
                    ]
                )
            )
        );

        return resultIdentifier;
    }

    public Call TypeArgumentAsStringCall(string name, string newName, TypeArguments? typeArguments, LuauExpression instance, bool isMethod = true)
    {
        var instanceName = GetTextOfOnlyTypeArgument(typeArguments, name);
        return new Call(new PropertyAccess(instance, [newName]), [new StringLiteral(instanceName)], isMethod);
    }

    public string GetTextOfOnlyTypeArgument(TypeArguments? typeArguments, string fnName) => MaybeGetTextOfOnlyTypeArgument(typeArguments, fnName)!;

    public string? MaybeGetTextOfOnlyTypeArgument(TypeArguments? typeArguments, string fnName)
    {
        var typeName = typeArguments?.ArgumentsList.FirstOrDefault();
        if (typeName == null)
            return null;

        var instanceType = SemanticModel.GetType(typeName);
        if (instanceType is not TypeParameter)
            return typeName.ToString();

        Diagnostics.Error(
            typeName,
            InternalCodes.AbstractTypeParameterInMacro,
            $"Cannot use type parameter '{typeName}' with '{fnName}::<T>()' macro."
        );

        return null;
    }
}