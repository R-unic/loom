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
    ///     The <c>T</c> of a <c>Future&lt;T&gt;</c>, however that future arrived here.
    /// </summary>
    /// <remarks>
    ///     <see cref="TypeSolver" />'s <c>Substitute</c> expands a generic whose arguments are all resolved
    ///     into its body, so a future held in a variable reaches this as the <c>Future</c> interface rather
    ///     than as the instantiation - which is why <c>Future.value</c> is declared <c>T?</c>, and why the
    ///     expanded form is read back off it. Matching that form by name is the same compromise
    ///     <see cref="Generation.Macros.Providers.ResultMacroProvider" /> makes for <c>ResultOk</c>.
    /// </remarks>
    private bool TryGetFutureValueType(Node failNode, Type type, [MaybeNullWhen(false)] out Type value)
    {
        switch (type)
        {
            case InstantiatedType instantiated when instantiated.GenericType.Equals(GetGenericFutureType(failNode)) && instantiated.Arguments.Count == 1:
                value = instantiated.Arguments[0];
                return true;

            case InterfaceType { Name: "Future" } interfaceType when interfaceType.Properties.Find(property => property.Name == "value") is { } settled:
                value = settled.ValueType.NonNullable();
                return true;

            default:
                value = null;
                return false;
        }
    }

    private GenericType GetGenericFutureType(Node failNode) => GetIntrinsicType<GenericType>(failNode, "Future");
}
