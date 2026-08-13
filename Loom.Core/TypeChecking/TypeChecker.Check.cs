using Loom.Core.Diagnostics;
using Loom.Core.FlowAnalysis;
using Loom.Core.Parsing.AST;
using Loom.Core.TypeChecking.Serialization;
using Loom.Core.Text;
using ArrayType = Loom.Core.TypeChecking.Types.ArrayType;
using PrimitiveType = Loom.Core.TypeChecking.Types.PrimitiveType;
using Type = Loom.Core.TypeChecking.Types.Type;
using UnionType = Loom.Core.TypeChecking.Types.UnionType;

namespace Loom.Core.TypeChecking;

public sealed partial class TypeChecker
{
    private Type Check(Expression expression, Type expected) => Check(expression, expected, _flowState, out _);

    private Type Check(Expression expression, Type expected, FlowState state) => Check(expression, expected, state, out _);

    private Type Check(Expression expression, Type expected, FlowState state, out TypeSolver.TypeConstraint? constraint)
    {
        constraint = null;

        return expression switch
        {
            Parenthesized parenthesized => BindType(parenthesized, Check(parenthesized.Expression, expected, state, out constraint)),
            ArrayLiteral arrayLiteral when expected is ArrayType arrayType => CheckArrayLiteral(arrayLiteral, arrayType, state),
            TupleExpression tupleExpression when expected is Types.TupleType tupleType => CheckTupleExpression(tupleExpression, tupleType, state),
            MatchExpression matchExpression => CheckMatchExpression(matchExpression, expected, state),
            TernaryOperator ternaryOperator => CheckTernaryOperator(ternaryOperator, expected, state),
            BinaryOperator { Operator.Kind: SyntaxKind.QuestionQuestion or SyntaxKind.QuestionQuestionEquals } nullCoalesce => CheckNullCoalesce(
                nullCoalesce,
                expected,
                state
            ),
            InterfaceInvocation interfaceInvocation => CheckInterfaceInvocation(interfaceInvocation, expected, state),
            FunctionExpression functionExpression when expected is Types.FunctionType expectedFunction => CheckFunctionExpression(
                functionExpression,
                expectedFunction,
                state
            ),
            _ => CheckSubsumption(expression, expected, state, out constraint)
        };

    }

    private Types.FunctionType CheckFunctionExpression(FunctionExpression functionExpression, Types.FunctionType expected, FlowState state)
    {
        var lastState = _flowState;
        _flowState = state;

        var typeParameters = functionExpression.TypeParameters?.ParameterList.ConvertAll(VisitTypeParameter) ?? [];
        var parameterList = functionExpression.Parameters?.ParameterList ?? [];
        var parameterTypes = new List<Type>(parameterList.Count);
        for (var i = 0; i < parameterList.Count; i++)
        {
            var parameter = parameterList[i];
            var declaredType = MaybeVisit(parameter.ColonTypeClause);
            var initializerType = MaybeVisit(parameter.EqualsValueClause);
            var contextualType = i < expected.ParameterTypes.Count ? expected.ParameterTypes[i] : null;
            var type = declaredType ?? contextualType ?? initializerType;
            if (type == null)
            {
                _diagnostics.Error(parameter, InternalCodes.MustHaveDefaultOrType, "Parameter must have a declared type or default value to infer from.");
                type = PrimitiveType.Unknown;
            }

            if (declaredType != null && contextualType != null)
                _semanticModel.TypeSolver.AddConstraint(contextualType, declaredType, parameter.ColonTypeClause!.Type);

            if (declaredType != null && parameter.EqualsValueClause != null)
                _semanticModel.TypeSolver.AddConstraint(initializerType!, declaredType, parameter.EqualsValueClause.Value);

            parameterTypes.Add(BindType(parameter, type));
        }

        MaybeVisit(functionExpression.ReturnType);
        var returnType = GetReturnType(functionExpression);
        var functionType = BindType(
            functionExpression,
            new Types.FunctionType(
                typeParameters,
                parameterTypes,
                returnType,
                HasRestParameter(functionExpression.Parameters),
                functionExpression.AsyncKeyword != null
            )
        );

        if (functionExpression.Body is ExpressionBody body)
        {
            if (functionExpression.ReturnType != null)
                Check(body.Expression, returnType);
        }
        else
        {
            Visit(functionExpression.Body);
        }

        _flowState = lastState;
        return functionType;
    }
    
    private Type CheckSubsumption(Expression expression, Type expected, FlowState state, out TypeSolver.TypeConstraint? constraint)
    {
        constraint = null;
        var actual = Visit(expression, state);
        if (TryInstantiateGenericFunctionArgument(expression, actual, expected, out var instantiated))
            actual = instantiated;

        CheckSizedNumberRange(expression, expected);

        if (actual.IsAssignableTo(expected))
            return actual;

        constraint = _semanticModel.TypeSolver.AddConstraint(actual, expected, expression);
        return actual;
    }

