using Loom.Core.Diagnostics;
using Loom.Core.Generation.Events;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Core.Resolving.Symbols;
using Loom.Core.Text;
using Loom.Luau;
using Loom.Luau.AST;
using BinaryOperator = Loom.Luau.AST.BinaryOperator;
using ElementAccess = Loom.Luau.AST.ElementAccess;
using ExpressionStatement = Loom.Core.Parsing.AST.ExpressionStatement;
using Identifier = Loom.Core.Parsing.AST.Identifier;
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

    public override LuauNode VisitAssignmentOperator(AssignmentOperator assignmentOperator) =>
        assignmentOperator.Operator.Kind is SyntaxKind.PlusEquals or SyntaxKind.MinusEquals
        && EventConnectionScopeAnalyzer.ResolveEventTarget(_semanticModel, assignmentOperator.Left) is { } eventTarget
            ? GenerateEventAssignment(assignmentOperator, eventTarget)
            : GenerateAssignment(assignmentOperator);

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
        var connectionSlot = new ElementAccess(store, luauFunction);
        var assign = new BinaryOperator(connectionSlot, "=", connect);
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
            LuauExpression connection = _eventConnections.TryGetLocalConnection(eventTarget, functionSymbol, out var localConnection)
                ? localConnection
                : new ElementAccess(GetConnectionStore(eventTarget), Visit(function));

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
}