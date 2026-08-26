using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Core.Resolving.Symbols;
using Loom.Core.Text;

namespace Loom.Core.FlowAnalysis;

public sealed class FlowAnalyzer(SemanticModel semanticModel)
{
    private readonly DiagnosticBag _diagnostics = new(options: semanticModel.Diagnostics.Options);
    private readonly Stack<List<FlowState>?> _loopExitScopes = [];
    private readonly Dictionary<Node, FlowState> _states = [];

    public FlowState GetState(Node node) => _states.TryGetValue(node, out var existingState) ? existingState : FlowState.Empty;

    public FlowAnalyzerResult Analyze()
    {
        BindState(semanticModel.Tree, AnalyzeStatements(semanticModel.Tree.Statements, new FlowState()));
        return new FlowAnalyzerResult(_diagnostics);
    }

    private FlowState AnalyzeStatements(IReadOnlyList<Statement> statements, FlowState state) =>
        statements.Aggregate(state, (current, statement) => AnalyzeStatement(statement, current));

    private FlowState AnalyzeStatement(Statement statement, FlowState state)
    {
        var newState = statement switch
        {
            Block block => AnalyzeBlock(block, state),
            VariableDeclaration variableDeclaration => AnalyzeVariableDeclaration(variableDeclaration, state),
            DestructuringDeclaration destructuringDeclaration => AnalyzeDestructuringDeclaration(destructuringDeclaration, state),
            FunctionDeclaration functionDeclaration => AnalyzeFunctionDeclaration(functionDeclaration, state),
            EventDeclaration eventDeclaration => AnalyzeEventDeclaration(eventDeclaration, state),
            InterfaceDeclaration interfaceDeclaration => AnalyzeInterfaceDeclaration(interfaceDeclaration, state),
            EnumDeclaration enumDeclaration => AnalyzeEnumDeclaration(enumDeclaration, state),
            Implement implement => AnalyzeImplement(implement, state),
            Return @return => AnalyzeReturn(@return, state),
            Break @break => AnalyzeBreak(@break, state),
            Continue @continue => AnalyzeContinue(@continue, state),
            If @if => AnalyzeIf(@if, state),
            After after => AnalyzeAfter(after, state),
            Every every => AnalyzeEvery(every, state),
            While @while => AnalyzeWhile(@while, state),
            For @for => AnalyzeFor(@for, state),
            ExpressionStatement expressionStatement => AnalyzeExpressionStatement(expressionStatement, state),
            _ => AnalyzeUnhandledStatement(statement, state, out state)
        };

        if (state.IsUnreachable)
            _diagnostics.Warn(statement, InternalCodes.UnreachableCode, "Unreachable code detected.");

        return newState;
    }

    private FlowState AnalyzeUnhandledStatement(Statement statement, FlowState state, out FlowState exitState)
    {
        foreach (var child in statement.Children)
            if (child is Statement childStatement)
                state = AnalyzeStatement(childStatement, state);

        exitState = state;
        return state;
    }

    private FlowState AnalyzeExpression(Expression expression, FlowState state)
    {
        BindState(expression, state);
        return expression switch
        {
            AssignmentOperator assignmentOperator => AnalyzeAssignment(assignmentOperator, state),
            Identifier identifier => AnalyzeIdentifier(identifier, state),
            MatchExpression matchExpression => AnalyzeMatchExpression(matchExpression, state),
            Is isExpression => AnalyzeIsExpression(isExpression, state),
            FunctionExpression functionExpression => AnalyzeFunctionExpression(functionExpression, state),
            _ => AnalyzeUnhandledExpression(expression, state)
        };
    }

    private FlowState AnalyzeFunctionExpression(FunctionExpression functionExpression, FlowState state)
    {
        var functionState = functionExpression.Parameters is { } parameters
            ? state.WithInitialized(parameters.ParameterList.Select(p => semanticModel.GetDeclarationSymbol(p)).OfType<Symbol>())
            : state;

        AnalyzeStatement(functionExpression.Body, functionState);
        return BindState(functionExpression, state);
    }

    private FlowState AnalyzeIsExpression(Is isExpression, FlowState state)
    {
        var afterScrutinee = AnalyzeExpression(isExpression.Expression, state);
        return BindState(isExpression, MarkPatternBindingsInitialized(isExpression.Pattern, afterScrutinee));
    }

    private FlowState AnalyzeUnhandledExpression(Expression expression, FlowState state) =>
        expression.Children.Aggregate(state, (current, child) => AnalyzeExpression(child, current));

