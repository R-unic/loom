using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;

namespace Loom.Core.Resolving;

public sealed partial class Resolver
{
    public override bool VisitAfter(After after)
    {
        Visit(after.Duration);

        using var _ = InContext(ResolverContext.Scheduler);
        Visit(after.Body);

        return true;
    }

    public override bool VisitEvery(Every every)
    {
        Visit(every.Duration);
        if (every.Condition != null)
            Visit(every.Condition);

        using var _ = InContext(ResolverContext.Scheduler);
        Visit(every.Body);

        return true;
    }

    public override bool VisitFor(For @for)
    {
        Visit(@for.CollectionExpression);

        using var _ = InScope();
        var namesDeclared = @for.Names.All(name => DeclareVariable(name, name.Token.Text));
        if (!namesDeclared)
            return false;

        using var __ = InContext(ResolverContext.Loop);
        Visit(@for.Body);

        return true;
    }

    public override bool VisitMatchExpression(MatchExpression matchExpression)
    {
        if (!Visit(matchExpression.Expression))
            return false;

        foreach (var arm in matchExpression.Arms)
            if (!Visit(arm))
                return false;

        return true;
    }

    public override bool VisitMatchArm(MatchArm matchArm)
    {
        using var _ = InScope();
        return Visit(matchArm.Pattern)
            && (matchArm.Guard == null || Visit(matchArm.Guard))
            && Visit(matchArm.Body);
    }

    public override bool VisitIf(If @if)
    {
        bool conditionSuccess, thenSuccess;

        // the else branch is resolved outside this scope: a name bound in the condition or the then
        // branch is not in scope for it
        using (var _ = InScope())
        {
            conditionSuccess = Visit(@if.Condition);
            thenSuccess = Visit(@if.ThenBranch);
        }

        var elseSuccess = @if.ElseBranch == null || Visit(@if.ElseBranch);
        return conditionSuccess && thenSuccess && elseSuccess;
    }

    public override bool VisitWhile(While @while)
    {
        Visit(@while.Condition);

        using var _ = InContext(ResolverContext.Loop);
        Visit(@while.Body);

        return true;
    }

    public override bool VisitContinue(Continue @continue)
    {
        if (_context == ResolverContext.Loop)
            return base.VisitContinue(@continue);

        _diagnostics.Error(@continue, InternalCodes.ContinueOutsideLoop, "Continue statements can only be used inside of loops.");
        return false;
    }

    public override bool VisitBreak(Break @break)
    {
        if (_context == ResolverContext.Loop)
            return base.VisitBreak(@break);

        _diagnostics.Error(@break, InternalCodes.BreakOutsideLoop, "Break statements can only be used inside of loops.");
        return false;
    }

    public override bool VisitReturn(Return @return)
    {
        if (@return.FirstAncestorImplementing<IFunctionLike>() is { } enclosingFunction)
        {
            var schedulerAncestor = FirstSchedulerAncestor(@return);
            if (schedulerAncestor == null || FirstSchedulerAncestor(enclosingFunction) == schedulerAncestor)
                return base.VisitReturn(@return);

            var keyword = schedulerAncestor is After ? "after" : "every";
            _diagnostics.Error(@return, InternalCodes.ReturnInAfter, $"Cannot return a value from an '{keyword}' statement body.");
            return false;
        }

        _diagnostics.Error(@return, InternalCodes.ReturnOutsideFunction, "Return statements can only be used inside of functions.");
        return false;
    }

    // '?' desugars to an early 'return' of the unwrapped Result on the error path, so it needs exactly the
    // same function/scheduler-boundary validation VisitReturn does above - an 'after'/'every' body runs as
    // a separate deferred callback, so a 'return' (or the implicit one inside a propagated '?') there
    // doesn't actually exit the enclosing declared function.
    public override bool VisitErrorPropagation(ErrorPropagation errorPropagation)
    {
        if (errorPropagation.FirstAncestorImplementing<IFunctionLike>() is { } enclosingFunction)
        {
            var schedulerAncestor = FirstSchedulerAncestor(errorPropagation);
            if (schedulerAncestor == null || FirstSchedulerAncestor(enclosingFunction) == schedulerAncestor)
                return base.VisitErrorPropagation(errorPropagation);

            var keyword = schedulerAncestor is After ? "after" : "every";
            _diagnostics.Error(errorPropagation, InternalCodes.ErrorPropagationInAfter, $"Cannot use '?' inside an '{keyword}' statement body.");
            return false;
        }

        _diagnostics.Error(errorPropagation, InternalCodes.ErrorPropagationOutsideFunction, "'?' can only be used inside of functions.");
        return false;
    }

