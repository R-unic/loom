using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Core.Text;
using Loom.Luau;
using Loom.Luau.AST;
using BinaryOperator = Loom.Luau.AST.BinaryOperator;
using PropertyAccess = Loom.Core.Parsing.AST.PropertyAccess;
using ExpressionStatement = Loom.Core.Parsing.AST.ExpressionStatement;
using Identifier = Loom.Core.Parsing.AST.Identifier;
using QualifiedName = Loom.Core.Parsing.AST.QualifiedName;
using TypeName = Loom.Luau.AST.TypeName;

namespace Loom.Core.Generation;

public sealed partial class LuauGenerator
{
    public override LuauNode VisitEventDeclaration(EventDeclaration eventDeclaration)
    {
        // TODO: generic events
        if (eventDeclaration.TypeParameters != null)
            _diagnostics.NotImplemented(eventDeclaration.TypeParameters, "Generic event declarations are not yet supported.");

        _semanticModel.RuntimeReferences += 2;
        var parameterTypes = eventDeclaration.Parameters?.ParameterList.ConvertAll(p => Visit(p.ColonTypeClause!.Type)) ?? [];
        var eventType = LuauFactory.QualifyRuntimeType(new TypeName("Event", parameterTypes));
        return new ConstVariable(eventDeclaration.Name.Text, eventType, LuauFactory.RuntimeLibraryCall(["Event", "new"], []));
    }

    public override LuauNode VisitAssignmentOperator(AssignmentOperator assignmentOperator)
    {
        if (assignmentOperator.Operator.Kind is SyntaxKind.PlusEquals or SyntaxKind.MinusEquals
            && ResolveEventTarget(assignmentOperator.Left) is { } eventTarget)
        {
            return GenerateEventAssignment(assignmentOperator, eventTarget);
        }

        if (assignmentOperator.Parent is ExpressionStatement)
            return VisitBinaryOperator(assignmentOperator);

        if (assignmentOperator.Left is Identifier)
        {
            var binary = (BinaryOperator)VisitBinaryOperator(assignmentOperator);
            var assignmentStatement = new Luau.AST.ExpressionStatement(binary);
            _state.Prereq(assignmentStatement);

            return binary.Left;
        }

        var left = Visit(assignmentOperator.Left);
        var right = Visit(assignmentOperator.Right);
        if (assignmentOperator.Parent is EqualsValueClause { Parent: NamedDeclaration declaration })
        {
            var identifierAssignment = new BinaryOperator(left, "=", new Luau.AST.Identifier(declaration.Name.Text));
            _state.Postreq(new Luau.AST.ExpressionStatement(identifierAssignment));

            return right;
        }

        var assigned = _state.PushToVariable("_assigned", right);
        var boundAssignment = new BinaryOperator(left, "=", assigned);
        _state.Prereq(new Luau.AST.ExpressionStatement(boundAssignment));

        return assigned;
    }

    private EventTarget? ResolveEventTarget(Expression left)
    {
        if (_semanticModel.GetSymbol(left) is { Kind: SymbolKind.Event } globalEventSymbol)
            return new EventTarget(null, globalEventSymbol);

        if (_semanticModel.GetPropertySymbol(left) is not { Kind: SymbolKind.Event } propertySymbol)
            return null;

        return new EventTarget(GetInstanceKey(left), propertySymbol);
    }

    private object? GetInstanceKey(Expression left) => left switch
    {
        PropertyAccess { Expression: Identifier identifier } => _semanticModel.GetSymbol(identifier),
        QualifiedName { Identifier: var identifier } => _semanticModel.GetSymbol(identifier),
        _ => new object()
    };

    private LuauExpression GenerateEventAssignment(AssignmentOperator assignmentOperator, EventTarget eventTarget)
    {
        var connectionTarget = Visit(assignmentOperator.Left);
        return assignmentOperator.Operator.Kind == SyntaxKind.PlusEquals
            ? GenerateEventConnect(assignmentOperator, connectionTarget, eventTarget)
            : GenerateEventDisconnect(assignmentOperator, eventTarget);
    }

