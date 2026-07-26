using Loom.Core.FlowAnalysis;
using Loom.Core.Parsing.AST;
using ArrayType = Loom.Core.TypeChecking.Types.ArrayType;
using PrimitiveType = Loom.Core.TypeChecking.Types.PrimitiveType;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.Core.TypeChecking;

public sealed partial class TypeChecker
{
    private Type Check(Expression expression, Type expected) => Check(expression, expected, _flowState);

    private Type Check(Expression expression, Type expected, FlowState state)
    {
        if (expression is ArrayLiteral arrayLiteral && expected is ArrayType arrayType)
            return CheckArrayLiteral(arrayLiteral, arrayType, state);

        if (expression is MatchExpression matchExpression)
            return CheckMatchExpression(matchExpression, expected, state);

        // Once more specific rules, add more, but for now it'll just be like that.
        return CheckSubsumption(expression, expected, state);
    }

    private Type CheckSubsumption(Expression expression, Type expected, FlowState state)
    {
        var actual = Visit(expression, state);
        if (TryInstantiateGenericFunctionArgument(expression, actual, expected, out var instantiated))
            actual = instantiated;

        if (actual.IsAssignableTo(expected))
            return actual;

        _semanticModel.TypeSolver.AddConstraint(actual, expected, expression);
        return actual;
    }

    private ArrayType CheckArrayLiteral(ArrayLiteral arrayLiteral, ArrayType expected, FlowState state)
    {
        foreach (var element in arrayLiteral.Expressions)
            Check(element, expected.ElementType, state);

        return BindType(arrayLiteral, expected);
    }

    private Type CheckMatchExpression(MatchExpression matchExpression, Type expected, FlowState state)
    {
        var scrutineeType = Visit(matchExpression.Expression, state);
        if (matchExpression.Arms.Count == 0)
            return BindType(matchExpression, PrimitiveType.Never);

        var lastState = _flowState;
        _flowState = state;
        foreach (var arm in matchExpression.Arms)
            CheckMatchArm(arm, scrutineeType, expected);

        _flowState = lastState;

        return BindType(matchExpression, expected);
    }
}