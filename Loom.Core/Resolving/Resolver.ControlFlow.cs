using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;

namespace Loom.Core.Resolving;

public sealed partial class Resolver
{
    public override bool VisitAfter(After after)
    {
        Visit(after.Duration);

        var lastContext = _context;
        _context = ResolverContext.Scheduler;
        Visit(after.Body);
        _context = lastContext;

        return true;
    }

    public override bool VisitEvery(Every every)
    {
        Visit(every.Duration);
        if (every.Condition != null)
            Visit(every.Condition);

        var lastContext = _context;
        _context = ResolverContext.Scheduler;
        Visit(every.Body);
        _context = lastContext;

        return true;
    }

    public override bool VisitFor(For @for)
    {
        Visit(@for.CollectionExpression);
        PushScope();
        var namesDeclared = !@for.Names.Any(name => !DeclareVariable(name, name.Token.Text));
        if (namesDeclared)
        {
            var lastContext = _context;
            _context = ResolverContext.Loop;
            Visit(@for.Body);
            _context = lastContext;
        }

        PopScope();
        return namesDeclared;
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
        PushScope();

        var success = Visit(matchArm.Pattern)
            && (matchArm.Guard == null || Visit(matchArm.Guard))
            && Visit(matchArm.Body);

        PopScope();
        return success;
    }

    public override bool VisitIf(If @if)
    {
        PushScope();
        var conditionSuccess = Visit(@if.Condition);
        var thenSuccess = Visit(@if.ThenBranch);
        PopScope();

        var elseSuccess = @if.ElseBranch == null || Visit(@if.ElseBranch);
        return conditionSuccess && thenSuccess && elseSuccess;
    }

    public override bool VisitWhile(While @while)
    {
        Visit(@while.Condition);

        var lastContext = _context;
        _context = ResolverContext.Loop;
        Visit(@while.Body);
        _context = lastContext;

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
    ///     owns and is free to yield, which is the same reason <see cref="TypeChecker" />'s
    ///     EnclosingFallibleCandidate stops at one. An 'after'/'every' body is emitted as its own deferred
    ///     callback, so it too yields on a thread of its own; unlike '?', which needs the enclosing
    ///     function's return, awaiting inside one says nothing about that function.
    /// </remarks>
    public override bool VisitAwait(Await @await)
    {
        var enclosingFunction = @await.FirstAncestorImplementing<IFunctionLike>();
        var schedulerAncestor = FirstSchedulerAncestor(@await);
        if (schedulerAncestor != null && (enclosingFunction == null || FirstSchedulerAncestor(enclosingFunction) != schedulerAncestor))
            return base.VisitAwait(@await);

        switch (enclosingFunction)
        {
            case FunctionExpression or FunctionDeclaration { AsyncKeyword: not null }:
                return base.VisitAwait(@await);

            case FunctionDeclaration { Name.Text: var name }:
                _diagnostics.Error(
                    @await,
                    InternalCodes.AwaitOutsideAsyncFunction,
                    $"'await' can only be used inside an 'async' function, and '{name}' is not one.",
                    $"write 'async fn {name}' - its callers then get a 'Future' and decide when to wait for it"
                );

                return false;

            default:
                _diagnostics.Error(
                    @await,
                    InternalCodes.AwaitOutsideAsyncFunction,
                    "'await' can only be used inside an 'async' function.",
                    "yielding here blocks every thread that requires this module - move it into an 'async fn'"
                );

                return false;
        }
    }

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
