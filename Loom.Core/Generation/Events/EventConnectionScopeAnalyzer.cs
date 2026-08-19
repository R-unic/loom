using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Core.Resolving.Symbols;
using Loom.Core.Text;

namespace Loom.Core.Generation.Events;

/// <summary>
///     Resolves which event a '+='/'-='/'^=' targets, and performs the static, Luau-scope-reachability
///     analysis <see cref="Generation.LuauGenerator" /> needs to decide whether an event connection can become a
///     plain Luau local instead of an entry in the hidden per-event connection store. Pure functions of
///     a <see cref="Resolving.SemanticModel" />/the source tree - no generation state involved.
/// </summary>
internal static class EventConnectionScopeAnalyzer
{
    public static EventTarget? ResolveEventTarget(SemanticModel semanticModel, Expression left)
    {
        if (semanticModel.GetSymbol(left) is { Kind: SymbolKind.Event } globalEventSymbol)
            return new EventTarget(null, globalEventSymbol);

        if (semanticModel.GetNamespaceMemberSymbol(left) is { Kind: SymbolKind.Event } namespaceEventSymbol)
            return new EventTarget(null, namespaceEventSymbol);

        if (semanticModel.GetPropertySymbol(left) is not { Kind: SymbolKind.Event } propertySymbol)
            return null;

        return new EventTarget(GetInstanceKey(semanticModel, left), propertySymbol);
    }

    /// <summary>
    ///     The function a '+='/'-='/'^=' connects or disconnects, whether it is named directly or read off a
    ///     namespace import - both name one function object, which is what a connection is tracked by. A
    ///     member of anything else is not one: it becomes a fresh closure at every connection.
    /// </summary>
    public static Symbol? ResolveConnectionFunction(SemanticModel semanticModel, Expression function) =>
        function is Identifier identifier ? semanticModel.GetSymbol(identifier) : semanticModel.GetNamespaceMemberSymbol(function);

    private static object? GetInstanceKey(SemanticModel semanticModel, Expression left) =>
        left switch
        {
            PropertyAccess { Expression: Identifier identifier } => semanticModel.GetSymbol(identifier),
            QualifiedName { Identifier: var identifier } => semanticModel.GetSymbol(identifier),
            _ => new object()
        };

    /// <summary>
    ///     Finds every (event, function) pair whose '+='/'^=' calls can each become a plain Luau local instead
    ///     of an entry in the hidden per-event connection store. A '-=' always rebinds to whichever '+='/'^='
    ///     most recently ran for that pair, so a connection only needs to prove local-safety against the
    ///     disconnects that fall between it and the next connect for the same pair (if any); if every
    ///     connect for a pair can clear that bar, the whole pair can use locals.
    /// </summary>
    public static HashSet<(EventTarget Target, Symbol Function)> ComputeLocallySafeConnections(SemanticModel semanticModel)
    {
        var connectsByKey = new Dictionary<(EventTarget, Symbol), List<AssignmentOperator>>();
        var disconnectsByKey = new Dictionary<(EventTarget, Symbol), List<AssignmentOperator>>();

        foreach (var assignment in semanticModel.Tree.EnumerateDescendants<AssignmentOperator>())
        {
            if (assignment.Operator.Kind is not (SyntaxKind.PlusEquals or SyntaxKind.MinusEquals or SyntaxKind.CaretEquals)) continue;
            if (ResolveEventTarget(semanticModel, assignment.Left) is not { } target) continue;
            if (ResolveConnectionFunction(semanticModel, assignment.Right) is not { } functionSymbol) continue;

            var key = (target, functionSymbol);
            var bucket = assignment.Operator.Kind is SyntaxKind.PlusEquals or SyntaxKind.CaretEquals ? connectsByKey : disconnectsByKey;
            if (!bucket.TryGetValue(key, out var list))
                bucket[key] = list = [];

            list.Add(assignment);
        }

        var localSafe = new HashSet<(EventTarget, Symbol)>();
        foreach (var (key, connects) in connectsByKey)
        {
            var orderedConnects = connects.OrderBy(connect => connect.Span.Position).ToList();
            disconnectsByKey.TryGetValue(key, out var disconnects);

            var safe = true;
            for (var i = 0; i < orderedConnects.Count && safe; i++)
            {
                var connect = orderedConnects[i];
                var nextConnectPosition = i + 1 < orderedConnects.Count ? orderedConnects[i + 1].Span.Position : int.MaxValue;

                safe = disconnects == null
                    || disconnects.TrueForAll(disconnect => disconnect.Span.Position <= connect.Span.Position
                        || disconnect.Span.Position >= nextConnectPosition
                        || CanShareLocalScope(connect, disconnect)
                    );
            }

            if (safe)
                localSafe.Add(key);
        }

        return localSafe;
    }

    /// <summary>
    ///     Whether a Luau local declared at <paramref name="connect" /> would still be in scope at
    ///     <paramref name="disconnect" />: they must live in the exact same Luau scope, with the connect
    ///     coming first, or the disconnect must be nested somewhere inside a scope that starts after the
    ///     connection within that shared scope (nested scopes see enclosing locals as upvalues, but siblings
    ///     and outer scopes never see locals declared inside a nested one).
    /// </summary>
    private static bool CanShareLocalScope(Node connect, Node disconnect)
    {
        if (FindImmediateScope(connect) is not { } connectScope)
            return false;

        var current = disconnect;
        while (FindImmediateScope(current) is { } found)
        {
            var (id, entry) = found;
            if (id.Equals(connectScope.Id))
                return IsAtOrAfter(id, connectScope.EntryChild, entry);

            current = id.Owner;
        }

        return false;
    }

    private static bool IsAtOrAfter(ScopeId id, Node earlier, Node later)
    {
        if (earlier == later)
            return true;

        if (id.Owner is not (Block or Tree))
            return false;

        return earlier.Span.Position < later.Span.Position;
    }

    /// <summary>
    ///     Walks up from <paramref name="node" /> to the nearest Luau-scope-introducing ancestor (a Block,
    ///     the file root, an if-branch, or a while/for/after/every/function body), returning that scope's
    ///     identity plus the direct child of the scope that <paramref name="node" /> descends through.
    /// </summary>
    private static (ScopeId Id, Node EntryChild)? FindImmediateScope(Node node)
    {
        var current = node;
        while (true)
        {
            if (current.Parent is not { } parent)
                return null;

            switch (parent)
            {
                case Tree or Block:
                    return (new ScopeId(parent), current);
                case If @if when current == @if.ThenBranch:
                    return (new ScopeId(@if), current);
                case If @if when @if.ElseBranch?.Branch == current:
                    return (new ScopeId(@if, 1), current);
                case While @while when @while.Body == current:
                    return (new ScopeId(@while), current);
                case For @for when @for.Body == current:
                    return (new ScopeId(@for), current);
                case After after when after.Body == current:
                    return (new ScopeId(after), current);
                case Every every when every.Body == current:
                    return (new ScopeId(every), current);
                case FunctionDeclaration function when function.Body == current:
                    return (new ScopeId(function), current);
                case FunctionExpression function when function.Body == current:
                    return (new ScopeId(function), current);
            }

            current = parent;
        }
    }

    private readonly record struct ScopeId(Node Owner, int Branch = 0);
}