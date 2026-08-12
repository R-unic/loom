using Loom.Core.Parsing.AST;
using Loom.Core.TypeChecking.Types;
using Loom.Luau;
using Loom.Luau.AST;
using FunctionType = Loom.Core.TypeChecking.Types.FunctionType;
using IntersectionType = Loom.Core.TypeChecking.Types.IntersectionType;

namespace Loom.Core.Generation;

/// <summary>
///     Lowers <c>async</c> and <c>await</c>.
/// </summary>
/// <remarks>
///     <para>
///         An <c>async fn</c> emits as an ordinary Luau function: a Luau thread yields transparently, so
///         nothing about the body needs wrapping. The asynchrony lives entirely at the call site - a call
///         becomes <c>Loom.future(callee, ...)</c>, which starts it on a thread of its own.
///     </para>
///     <para>
///         Except when an <c>await</c> consumes the call directly, which is most of them. There, building
///         the future would spawn a thread only for the very next operation to park on it, and starting a
///         function eagerly and then waiting for it is what a plain call already does - so the future is
///         elided and only the call is emitted. That is the same fusion
///         <see cref="TryGeneratePropagatedWrappedCall" /> performs for <c>?</c>, and the two compose: an
///         <c>await …?</c> on a fallible yielding member emits neither a future nor a Result.
///     </para>
/// </remarks>
public sealed partial class LuauGenerator
{
    public override LuauNode VisitAwait(Await @await)
    {
        if (@await.Expression is Invocation invocation && IsFusedAwaitedCall(invocation))
            return Visit(@await.Expression);

        _semanticModel.RuntimeReferences++;
        return LuauFactory.RuntimeLibraryCall(["await"], [(LuauExpression)Visit(@await.Expression)]);
    }

    /// <summary>
    ///     The receiver of a chain step that read through a <c>Future</c>, waited for if it is not a call the
    ///     fusion already collapsed.
    /// </summary>
    /// <remarks>
    ///     A chain of yielding calls needs nothing here - each link is fused, so <c>a.wait_for_child("a")
    ///     .wait_for_child("b")</c> emits the two plain calls. A future that arrived some other way, as a
    ///     stored variable or a parameter, has to actually be waited for before the member exists on it.
    /// </remarks>
    private LuauExpression AccessTarget(Expression access, Expression receiver)
    {
        var generated = (LuauExpression)Visit(receiver);
        if (!IsChainAwaited(access) || receiver is Invocation invocation && IsFusedAwaitedCall(invocation))
            return generated;

        _semanticModel.RuntimeReferences++;
        return LuauFactory.RuntimeLibraryCall(["await"], [generated]);
    }

    /// <summary>
    ///     Whether calling this evaluates to a <c>Future</c>. Read off the callee's type rather than off the
    ///     declaration, so a call through a variable or a parameter is lowered the same way a call by name is.
    /// </summary>
    private bool IsAsyncInvocation(Invocation invocation) =>
        _semanticModel.GetType(invocation.Expression) switch
        {
            FunctionType functionType => functionType.IsAsync,
            IntersectionType { Types.Count: > 0 } intersection => intersection.Types.TrueForAll(t => t is FunctionType { IsAsync: true }),
            _ => false
        };

    /// <summary>
    ///     Whether the future this call would build is elided because something consumes it immediately.
    ///     Both halves of the fusion ask through here - the call skips building the future and the waiter
    ///     skips waiting on one - so they cannot disagree about which calls are fused.
    /// </summary>
    /// <remarks>
    ///     Two consumers qualify. An <c>await</c> directly on the call, and a chain step reading straight
    ///     through it (<c>TypeChecker.TryReadThroughFuture</c>) - in a chain of yielding calls every link but
    ///     the last is consumed by the next one, so the whole chain collapses to the plain Luau calls.
    /// </remarks>
    private bool IsFusedAwaitedCall(Invocation invocation) =>
        IsAsyncInvocation(invocation)
        && (invocation.Parent is Await || invocation.Parent is Expression access && IsChainAwaited(access));

    private bool IsChainAwaited(Expression access) => _semanticModel.ChainAwaitedReceiverTypes.ContainsKey(access.Id);

    /// <summary>
    ///     Starts the call on a thread of its own. <c>Loom.future</c> takes the callee and its arguments
    ///     rather than a closure over them, so a call site allocates the future and its thread and nothing
    ///     else - no per-site function is built.
    /// </summary>
    /// <param name="wrapsErrors">
    ///     Whether the callee raises rather than returning, in which case the raise has to become the
    ///     <c>Result</c> its type already promises instead of rejecting the future. That has to happen on
    ///     the future's own thread, which is why the runtime has a second entry point for it rather than
    ///     this reusing <see cref="GenerateWrappedCall" />.
    /// </param>
    private LuauExpression GenerateFutureCall(Invocation invocation, LuauExpression callee, List<LuauExpression> arguments, bool wrapsErrors)
    {
        List<LuauExpression> callArguments = [callee, ..arguments];

        // a method needs its receiver passed explicitly, since it is being handed over as a value rather
        // than called through ':'. Caching it first keeps 'find_store().get_async(key)' from running
        // 'find_store' twice, once to read the member and once to pass as self.
        if (callee is Luau.AST.PropertyAccess { Target: var instance } access && IsMethodReference(invocation.Expression))
        {
            var cached = _state.PushToVariable("_receiver", instance);
            callArguments = [new Luau.AST.PropertyAccess(cached, access.Names), cached, ..arguments];
        }

        _semanticModel.RuntimeReferences++;
        return LuauFactory.RuntimeLibraryCall([wrapsErrors ? "future_wrapped" : "future"], callArguments);
    }
}