    private LuauExpression GenerateEventConnect(AssignmentOperator assignmentOperator, LuauExpression connectionTarget, EventTarget eventTarget)
    {
        var function = assignmentOperator.Right;
        var luauFunction = WrapAnonymousFunction(function, Visit(function), new UnitType());
        var connect = new Call(new Luau.AST.PropertyAccess(connectionTarget, ["Connect"]), [luauFunction], true);
        if (luauFunction is AnonymousFunction || function is not Identifier identifier || _semanticModel.GetSymbol(identifier) is not { } functionSymbol)
            return connect;

        _eventConnections.MarkConnected(eventTarget, functionSymbol);

        // When every disconnect for this (event, function) pair provably shares a Luau scope with
        // this connect, a plain local keeps the output as minimal as hand-written code would be.
        if (_localSafeConnections.Value.Contains((eventTarget, functionSymbol)))
        {
            if (assignmentOperator.Parent is EqualsValueClause { Parent: VariableDeclaration declaration })
            {
                _eventConnections.TrackLocalConnection(eventTarget, functionSymbol, new Luau.AST.Identifier(declaration.Name.Text));
                return connect;
            }

            var connectionVariable = _state.PushToVariable($"{identifier.Name.Text}_conn", connect);
            _eventConnections.TrackLocalConnection(eventTarget, functionSymbol, connectionVariable);
            return connectionVariable;
        }

        var store = GetConnectionStore(eventTarget);
        var connectionSlot = new Luau.AST.ElementAccess(store, luauFunction);
        var assign = new BinaryOperator(connectionSlot, "=", connect);

        // A bare 'event += fn;' statement doesn't need the connection's value, so the store
        // assignment can be emitted directly instead of stashed as a prereq of an unused expression.
        if (assignmentOperator.Parent is ExpressionStatement)
            return assign;

        _state.Prereq(new Luau.AST.ExpressionStatement(assign));
        return connectionSlot;
    }

    private LuauExpression GenerateEventDisconnect(AssignmentOperator assignmentOperator, EventTarget eventTarget)
    {
        var function = assignmentOperator.Right;
        if (function is Identifier identifier
            && _semanticModel.GetSymbol(identifier) is { } functionSymbol
            && _eventConnections.IsConnected(eventTarget, functionSymbol))
        {
            var connection = _eventConnections.TryGetLocalConnection(eventTarget, functionSymbol, out var localConnection)
                ? (LuauExpression)localConnection
                : new Luau.AST.ElementAccess(GetConnectionStore(eventTarget), Visit(function));

            return new Call(new Luau.AST.PropertyAccess(connection, ["Disconnect"]), [], true);
        }

        if (function is not Identifier && IsMethodReference(function))
        {
            _diagnostics.Error(
                function,
                InternalCodes.AnonymousEventDisconnect,
                "Cannot disconnect a function reference that gets wrapped into a new Luau closure on every connection.",
                "store the connection returned from '+=' and disconnect that instead."
            );

            return new NilLiteral();
        }

        _diagnostics.Error(
            assignmentOperator,
            InternalCodes.UnresolvedEventDisconnect,
            "No event connection exists for this function, connect it with '+=' before disconnecting it."
        );

        return new NilLiteral();
    }

    private Luau.AST.Identifier GetConnectionStore(EventTarget eventTarget) =>
        _eventConnections.GetOrCreateStore(eventTarget, () => _state.Scope.AddIdentifier(ConnectionStoreBaseName(eventTarget)));

    private static string ConnectionStoreBaseName(EventTarget eventTarget) =>
        eventTarget.Instance is Symbol instanceSymbol
            ? $"_{instanceSymbol.Name}_{eventTarget.Event.Name}_connections"
            : $"_{eventTarget.Event.Name}_connections";

