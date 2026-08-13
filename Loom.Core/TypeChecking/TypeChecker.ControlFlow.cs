using Loom.Core.FlowAnalysis;
using Loom.Core.Parsing.AST;
using Loom.Core.TypeChecking.Types;

namespace Loom.Core.TypeChecking;

using Type = Types.Type;

public sealed partial class TypeChecker
{
    public override Type VisitBreak(Break @break)
    {
        _loopExitScopes.Peek().Add(_flowState);
        return BindType(@break, Types.PrimitiveType.Void);
    }

    public override Type VisitContinue(Continue @continue)
    {
        _loopExitScopes.Peek().Add(_flowState);
        return BindType(@continue, Types.PrimitiveType.Void);
    }

    public override Type VisitFor(For @for)
    {
        var collectionType = Visit(@for.CollectionExpression);
        if (collectionType is InstantiatedType i)
            collectionType = i.Expand();

        _semanticModel.TypeSolver.AddConstraint(collectionType, ObjectType.Empty, @for.CollectionExpression);
        var isRange = collectionType.Equals(IntrinsicTypes.Range);
        var iteratedElement = IteratorElementType(collectionType);
        var elementType = isRange ? Types.PrimitiveType.Number : iteratedElement ?? GetObjectValueType(collectionType);
        var maxNames = isRange || iteratedElement != null ? 1 : 2;
        if (@for.Names.Count > maxNames)
        {
            _diagnostics.NotImplemented(
                @for.Names[maxNames],
                isRange ? "Iterating over a range only produces one value, so only one name is permitted."
                : iteratedElement != null ? "An iterator's 'next' answers one value, so only one name is permitted."
                : "Functional iterators are not supported yet, so more than two names is not permitted."
            );

            return BindType(@for, Types.PrimitiveType.Never);
        }

        switch (collectionType)
        {
            case var _ when collectionType.Equals(IntrinsicTypes.Range):
                BindType(@for.Names[0], elementType);
                break;

            // Ahead of the object case: an iterator is an interface, and walking its own fields is not
            // what a type that says how to iterate itself meant.
            case var _ when iteratedElement != null:
                BindType(@for.Names[0], elementType);
                break;
            case Types.ArrayType:
                BindType(@for.Names[0], elementType);
                if (@for.Names.Count > 1)
                    BindType(@for.Names[1], Types.PrimitiveType.Number);

                break;
            case InterfaceType or ObjectType:
            {
                var objectType = collectionType is InterfaceType interfaceType ? interfaceType.ObjectType : (ObjectType)collectionType;
                BindType(@for.Names[0], objectType.KeyUnion());
                if (@for.Names.Count > 1)
                    BindType(@for.Names[1], elementType);

                break;
            }
        }

        _loopExitScopes.Push([]);
        var bodyType = CheckBody(@for.Body, _flowState);
        AssignLoopExitState(@for);

        return BindType(@for, bodyType);
    }

    public override Type VisitAfter(After after)
    {
        var durationType = Visit(after.Duration);
        _semanticModel.TypeSolver.AddConstraint(durationType, Types.PrimitiveType.Number, after.Duration);

        return BindType(after, Visit(after.Body));
    }

    private static Type? IteratorElementType(Type collectionType) =>
        collectionType is InterfaceType interfaceType ? interfaceType.IteratedElementType : null;

    public override Type VisitEvery(Every every)
    {
        var durationType = Visit(every.Duration);
        _semanticModel.TypeSolver.AddConstraint(durationType, Types.PrimitiveType.Number, every.Duration);

        if (every.Condition != null)
        {
            var conditionType = Visit(every.Condition);
            _semanticModel.TypeSolver.AddConstraint(conditionType, Types.PrimitiveType.Bool, every.Condition);
        }

        return BindType(every, Visit(every.Body));
    }

    public override Type VisitWhile(While @while)
    {
        var conditionType = Visit(@while.Condition);
        _semanticModel.TypeSolver.AddConstraint(conditionType, Types.PrimitiveType.Bool, @while.Condition);

        _loopExitScopes.Push([]);
        var (trueState, _) = _narrower.ComputeBranchStates(@while.Condition, _flowState);
        var bodyType = CheckBody(@while.Body, trueState);
        AssignLoopExitState(@while);

        return BindType(@while, bodyType);
    }

    public override Type VisitIf(If @if)
    {
        var conditionType = Visit(@if.Condition);
        _semanticModel.TypeSolver.AddConstraint(conditionType, Types.PrimitiveType.Bool, @if.Condition);

        var (trueState, falseState) = _narrower.ComputeBranchStates(@if.Condition, _flowState);
        var thenType = CheckBody(@if.ThenBranch, trueState);
        var thenExit = _exitStates.GetValueOrDefault(@if.ThenBranch, trueState);
        var elseType = @if.ElseBranch != null ? CheckBody(@if.ElseBranch, falseState) : Types.PrimitiveType.None;
        var elseExit = @if.ElseBranch != null ? _exitStates.GetValueOrDefault(@if.ElseBranch, falseState) : falseState;

        _exitStates[@if] = MergeExitStates(thenExit, elseExit);
        return BindType(@if, TypeSimplifier.Simplify(new Types.UnionType([thenType, elseType])));
    }

    public override Type VisitReturn(Return @return)
    {
        if (@return.Expression == null)
            return BindType(@return, Types.PrimitiveType.Void);

        var expected = GetEnclosingDeclaredReturnType(@return);
        var actual = expected != null
            ? Check(@return.Expression, expected)
            : Visit(@return.Expression);

        return BindType(@return, actual);
    }

    private Type? GetEnclosingDeclaredReturnType(Node node)
    {
        if (node.FirstAncestorImplementing<IFunctionLike>() is not { } enclosingFunction)
            return null;

        return ((IFunctionLike)enclosingFunction).ReturnType != null
            ? ((Types.FunctionType)_semanticModel.GetType(enclosingFunction)).ReturnType
            : null;
    }

    private List<Type> CheckStatements(Node node, List<Statement> statements)
    {
        var current = _flowState;
        var types = new List<Type>(statements.Count);
        foreach (var statement in statements)
        {
            types.Add(Visit(statement, current));
            current = GetStatementExitState(statement, current);
        }

        _exitStates[node] = current;
        return types;
    }

    private Type CheckBody(Statement body, FlowState current)
    {
        var type = Visit(body, current);
        if (body is Block)
            return type;

        var exit = GetStatementExitState(body, current);
        _exitStates[body] = exit;

        return type;
    }

    private FlowState GetStatementExitState(Statement statement, FlowState entryState) =>
        statement switch
        {
            Return or Break or Continue => new FlowState(entryState) { IsUnreachable = true },
            _ => _exitStates.GetValueOrDefault(statement, entryState)
        };

    private void AssignLoopExitState(Node node)
    {
        var exits = _loopExitScopes.Pop();
        var bodyExit = exits.Aggregate(_flowState, MergeExitStates);
        _exitStates[node] = bodyExit;
    }

    private static FlowState MergeExitStates(FlowState left, FlowState right) =>
        left.IsUnreachable
            ? right
            : right.IsUnreachable
                ? left
                : left.Merge(right);
}
