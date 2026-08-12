using System.Diagnostics.CodeAnalysis;
using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.TypeChecking.Types;

namespace Loom.Core.TypeChecking;

using Type = Types.Type;

/// <summary>
///     Types <c>await</c>, and the <c>Future</c> a call to an <c>async fn</c> produces for it to consume.
/// </summary>
/// <remarks>
///     A <c>Future</c> is the intrinsic generic declared in <c>runtime.loom</c>, reached the same way
///     <c>Event</c> is - by identity against the intrinsic definition rather than by name, so a project
///     declaring its own <c>Future</c> does not accidentally satisfy <c>await</c>.
/// </remarks>
public sealed partial class TypeChecker
{
    public override Type VisitAwait(Await @await)
    {
        var operandType = Visit(@await.Expression);

        // an operand that already failed to type has reported once; a second error naming 'never' would
        // only bury the first
        if (Type.IsNever(operandType))
            return BindType(@await, Types.PrimitiveType.Never);

        if (!TryGetFutureValueType(@await, operandType, out var valueType))
        {
            _diagnostics.Error(
                @await,
                InternalCodes.AwaitRequiresFuture,
                $"'await' can only be used on a 'Future<T>', but got '{operandType}'.",
                "a Future comes from calling an 'async fn' - this value is already here, so there is nothing to wait for"
            );

            return BindType(@await, Types.PrimitiveType.Never);
        }

        return BindType(@await, valueType);
    }

    /// <summary>
    ///     Reads a <c>Future</c> receiver through to its value when the enclosing <c>await</c> covers it, so
    ///     one <c>await</c> serves a whole chain of yielding calls instead of one set of parentheses per step.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Two conditions, and both matter. The access has to be the callee of an invocation - reading a
    ///         <em>field</em> off a future still takes the parenthesised form, which keeps the read-through
    ///         tied to "another suspension point follows" rather than to member access in general, and leaves
    ///         <c>future.status</c> meaning what it says. And it has to sit on the awaited expression's own
    ///         spine, reached from the <c>await</c> through callees and receivers only, so an argument buried
    ///         inside the chain is not quietly awaited along with it.
    ///     </para>
    ///     <para>
    ///         What the <c>await</c> keeps promising is that the expression parks the thread; what it stops
    ///         saying is how many times. That is the trade - the failure this exists to prevent is not
    ///         knowing a call blocks at all.
    ///     </para>
    /// </remarks>
    private bool TryReadThroughFuture(Expression accessExpression, Type receiverType, int nameCount, [MaybeNullWhen(false)] out Type value)
    {
        value = null;
        if (nameCount != 1 || !IsCalleeOfAwaitedChain(accessExpression) || !TryGetFutureValueType(accessExpression, receiverType, out var settled))
            return false;

        _semanticModel.ChainAwaitedReceiverTypes[accessExpression.Id] = settled;
        value = settled;

        return true;
    }

    private static bool IsCalleeOfAwaitedChain(Expression accessExpression)
    {
        if (accessExpression.Parent is not Invocation invocation || invocation.Expression != accessExpression)
            return false;

        for (Node current = invocation; current.Parent is { } parent; current = parent)
            switch (parent)
            {
                case Await:
                    return true;

                // the spine, and only the spine: a callee's own expression and a member access's receiver
                case Invocation { Expression: var callee } when callee == current:
                case PropertyAccess { Expression: var receiver } when receiver == current:
                    continue;

                default:
                    return false;
            }

        return false;
    }

    /// <summary>
    ///     Whether calling something of this type hands back a <c>Future</c> rather than the return type
    ///     itself. An overload set answers yes only when every candidate does: which one a call picks is not
    ///     known here, and the Roblox surface never mixes the two on one member anyway.
    /// </summary>
    private static bool IsAsyncCallee(Type calleeType) =>
        calleeType switch
        {
            Types.FunctionType functionType => functionType.IsAsync,
            Types.IntersectionType { Types.Count: > 0 } intersection => intersection.Types.TrueForAll(t => t is Types.FunctionType { IsAsync: true }),
            _ => false
        };

    private InstantiatedType InstantiateFutureType(Node failNode, Type valueType) => GetGenericFutureType(failNode).Construct([valueType]);

    private bool IsFutureType(Node failNode, Type type) => TryGetFutureValueType(failNode, type, out _);

    /// <summary>
    ///     The <c>T</c> of a <c>Future&lt;T&gt;</c>, however that future arrived here - including out of a
    ///     variable, which used to reach this as the expanded <c>Future</c> interface with the
    ///     instantiation, and so <c>T</c>, already gone (see #198).
    /// </summary>
    private bool TryGetFutureValueType(Node failNode, Type type, [MaybeNullWhen(false)] out Type value)
    {
        if (type is InstantiatedType instantiated && instantiated.GenericType.Equals(GetGenericFutureType(failNode)) && instantiated.Arguments.Count == 1)
        {
            value = instantiated.Arguments[0];
            return true;
        }

        value = null;
        return false;
    }

    private GenericType GetGenericFutureType(Node failNode) => GetIntrinsicType<GenericType>(failNode, "Future");
}