    private FlowState AnalyzeMatchExpression(MatchExpression matchExpression, FlowState state)
    {
        var afterScrutinee = AnalyzeExpression(matchExpression.Expression, state);
        if (matchExpression.Arms.Count == 0)
            return BindState(matchExpression, afterScrutinee);

        FlowState? merged = null;
        foreach (var arm in matchExpression.Arms)
        {
            var armState = AnalyzeMatchArm(arm, new FlowState(afterScrutinee));
            merged = merged == null ? armState : MergeStates(merged, armState);
        }

        return BindState(matchExpression, merged!);
    }

    private FlowState AnalyzeMatchArm(MatchArm matchArm, FlowState state)
    {
        var armState = MarkPatternBindingsInitialized(matchArm.Pattern, state);
        if (matchArm.Guard != null)
            armState = AnalyzeExpression(matchArm.Guard, armState);

        armState = AnalyzeExpression(matchArm.Body, armState);
        return BindState(matchArm, armState);
    }

    private FlowState MarkPatternBindingsInitialized(Pattern pattern, FlowState state) => state.WithInitialized(CollectPatternBindingSymbols(pattern));

    private IEnumerable<Symbol> CollectPatternBindingSymbols(Pattern pattern) =>
        pattern switch
        {
            IdentifierPattern or LetPattern =>
                semanticModel.GetDeclarationSymbol(pattern) is { } binding ? [binding] : [],
            TypedPattern typedPattern =>
                CollectTypedPatternBindingSymbols(typedPattern),
            TypePattern { ObjectPattern: not null } typePattern =>
                CollectPatternBindingSymbols(typePattern.ObjectPattern),
            ObjectPattern objectPattern =>
                objectPattern.Fields.SelectMany(field => CollectPatternBindingSymbols(field.Pattern)),
            ArrayPattern arrayPattern =>
                arrayPattern.Elements
                    .SelectMany(CollectPatternBindingSymbols)
                    .Concat(arrayPattern.Rest != null ? CollectPatternBindingSymbols(arrayPattern.Rest) : []),
            RestPattern restPattern =>
                CollectPatternBindingSymbols(restPattern.Pattern),
            TuplePattern tuplePattern =>
                tuplePattern.Patterns.SelectMany(CollectPatternBindingSymbols),
            OrPattern orPattern =>
                orPattern.Patterns.SelectMany(CollectPatternBindingSymbols),
            RangePattern rangePattern =>
                CollectPatternBindingSymbols(rangePattern.Minimum)
                    .Concat(CollectPatternBindingSymbols(rangePattern.Maximum)),
            _ => []
        };

    private IEnumerable<Symbol> CollectTypedPatternBindingSymbols(TypedPattern typedPattern)
    {
        if (semanticModel.GetDeclarationSymbol(typedPattern) is { } typedBinding)
            yield return typedBinding;

        if (typedPattern.ObjectPattern == null)
            yield break;

        foreach (var symbol in CollectPatternBindingSymbols(typedPattern.ObjectPattern))
            yield return symbol;
    }

    private FlowState AnalyzeImplement(Implement implement, FlowState state)
    {
        var bodyState = state
            .WithInitialized(semanticModel.GetDeclarationSymbols(implement))
            .WithInitialized(
                implement.Body.Implementations
                    .Select(declaration => semanticModel.GetDeclarationSymbol(declaration, SymbolKind.Function))
                    .OfType<Symbol>()
            );

        return AnalyzeStatement(implement.Body, bodyState);
    }

    private FlowState AnalyzeBlock(Block block, FlowState state) => BindState(block, AnalyzeStatements(block.Statements, state));

    private FlowState AnalyzeEventDeclaration(EventDeclaration eventDeclaration, FlowState state) =>
        BindState(
            eventDeclaration,
            semanticModel.GetDeclarationSymbol(eventDeclaration, SymbolKind.Event) is { } symbol
                ? state.WithInitialized(symbol)
                : state
        );

    private FlowState AnalyzeInterfaceDeclaration(InterfaceDeclaration interfaceDeclaration, FlowState state) =>
        BindState(
            interfaceDeclaration,
            semanticModel.GetDeclarationSymbol(interfaceDeclaration, SymbolKind.Variable) is { } symbol
                ? state.WithInitialized(symbol)
                : state
        );

    private FlowState AnalyzeEnumDeclaration(EnumDeclaration enumDeclaration, FlowState state) =>
        BindState(
            enumDeclaration,
            semanticModel.GetDeclarationSymbol(enumDeclaration, SymbolKind.Variable) is { } symbol
                ? state.WithInitialized(symbol)
                : state
        );

