using Loom.Core.FlowAnalysis;
using Loom.Core.Parsing.AST;
using Loom.Core.TypeChecking.Types;

namespace Loom.Core.TypeChecking;

using Type = Types.Type;
using Loom.Core.TypeChecking.Solving;
using Loom.Core.TypeChecking.Intrinsic;

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

        // What a type parameter can be iterated as is whatever its constraint promised, so the loop reads
        // through to that rather than at the parameter - which satisfies nothing and is assignable to
        // nothing, and would otherwise reject every generic function that takes something to walk.
        if (collectionType is Types.TypeParameter { Constraint: { } constraint })
            collectionType = TypeSimplifier.Expanded(constraint);

        var isRange = collectionType.Equals(IntrinsicTypes.Range);
        var iteratedElement = IteratorElementType(collectionType);
        var functionReturns = iteratedElement == null ? FunctionIteratorReturns(collectionType) : null;
        if (functionReturns == null)
            _semanticModel.TypeSolver.AddConstraint(collectionType, ObjectType.Empty, @for.CollectionExpression);

        var elementType = isRange ? Types.PrimitiveType.Number : iteratedElement ?? GetObjectValueType(collectionType);
        var maxNames = isRange || iteratedElement != null ? 1 : functionReturns?.Count ?? 2;
        if (@for.Names.Count > maxNames)
        {
            _diagnostics.NotImplemented(
                @for.Names[maxNames],
                isRange ? "Iterating over a range only produces one value, so only one name is permitted."
                : iteratedElement != null ? "An iterator's 'next' answers one value, so only one name is permitted."
                : functionReturns != null ? $"This iterator function returns {functionReturns.Count} value(s) per step, so at most {functionReturns.Count} name(s) is permitted."
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
            // A bare function already meets the "call it, stop at nil" protocol IteratorCall wraps an
            // Iterator<T> in to satisfy Luau's generic for - which is what lets a native library's own
            // closure-returning iterator (each/children/query.iter in jecs, for instance) drive the loop
            // directly, with no adapter.
            case var _ when functionReturns != null:
                for (var index = 0; index < @for.Names.Count; index++)
                    BindType(@for.Names[index], functionReturns[index]);

                break;
            case Types.ArrayType:
                BindType(@for.Names[0], elementType);
                if (@for.Names.Count > 1)
                    BindType(@for.Names[1], Types.PrimitiveType.Number);

                break;
            // One name over a keyed collection binds the value, not the key - which is what the generator
            // emits, placing a discard where the key would go. Binding the key here promised a name of one
            // type and handed the loop a value of another, with nothing in between to notice.
            case InterfaceType or ObjectType:
            {
                if (@for.Names.Count == 1)
                {
                    BindType(@for.Names[0], elementType);
                    break;
                }

                BindType(@for.Names[0], ((NativelyIndexableType)collectionType).KeyUnion());
                BindType(@for.Names[1], elementType);
                break;
            }
            // A collection type nothing above recognizes already failed the object constraint above; binding
            // every name here keeps that the only diagnostic instead of a second, confusing "no symbol" one
            // when the body goes on to use a name nothing here ever gave a type.
            default:
                foreach (var name in @for.Names)
                    BindType(name, Types.PrimitiveType.Never);

                break;
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

    /// <summary>
    ///     The values a bare, zero-required-argument function hands the loop each step, positionally - a
    ///     single type for one value, or a tuple return's elements for several. Null for anything else,
    ///     including a function that requires arguments: nothing supplies those on a native Lua for's every
    ///     call, so requiring one is a signature the loop could never actually call correctly.
    /// </summary>
    private static List<Type>? FunctionIteratorReturns(Type collectionType) =>
        collectionType is Types.FunctionType { RequiredParameterTypes.Count: 0 } functionType
            ? functionType.ReturnType is Types.TupleType tuple ? tuple.ElementTypes : [functionType.ReturnType]
            : null;

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