    /// <summary>
    /// Finds every '+=' whose matching '-=' calls (if any) are all provably reachable from a Luau
    /// local declared at the '+=' site - i.e. the connection can be a plain local instead of an entry
    /// in the hidden per-event connection store. Computed lazily on the first '+=' so files with no
    /// event connections never pay for the full-tree scan, but once computed it covers the whole file
    /// so each '+=' knows, at the point it's generated, whether a later '-=' elsewhere will need the store.
    /// </summary>
    private HashSet<(EventTarget Target, Symbol Function)> ComputeLocalSafeConnections()
    {
        var connectsByKey = new Dictionary<(EventTarget, Symbol), List<AssignmentOperator>>();
        var disconnectsByKey = new Dictionary<(EventTarget, Symbol), List<AssignmentOperator>>();

        foreach (var assignment in _semanticModel.Tree.GetDescendants<AssignmentOperator>())
        {
            if (assignment.Operator.Kind is not (SyntaxKind.PlusEquals or SyntaxKind.MinusEquals))
                continue;

            if (ResolveEventTarget(assignment.Left) is not { } target)
                continue;

            if (assignment.Right is not Identifier identifier || _semanticModel.GetSymbol(identifier) is not { } functionSymbol)
                continue;

            var key = (target, functionSymbol);
            var bucket = assignment.Operator.Kind == SyntaxKind.PlusEquals ? connectsByKey : disconnectsByKey;
            if (!bucket.TryGetValue(key, out var list))
                bucket[key] = list = [];

            list.Add(assignment);
        }

        var localSafe = new HashSet<(EventTarget, Symbol)>();
        foreach (var (key, connects) in connectsByKey)
        {
            // More than one '+=' for the same target/function means a single local can't represent
            // both connections unambiguously, so fall back to the store.
            if (connects.Count != 1)
                continue;

            if (!disconnectsByKey.TryGetValue(key, out var disconnects) || disconnects.TrueForAll(d => CanShareLocalScope(connects[0], d)))
                localSafe.Add(key);
        }

        return localSafe;
    }

    /// <summary>
    /// Whether a Luau local declared at <paramref name="connect"/> would still be in scope at
    /// <paramref name="disconnect"/>: they must live in the exact same Luau scope, with the connect
    /// coming first, or the disconnect must be nested somewhere inside a scope that starts after the
    /// connect within that shared scope (nested scopes see enclosing locals as upvalues, but siblings
    /// and outer scopes never see locals declared inside a nested one).
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

        // Non-block scopes (a bare 'if cond stmt' body, etc.) hold exactly one statement, so distinct
        // connect/disconnect nodes can only share one by also sharing a Block/Tree ancestor already
        // handled above; treat any other equality-without-list case as unsafe.
        if (id.Owner is not (Block or Tree))
            return false;

        // A Node's Children (and so a Block/Tree's Statements) are always kept in source order, so
        // comparing positions is equivalent to comparing list indices without the linear scan.
        return earlier.Span.Position < later.Span.Position;
    }

    private readonly record struct ScopeId(Node Owner, int Branch = 0);

    /// <summary>
    /// Walks up from <paramref name="node"/> to the nearest Luau-scope-introducing ancestor (a Block,
    /// the file root, an if-branch, or a while/for/after/function body), returning that scope's
    /// identity plus the direct child of the scope that <paramref name="node"/> descends through.
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
                    return (new ScopeId(@if, 0), current);
                case If @if when @if.ElseBranch?.Branch == current:
                    return (new ScopeId(@if, 1), current);
                case While @while when @while.Body == current:
                    return (new ScopeId(@while), current);
                case For @for when @for.Body == current:
                    return (new ScopeId(@for), current);
                case After after when after.Body == current:
                    return (new ScopeId(after), current);
                case FunctionDeclaration function when function.Body == current:
                    return (new ScopeId(function), current);
            }

            current = parent;
        }
    }
}