    private FlowState AnalyzeFunctionDeclaration(FunctionDeclaration functionDeclaration, FlowState state)
    {
        var newState = semanticModel.GetDeclarationSymbol(functionDeclaration) is { } symbol
            ? state.WithInitialized(symbol)
            : state;

        var functionState = functionDeclaration.Parameters is { } parameters
            ? newState.WithInitialized(
                parameters.ParameterList
                    .Select(parameter => semanticModel.GetDeclarationSymbol(parameter))
                    .OfType<Symbol>()
            )
            : newState;

        AnalyzeStatement(functionDeclaration.Body, functionState);
        return BindState(functionDeclaration, newState);
    }

    private FlowState AnalyzeVariableDeclaration(VariableDeclaration variableDeclaration, FlowState state)
    {
        if (variableDeclaration.EqualsValueClause == null)
            return BindState(variableDeclaration, state);

        var result = AnalyzeExpression(variableDeclaration.EqualsValueClause.Value, state);
        var symbol = semanticModel.GetDeclarationSymbol(variableDeclaration);
        return BindState(variableDeclaration, symbol == null ? result : result.WithInitialized(symbol));
    }

    private FlowState AnalyzeDestructuringDeclaration(DestructuringDeclaration destructuringDeclaration, FlowState state)
    {
        if (destructuringDeclaration.EqualsValueClause == null)
            return BindState(destructuringDeclaration, state);

        var result = AnalyzeExpression(destructuringDeclaration.EqualsValueClause.Value, state);
        var symbols = CollectDestructuringBindingSymbols(destructuringDeclaration.Target);
        return BindState(destructuringDeclaration, result.WithInitialized(symbols));
    }

    private IEnumerable<Symbol> CollectDestructuringBindingSymbols(DestructuringTarget target) =>
        target switch
        {
            ArrayDestructuringTarget arrayTarget => arrayTarget.Elements.SelectMany(CollectDestructuringBindingSymbols),
            ObjectDestructuringTarget objectTarget => objectTarget.Fields.SelectMany(CollectDestructuringBindingSymbols),
            TupleDestructuringTarget tupleTarget => tupleTarget.Elements.SelectMany(CollectDestructuringBindingSymbols),
            _ => []
        };

    private IEnumerable<Symbol> CollectDestructuringBindingSymbols(DestructuringElement element) =>
        element.NestedTarget != null
            ? CollectDestructuringBindingSymbols(element.NestedTarget)
            : semanticModel.GetDeclarationSymbol(element) is { } symbol ? [symbol] : [];

    private IEnumerable<Symbol> CollectDestructuringBindingSymbols(ObjectDestructuringField field) =>
        field.NestedTarget != null
            ? CollectDestructuringBindingSymbols(field.NestedTarget)
            : semanticModel.GetDeclarationSymbol(field) is { } symbol ? [symbol] : [];

    private FlowState AnalyzeIf(If @if, FlowState state)
    {
        var conditionState = AnalyzeExpression(@if.Condition, state);
        var thenState = AnalyzeStatement(@if.ThenBranch, conditionState);
        var elseState = @if.ElseBranch != null ? AnalyzeStatement(@if.ElseBranch.Branch, conditionState) : conditionState;
        return BindState(@if, MergeStates(thenState, elseState));
    }

    private FlowState AnalyzeAfter(After after, FlowState state)
    {
        var bodyState = AnalyzeExpression(after.Duration, state);
        var (body, _) = CaptureExitStates(after.Body, bodyState, null);
        return BindState(after, state.Merge(body));
    }

    private FlowState AnalyzeEvery(Every every, FlowState state)
    {
        var bodyState = AnalyzeExpression(every.Duration, state);
        if (every.Condition != null)
            bodyState = AnalyzeExpression(every.Condition, bodyState);

        var (body, _) = CaptureExitStates(every.Body, bodyState, null);
        return BindState(every, state.Merge(body));
    }

    private FlowState AnalyzeWhile(While @while, FlowState state)
    {
        var bodyState = AnalyzeExpression(@while.Condition, state);
        var (body, breakStates) = CaptureExitStates(@while.Body, bodyState, []);
        return BindState(@while, ComputeLoopExitState(state, body, breakStates));
    }

    private FlowState AnalyzeFor(For @for, FlowState state)
    {
        var bodyState = AnalyzeExpression(@for.CollectionExpression, state)
            .WithInitialized(@for.Names.Select(name => semanticModel.GetDeclarationSymbol(name)).OfType<Symbol>());

        var (body, breakStates) = CaptureExitStates(@for.Body, bodyState, []);
        return BindState(@for, ComputeLoopExitState(state, body, breakStates));
    }

