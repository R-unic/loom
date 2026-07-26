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

    public override bool VisitFor(For @for)
    {
        Visit(@for.CollectionExpression);
        PushScope();
        if (@for.Names.Any(name => !DeclareVariable(name, name.Token.Text, SymbolKind.Variable)))
            return false;

        var lastContext = _context;
        _context = ResolverContext.Loop;
        Visit(@for.Body);
        _context = lastContext;

        PopScope();
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
        PushScope();

        var success = Visit(matchArm.Pattern)
            && (matchArm.Guard == null || Visit(matchArm.Guard))
            && Visit(matchArm.Body);

        PopScope();
        return success;
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
        if (@return.FirstAncestorOfType<FunctionDeclaration>() is { } functionDeclaration)
        {
            var after = @return.FirstAncestorOfType<After>();
            if (after == null || functionDeclaration.FirstAncestorOfType<After>() == after)
                return base.VisitReturn(@return);

            _diagnostics.Error(@return, InternalCodes.ReturnInAfter, "Cannot return a value from an 'after' statement body.");
            return false;
        }

        _diagnostics.Error(@return, InternalCodes.ReturnOutsideFunction, "Return statements can only be used inside of functions.");
        return false;
    }
}
