using Loom.Core.Diagnostics;
using Loom.Core.Generation.Events;
using Loom.Core.Parsing.AST;
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
        if (eventDeclaration.TypeParameters != null)
            _diagnostics.Error(
                eventDeclaration.TypeParameters,
                InternalCodes.GenericEventDeclaration,
                "Event declarations cannot be generic.",
                "an event lowers to a single Luau value, and Luau has no way to express a generic value - declare the event without type parameters, or make the enclosing interface generic instead."
            );

        _semanticModel.RuntimeReferences += 2;

        // an exported event's store goes out with it whether this module ever connects to it or not,
        // since the module that does connect reaches the connections through this one's export table
        if (_semanticModel.GetDeclarationSymbol(eventDeclaration) is { } eventSymbol && IsSharedEvent(new EventTarget(null, eventSymbol)))
            GetConnectionStore(new EventTarget(null, eventSymbol));

        var eventType = LuauFactory.QualifyRuntimeType(new TypeName("Event", EventTypeArguments(eventDeclaration.Parameters)));
        LuauExpression initializer = LuauFactory.RuntimeLibraryCall(["Event", "new"], []);

        return new ConstVariable(eventDeclaration.Name.Text, eventType, initializer);
    }

    /// <summary>
    ///     The runtime <c>Event&lt;T...&gt;</c>'s type arguments for <paramref name="parameters" />. A rest
    ///     parameter becomes a variadic pack rather than the array it declares: the handler takes any number
    ///     of the elements, and passing the array type would have Luau expect a single table.
    /// </summary>
    private List<LuauType> EventTypeArguments(Parameters? parameters)
    {
        var parameterList = parameters?.ParameterList ?? [];
        return parameterList.ConvertAll(parameter =>
            {
                var type = Visit(parameter.ColonTypeClause!.Type);
                return parameter.DotDot != null && type is TableType { Indexer.ValueType: { } elementType }
                    ? new VariadicTypePack(elementType)
                    : type;
            }
        );
    }

    public override LuauNode VisitAssignmentOperator(AssignmentOperator assignmentOperator) =>
        assignmentOperator.Operator.Kind is SyntaxKind.PlusEquals or SyntaxKind.MinusEquals or SyntaxKind.CaretEquals
        && EventConnectionScopeAnalyzer.ResolveEventTarget(_semanticModel, assignmentOperator.Left) is { } eventTarget
            ? GenerateEventAssignment(assignmentOperator, eventTarget)
            : GenerateAssignment(assignmentOperator);

    private LuauExpression GenerateEventAssignment(AssignmentOperator assignmentOperator, EventTarget eventTarget)
    {
        var connectionTarget = Visit(assignmentOperator.Left);
        return assignmentOperator.Operator.Kind switch
        {
            SyntaxKind.PlusEquals => GenerateEventConnect(assignmentOperator, connectionTarget, eventTarget, "Connect"),
            SyntaxKind.CaretEquals => GenerateEventConnect(assignmentOperator, connectionTarget, eventTarget, "Once"),
            _ => GenerateEventDisconnect(assignmentOperator, eventTarget)
        };
    }

    private LuauExpression GenerateEventConnect(AssignmentOperator assignmentOperator, LuauExpression connectionTarget, EventTarget eventTarget, string method)
    {
        var function = assignmentOperator.Right;
        var luauFunction = WrapAnonymousFunction(function, Visit(function), new UnitType());
        var connect = new Call(new Luau.AST.PropertyAccess(connectionTarget, [method]), [luauFunction], true);
        if (luauFunction is AnonymousFunction || EventConnectionScopeAnalyzer.ResolveConnectionFunction(_semanticModel, function) is not { } functionSymbol)
            return connect;

        _eventConnections.MarkConnected(eventTarget, functionSymbol);

        // a shared event's connections have to land in the store every module reads, since the '-=' that
        // rebinds this connection may well be in another one - a local here would be invisible to it
        if (!IsSharedEvent(eventTarget) && _localSafeConnections.Value.Contains((eventTarget, functionSymbol)))
        {
            if (assignmentOperator.Parent is EqualsValueClause { Parent: VariableDeclaration declaration })
            {
                _eventConnections.TrackLocalConnection(eventTarget, functionSymbol, new Luau.AST.Identifier(declaration.Name.Text));
                return connect;
            }

            var connectionVariable = _state.PushToVariable($"{functionSymbol.Name}_conn", connect);
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
        var isShared = IsSharedEvent(eventTarget);
        if (EventConnectionScopeAnalyzer.ResolveConnectionFunction(_semanticModel, function) is { } functionSymbol
            && (isShared || _eventConnections.IsConnected(eventTarget, functionSymbol)))
        {
            // whether a shared event's store holds a connection for this function is only known at
            // runtime - the '+=' that filled the slot can live in a module compiled on its own
            if (isShared)
            {
                _semanticModel.RuntimeReferences += 1;
                return LuauFactory.RuntimeLibraryCall(["disconnect_event"], [GetConnectionStore(eventTarget), Visit(function)]);
            }

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
                "store the connection returned from '+=' or '^=' and disconnect that instead."
            );

            return new NilLiteral();
        }

        _diagnostics.Error(
            assignmentOperator,
            InternalCodes.UnresolvedEventDisconnect,
            "No event connection exists for this function, connect it with '+=' or '^=' before disconnecting it."
        );

        return new NilLiteral();
    }

    private Luau.AST.Identifier GetConnectionStore(EventTarget eventTarget) =>
        _eventConnections.GetOrCreateStore(eventTarget, () => DeclareConnectionStore(eventTarget));

    /// <summary>
    ///     Names the local backing <paramref name="eventTarget" />'s connections and says what it holds: an
    ///     event this module imported reads the exporting module's store, so both ends of a connection made
    ///     here and broken there (or the other way round) work off one table; anything else starts empty.
    /// </summary>
    private (string Name, LuauExpression Value) DeclareConnectionStore(EventTarget eventTarget)
    {
        if (FindImportedEventStore(eventTarget.Event) is not { } imported)
            return (_state.Scope.AddIdentifier(ConnectionStoreBaseName(eventTarget)), Table.Empty);

        return (_state.Scope.AddIdentifier(imported.LocalName), imported.Value);
    }

    private (string LocalName, LuauExpression Value)? FindImportedEventStore(Symbol eventSymbol)
    {
        if (_semanticModel.ImportBindings.Find(binding => binding.Symbol == eventSymbol) is { } importBinding)
            return (
                EventConnectionTracker.StoreExportName(importBinding.LocalName),
                _moduleGenerator.GenerateModuleMember(importBinding.Module, EventConnectionTracker.StoreExportName(importBinding.ExportedName))
            );

        foreach (var namespaceImport in _semanticModel.NamespaceImports)
            if (namespaceImport.ModuleModel.Exports.Find(e => e.Symbol == eventSymbol) is { } export)
                return (
                    EventConnectionTracker.StoreExportName(export.Name),
                    _moduleGenerator.GenerateModuleMember(namespaceImport.Module, EventConnectionTracker.StoreExportName(export.Name))
                );

        return null;
    }

    /// <summary>
    ///     Whether other modules can see this event, which they can once it is exported or imported. Their
    ///     connections and this module's share one store, and neither end can tell statically what the other
    ///     has connected.
    /// </summary>
    private bool IsSharedEvent(EventTarget eventTarget) => eventTarget.Instance == null && SharedEventSymbols.Contains(eventTarget.Event);

    private HashSet<Symbol> SharedEventSymbols =>
        field ??=
        [
            .._semanticModel.Exports.Where(export => export.Symbol.Kind == SymbolKind.Event).Select(export => export.Symbol),
            .._semanticModel.ImportBindings.Where(binding => binding.Symbol.Kind == SymbolKind.Event).Select(binding => binding.Symbol),
            .._semanticModel.NamespaceImports.SelectMany(namespaceImport => namespaceImport.ModuleModel.Exports)
                .Where(export => export.Symbol.Kind == SymbolKind.Event)
                .Select(export => export.Symbol)
        ];

    private static string ConnectionStoreBaseName(EventTarget eventTarget) =>
        eventTarget.Instance is Symbol instanceSymbol
            ? EventConnectionTracker.StoreExportName($"{instanceSymbol.Name}_{eventTarget.Event.Name}")
            : EventConnectionTracker.StoreExportName(eventTarget.Event.Name);
}