    private void CheckSizedNumberRange(Expression expression, Type expected)
    {
        if (expected.NonNullable() is not Types.SizedNumberType sizedNumberType || !TryGetNumericLiteralValue(expression, out var value))
            return;

        var (minimum, maximum) = sizedNumberType.NumberType.Range();
        if (value < minimum || value > maximum)
            _diagnostics.Warn(
                expression,
                InternalCodes.NumberOutOfRange,
                $"'{value}' is out of range for '{sizedNumberType}' ({minimum} to {maximum})."
            );
    }

    private static bool TryGetNumericLiteralValue(Expression expression, out double value)
    {
        switch (expression)
        {
            case Literal { Value: long longValue }:
                value = longValue;
                return true;
            case Literal { Value: int intValue }:
                value = intValue;
                return true;
            case Literal { Value: double doubleValue }:
                value = doubleValue;
                return true;
            case UnaryOperator { Operator.Kind: SyntaxKind.Minus } unaryOperator when TryGetNumericLiteralValue(unaryOperator.Operand, out var operandValue):
                value = -operandValue;
                return true;
            default:
                value = 0;
                return false;
        }
    }

    private ArrayType CheckArrayLiteral(ArrayLiteral arrayLiteral, ArrayType expected, FlowState state)
    {
        var elementTypes = new List<Type>(arrayLiteral.Expressions.Count);
        var elementConstraints = new List<TypeSolver.TypeConstraint>();
        foreach (var element in arrayLiteral.Expressions)
        {
            TypeSolver.TypeConstraint? constraint;
            if (element is SpreadElement spreadElement)
            {
                // Immutable, because the copy is what lands in the result: a 'mut' operand spreads into an
                // immutable array and an immutable one spreads into a 'mut' array.
                var operandType = Check(spreadElement.Expression, new ArrayType(expected.ElementType, false), state, out constraint);
                BindType(spreadElement, operandType);
                elementTypes.Add(SpreadedElementType(spreadElement, operandType));
            }
            else
            {
                elementTypes.Add(Check(element, expected.ElementType, state, out constraint));
            }

            if (constraint != null)
                elementConstraints.Add(constraint);
        }

        if (elementConstraints.Count <= 0)
            return BindType(arrayLiteral, expected);

        var actualElementType = TypeSimplifier.Simplify(new UnionType(elementTypes.ConvertAll(t => t.Widen())));
        var actualArrayType = new ArrayType(actualElementType, expected.IsMutable);
        var trace = new TypeSolver.TypeMismatchTrace(actualArrayType, expected);
        foreach (var constraint in elementConstraints)
            constraint.Trace = trace;

        return BindType(arrayLiteral, expected);
    }

    private Types.TupleType CheckTupleExpression(TupleExpression tupleExpression, Types.TupleType expected, FlowState state)
    {
        if (tupleExpression.Expressions.Count != expected.ElementTypes.Count)
        {
            _diagnostics.Error(
                tupleExpression,
                InternalCodes.TupleArityMismatch,
                $"Tuple type '{expected}' expects {expected.ElementTypes.Count} element(s), but {tupleExpression.Expressions.Count} were provided."
            );

            return BindType(tupleExpression, expected);
        }

        for (var i = 0; i < tupleExpression.Expressions.Count; i++)
            Check(tupleExpression.Expressions[i], expected.ElementTypes[i], state);

        return BindType(tupleExpression, expected);
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

    private Type CheckTernaryOperator(TernaryOperator ternaryOperator, Type expected, FlowState state)
    {
        var conditionType = Visit(ternaryOperator.Condition, state);
        _semanticModel.TypeSolver.AddConstraint(conditionType, PrimitiveType.Bool, ternaryOperator.Condition);

        var (trueState, falseState) = _narrower.ComputeBranchStates(ternaryOperator.Condition, state);
        Check(ternaryOperator.ThenBranch, expected, trueState);
        Check(ternaryOperator.ElseBranch, expected, falseState);

        return BindType(ternaryOperator, expected);
    }

    private Type CheckNullCoalesce(BinaryOperator nullCoalesce, Type expected, FlowState state)
    {
        var leftType = Visit(nullCoalesce.Left, state);
        var rightType = Check(nullCoalesce.Right, expected, state);

        if (!Type.IsOptional(leftType))
            _diagnostics.Warn(nullCoalesce, InternalCodes.RedundantCode, $"Null coalescing has no effect since '{leftType}' is not optional.");

        return BindType(nullCoalesce, TypeSimplifier.Simplify(new UnionType([leftType, rightType]).NonNullable()));
    }
}