    /// <summary>
    ///     Enforces that a yield only happens where the code around it says one can - the same guarantee
    ///     <c>[fallible]</c> gives for raising, and for the same reason: a signature that hides it leaves
    ///     the caller unable to see that it blocks.
    /// </summary>
    /// <remarks>
    ///     Two places are exempt. A function <em>expression</em> is anonymous, so it has no signature for
    ///     'async' to appear on and no caller to propagate to - an event handler runs on a thread Roblox
    ///     owns and is free to yield, which is the same reason <see cref="TypeChecking.TypeChecker" />'s
    ///     EnclosingFallibleCandidate stops at one. An 'after'/'every' body is emitted as its own deferred
    ///     callback, so it too yields on a thread of its own; unlike '?', which needs the enclosing
    ///     function's return, awaiting inside one says nothing about that function.
    /// </remarks>
    public override bool VisitAwait(Await await)
    {
        var enclosingFunction = await.FirstAncestorImplementing<IFunctionLike>();
        var schedulerAncestor = FirstSchedulerAncestor(await);
        if (schedulerAncestor != null && (enclosingFunction == null || FirstSchedulerAncestor(enclosingFunction) != schedulerAncestor))
            return base.VisitAwait(await);

        // Checked ahead of whether the function is async, because a [no_yield] one may not be: the
        // attribute says the thread would yield where Luau raises rather than suspends, and 'async' is
        // no more allowed there than 'await' is.
        if (NoYieldContext(enclosingFunction) is { } noYield)
        {
            _diagnostics.Error(
                await,
                InternalCodes.YieldInNoYieldContext,
                $"'{noYield}' is marked '[no_yield]', so it cannot await.",
                "Luau raises rather than suspends when a thread yields across a C-call boundary - do the waiting before the call, and hand this one the answer"
            );

            return false;
        }

        switch (enclosingFunction)
        {
            case FunctionExpression or FunctionDeclaration { AsyncKeyword: not null }:
                return base.VisitAwait(await);

            case FunctionDeclaration { Name.Text: var name }:
                _diagnostics.Error(
                    await,
                    InternalCodes.AwaitOutsideAsyncFunction,
                    $"'await' can only be used inside an 'async' function, and '{name}' is not one.",
                    $"write 'async fn {name}' - its callers then get a 'Future' and decide when to wait for it"
                );

                return false;

            default:
                _diagnostics.Error(
                    await,
                    InternalCodes.AwaitOutsideAsyncFunction,
                    "'await' can only be used inside an 'async' function.",
                    "yielding here blocks every thread that requires this module - move it into an 'async fn'"
                );

                return false;
        }
    }

    /// <summary>
    ///     The name of the nearest enclosing function declared <c>[no_yield]</c>, or null when nothing between
    ///     here and the top forbids yielding.
    /// </summary>
    /// <remarks>
    ///     A function expression stops the walk. Its body runs on a thread of its own - which is what makes it
    ///     exempt from needing 'async' at all - so a [no_yield] function is not a context an anonymous one
    ///     inherits, any more than it inherits the obligation to propagate.
    /// </remarks>
    private static string? NoYieldContext(Node? enclosingFunction) =>
        enclosingFunction is FunctionDeclaration { Attributes: { } attributes } declaration
        && attributes.AttributeList.Exists(attribute => attribute.Name == "no_yield")
            ? declaration.Name.Text
            : null;

    /// <summary>
    ///     The nearest deferred-execution body (an 'after' or 'every' statement) wrapping <paramref name="node" />,
    ///     if any - a bare 'return' inside one is ambiguous with returning from the enclosing function, so it's
    ///     forbidden entirely.
    /// </summary>
    private static Node? FirstSchedulerAncestor(Node node)
    {
        for (var current = node.Parent; current != null; current = current.Parent)
            if (current is After or Every)
                return current;

        return null;
    }
}
