using Loom.Luau.AST;

namespace Loom.Core.Generation.Macros;

/// <summary>
///     Collects every name an inlined callback body mentions, so a fused pipeline can tell whether
///     binding one stage's parameter would capture a name a later stage meant to read from outside
///     the loop.
/// </summary>
/// <remarks>
///     Unfused, each stage owns a loop of its own and its parameter cannot outlive it. Fusing puts
///     them in one body, where <c>select(fn(n) -&gt; n * 2).where(fn(y) -&gt; y &gt; n)</c> would bind
///     <c>n</c> to the mapped value and silently steer the <c>n</c> in the second callback away from
///     the one it was written against. The scan is deliberately blunt: it collects every identifier
///     rather than only free ones, and <see cref="TryCollect" /> answers false for any node shape it
///     does not model. Both make it over-report, which costs a stage its inlining and never costs
///     correctness - the wrong direction to be wrong in is the one that miscompiles.
/// </remarks>
internal static class LuauIdentifiers
{
    public static bool TryCollect(LuauNode? node, HashSet<string> names)
    {
        switch (node)
        {
            case null:
            case NumberLiteral:
            case StringLiteral:
            case BooleanLiteral:
            case NilLiteral:
            case Break:
            case Continue:
            case NoOpStatement:
            case Comment:
                return true;

            case Identifier identifier:
                names.Add(identifier.Name);
                return true;

            case Parenthesized parenthesized:
                return TryCollect(parenthesized.Expression, names);

            case PropertyAccess propertyAccess:
                return TryCollect(propertyAccess.Target, names);

            case ElementAccess elementAccess:
                return TryCollect(elementAccess.Target, names) && TryCollect(elementAccess.Index, names);

            case TypeCast cast:
                return TryCollect(cast.Expression, names);

            case UnaryOperator unary:
                return TryCollect(unary.Operand, names);

            case BinaryOperator binary:
                return TryCollect(binary.Left, names) && TryCollect(binary.Right, names);

            case Call call:
                return TryCollect(call.Callee, names) && TryCollectAll(call.Arguments, names);

            case Table table:
                return TryCollectAll(table.Initializers, names);

            case ComputedPropertyTableInitializer computed:
                return TryCollect(computed.Key, names) && TryCollect(computed.Value, names);

            case TableInitializer initializer:
                return TryCollect(initializer.Value, names);

            case InterpolatedString interpolated:
                return interpolated.Segments.TrueForAll(
                    segment => segment is not InterpolatedStringExpressionSegment expression || TryCollect(expression.Expression, names)
                );

            case IfExpression ifExpression:
                return TryCollect(ifExpression.Condition, names)
                    && TryCollect(ifExpression.ThenBranch, names)
                    && ifExpression.ElseIfBranches.TrueForAll(branch => TryCollect(branch.Condition, names) && TryCollect(branch.Branch, names))
                    && TryCollect(ifExpression.ElseBranch, names);

            case AnonymousFunction function:
            {
                names.UnionWith(function.Parameters.ConvertAll(parameter => parameter.Name));
                return TryCollect(function.Body, names);
            }
            case Function function:
            {
                names.Add(function.Name);
                names.UnionWith(function.Parameters.ConvertAll(parameter => parameter.Name));
                return TryCollect(function.Body, names);
            }

            case Chunk chunk:
                return TryCollectAll(chunk.Statements, names);

            case Do @do:
                return TryCollect(@do.Body, names);

            case ExpressionStatement statement:
                return TryCollect(statement.Expression, names);

            case ConstVariable variable:
            {
                names.Add(variable.Name);
                return TryCollect(variable.Initializer, names);
            }
            case LocalVariable variable:
            {
                names.Add(variable.Name);
                return TryCollect(variable.Initializer, names);
            }
            case MultiConstVariable variable:
            {
                names.UnionWith(variable.Names);
                return TryCollect(variable.Initializer, names);
            }

            case Return @return:
                return TryCollect(@return.Expression, names);

            case MultiReturn multiReturn:
                return TryCollectAll(multiReturn.Expressions, names);

            case IfStatement ifStatement:
                return TryCollect(ifStatement.Condition, names)
                    && TryCollect(ifStatement.ThenBranch, names)
                    && ifStatement.ElseIfBranches.TrueForAll(branch => TryCollect(branch.Condition, names) && TryCollect(branch.Branch, names))
                    && TryCollect(ifStatement.ElseBranch, names);

            case WhileStatement whileStatement:
                return TryCollect(whileStatement.Condition, names) && TryCollect(whileStatement.Body, names);

            case ForStatement forStatement:
            {
                names.UnionWith(forStatement.Names);
                return TryCollect(forStatement.Expression, names) && TryCollect(forStatement.Body, names);
            }
            case NumericForStatement numericFor:
            {
                names.Add(numericFor.Name);
                return TryCollect(numericFor.Start, names)
                    && TryCollect(numericFor.End, names)
                    && TryCollect(numericFor.IncrementBy, names)
                    && TryCollect(numericFor.Body, names);
            }
        }

        return false;
    }

    private static bool TryCollectAll<T>(List<T> nodes, HashSet<string> names) where T : LuauNode =>
        nodes.TrueForAll(node => TryCollect(node, names));
}