    private FlowState AnalyzeBreak(Break @break, FlowState state)
    {
        var result = new FlowState(state) { IsUnreachable = true };
        if (_loopExitScopes.Count > 0 && _loopExitScopes.Peek() is { } scope)
            scope.Add(result);

        return BindState(@break, result);
    }

    private FlowState AnalyzeContinue(Continue @continue, FlowState state) => BindState(@continue, new FlowState(state) { IsUnreachable = true });

    private FlowState AnalyzeReturn(Return @return, FlowState state) =>
        BindState(@return, new FlowState(@return.Expression != null ? AnalyzeExpression(@return.Expression, state) : state) { IsUnreachable = true });

    private FlowState AnalyzeExpressionStatement(ExpressionStatement statement, FlowState state)
    {
        state = AnalyzeExpression(statement.Expression, state);
        return BindState(statement, state);
    }

    private FlowState AnalyzeAssignment(AssignmentOperator assignment, FlowState state)
    {
        if (assignment.Left is Identifier identifier)
        {
            var symbol = semanticModel.GetSymbol(identifier);
            if (symbol == null)
                return BindState(assignment, state);

            if (assignment.Operator.Kind != SyntaxKind.Equals)
            {
                state = AnalyzeIdentifier(identifier, state);
                state = AnalyzeExpression(assignment.Right, state);
                CheckReassignment(assignment, identifier, symbol);
                return BindState(assignment, state);
            }

            CheckReassignment(assignment, identifier, symbol);
            state = AnalyzeExpression(assignment.Right, state).WithInitialized(symbol);
        }
        else
        {
            state = AnalyzeExpression(assignment.Left, state);
            state = AnalyzeExpression(assignment.Right, state);
        }

        return BindState(assignment, state);
    }

    private void CheckReassignment(AssignmentOperator assignment, Identifier identifier, Symbol symbol)
    {
        if (symbol.IsMutable) return;
        if (symbol.Kind == SymbolKind.Event && assignment.Operator.Kind is SyntaxKind.PlusEquals or SyntaxKind.MinusEquals or SyntaxKind.CaretEquals) return;

        _diagnostics.Error(
            assignment,
            InternalCodes.AssignToImmutable,
            $"Cannot assign to immutable variable '{identifier}'.",
            $"did you mean to declare '{identifier}' as mutable?"
        );
    }

    private FlowState AnalyzeIdentifier(Identifier identifier, FlowState state)
    {
        var symbol = semanticModel.GetSymbol(identifier);
        if (symbol is null
            || symbol.IsIntrinsic
            || semanticModel.IsImported(symbol)
            || symbol.Declaration.FirstAncestorOfType<Declare>() is not null
            || symbol is { IsValueSymbol: false }
            || state.DefinitelyInitialized.Contains(symbol))
            return BindState(identifier, state);

        if (state.MaybeInitialized.Contains(symbol))
            _diagnostics.Error(identifier, InternalCodes.UseOfMaybeUninitialized, $"Variable '{symbol.Name}' might not be initialized on this path.");
        else
            _diagnostics.Error(identifier, InternalCodes.UseOfUninitialized, $"Use of uninitialized variable '{symbol.Name}'.");

        return BindState(identifier, state);
    }

    private (FlowState State, List<FlowState> ExitStates) CaptureExitStates(Statement statement, FlowState bodyState, List<FlowState>? initial)
    {
        _loopExitScopes.Push(initial);
        var body = AnalyzeStatement(statement, bodyState);
        return (body, _loopExitScopes.Pop()!);
    }

    private static FlowState ComputeLoopExitState(FlowState entryState, FlowState bodyState, List<FlowState> breakStates)
    {
        var definitely = entryState.DefinitelyInitialized;
        foreach (var exit in breakStates)
            definitely = definitely.Intersect(exit.DefinitelyInitialized);

        var maybeBuilder = entryState.MaybeInitialized.ToBuilder();
        maybeBuilder.UnionWith(bodyState.MaybeInitialized);
        foreach (var exit in breakStates)
            maybeBuilder.UnionWith(exit.MaybeInitialized);

        return new FlowState(definitely, maybeBuilder.ToImmutable(), entryState.IsUnreachable);
    }

    private FlowState BindState(Node node, FlowState state)
    {
        _states[node] = state;
        return state;
    }

    private static FlowState MergeStates(FlowState left, FlowState right) =>
        left.IsUnreachable
            ? right
            : right.IsUnreachable
                ? left
                : left.Merge(right);
}