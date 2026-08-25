using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Text;
using Loom.Core.TypeChecking.Types;

namespace Loom.Core.TypeChecking;

using Type = Types.Type;
using Loom.Core.TypeChecking.Operators;
using Loom.Core.TypeChecking.Solving;
using Loom.Core.TypeChecking.Intrinsic;

public sealed partial class TypeChecker
{
    public override Type VisitAssignmentOperator(AssignmentOperator assignmentOperator)
    {
        Type resultType;
        if (assignmentOperator.Operator.Kind != SyntaxKind.Equals)
        {
            resultType = VisitBinaryOperator(assignmentOperator);
        }
        else
        {
            // Without narrowing: checking a fresh value needs the target's full declared type, not
            // whatever a prior `??=`/etc. last narrowed it to (else a legal `n = none` could wrongly fail).
            var targetState = _narrower.WithoutNarrowing(assignmentOperator.Left, _flowState);
            var targetType = Visit(assignmentOperator.Left, targetState);
            var valueType = Check(assignmentOperator.Right, targetType);
            resultType = CheckImmutableAssignmentTarget(assignmentOperator, valueType);
        }

        NarrowAfterAssignment(assignmentOperator, resultType);
        return resultType;
    }

    private void NarrowAfterAssignment(AssignmentOperator assignmentOperator, Type resultType)
    {
        // Only CheckStatements can thread a narrowed state forward via _exitStates, and only for an
        // assignment that's its own statement - one nested in a larger expression has no entry to attach to.
        if (assignmentOperator.Parent is not ExpressionStatement statement)
            return;

        // Skip if resultType isn't even assignable to the target's type - e.g. `handler += callback` on
        // an event field returns a ScriptConnection, not a new value for `handler`.
        var targetType = _semanticModel.GetType(assignmentOperator.Left);
        if (!resultType.IsAssignableTo(targetType))
            return;

        if (_narrower.TryNarrowAfterAssignment(assignmentOperator.Left, resultType, _flowState) is { } narrowed)
            _exitStates[statement] = narrowed;
    }

    private Type CheckImmutableAssignmentTarget(AssignmentOperator assignmentOperator, Type valueType)
    {
        if (assignmentOperator.Left is not (ElementAccess or PropertyAccess or QualifiedName))
            return BindType(assignmentOperator, valueType);

        var expression = assignmentOperator.Left switch
        {
            ElementAccess access => access.Expression,
            PropertyAccess propertyAccess => propertyAccess.Expression,
            QualifiedName name => name.Identifier,
            _ => null!
        };

        // Expanded, since what is being asked is whether the member written through is mutable, and a
        // target written as an instantiation ('ImmutRecord<string, bool>') only has members once expanded.
        var expressionType = TypeSimplifier.Expanded(_semanticModel.GetType(expression));
        if (expressionType is Types.PrimitiveType { Kind: PrimitiveTypeKind.String })
            expressionType = IntrinsicTypes.StringMembers;

        var indexType = assignmentOperator.Left switch
        {
            ElementAccess access => _semanticModel.GetType(access.IndexExpression),
            PropertyAccess propertyAccess => new Types.LiteralType(propertyAccess.Names[0].Name.Text),
            QualifiedName name => new Types.LiteralType(name.Names[0].Name.Text),
            _ => null!
        };

        if (expressionType is not NativelyIndexableType indexableType)
            return BindType(assignmentOperator, valueType);

        // read only, so the node's own list is walked rather than copied
        IReadOnlyList<DotName> names = assignmentOperator.Left switch
        {
            PropertyAccess propertyAccess => propertyAccess.Names,
            QualifiedName name => name.Names,
            _ => []
        };

        if (names.Count > 1)
        {
            foreach (var name in names.SkipLast(1))
            {
                var property = indexableType.GetProperty(name.Name.Text);
                if (property?.ValueType is not NativelyIndexableType nestedIndexable)
                    return BindType(assignmentOperator, valueType);

                indexableType = nestedIndexable;
            }

            indexType = new Types.LiteralType(names[^1].Name.Text);
        }

        var (bodyType, _) = indexableType.GetTypeAtIndex(indexType);
        if (bodyType is not { IsMutable: false })
            return BindType(assignmentOperator, valueType);

        var display = bodyType switch
        {
            ObjectProperty property => $"property '{property.Name}'.",
            ObjectIndexer indexer => $"index '{indexer.KeyType}'.",
            _ => ""
        };

        _diagnostics.Error(assignmentOperator, InternalCodes.AssignToImmutable, $"Cannot assign to immutable {display}");

        // Dropping AddConstraint here because the Check method already does it
        return BindType(assignmentOperator, valueType);
    }

    public override Type VisitTernaryOperator(TernaryOperator ternaryOperator)
    {
        var conditionType = Visit(ternaryOperator.Condition);
        _semanticModel.TypeSolver.AddConstraint(conditionType, Types.PrimitiveType.Bool, ternaryOperator.Condition);

        var (trueState, falseState) = _narrower.ComputeBranchStates(ternaryOperator.Condition, _flowState);
        var thenBranchType = Visit(ternaryOperator.ThenBranch, trueState);
        var elseBranchType = Visit(ternaryOperator.ElseBranch, falseState);
        var union = new Types.UnionType([thenBranchType, elseBranchType]);
        return BindType(ternaryOperator, TypeSimplifier.Simplify(union));
    }

    public override Type VisitBinaryOperator(BinaryOperator binaryOperator)
    {
        var leftType = Visit(binaryOperator.Left);

        if (binaryOperator.Operator.Kind is SyntaxKind.PlusEquals or SyntaxKind.MinusEquals or SyntaxKind.CaretEquals
            && TryGetEventParameterTypes(binaryOperator, leftType, out var eventParameters))
        {
            var assignableFunction = new Types.FunctionType([], eventParameters, Types.PrimitiveType.Void, IsVariadicEvent(binaryOperator.Left));
            var handlerType = Check(binaryOperator.Right, assignableFunction);
            _semanticModel.TypeSolver.AddConstraint(handlerType, assignableFunction, binaryOperator.Right);
            return BindType(binaryOperator, GetIntrinsicType(binaryOperator, "ScriptConnection"));
        }

        Type rightType;
        switch (binaryOperator.Operator.Kind)
        {
            case SyntaxKind.AmpersandAmpersand or SyntaxKind.AmpersandAmpersandEquals:
                var (trueState, _) = _narrower.ComputeBranchStates(binaryOperator.Left, _flowState);
                rightType = Visit(binaryOperator.Right, trueState);
                break;
            case SyntaxKind.PipePipe or SyntaxKind.PipePipeEquals:
                var (_, falseState) = _narrower.ComputeBranchStates(binaryOperator.Left, _flowState);
                rightType = Visit(binaryOperator.Right, falseState);
                break;
            default:
                rightType = Visit(binaryOperator.Right);
                break;
        }
        
        if (TryBindBinaryOperatorOverload(binaryOperator, leftType, rightType, out var overloadType))
            return BindType(binaryOperator, overloadType);

        var rule = BinaryOperatorBinder.GetRule(binaryOperator, leftType, rightType);
        if (rule != null)
        {
            _semanticModel.TypeSolver.AddConstraint(leftType, rule.LeftType, binaryOperator.Left);
            _semanticModel.TypeSolver.AddConstraint(rightType, rule.RightType, binaryOperator.Right);

            if (IsBitwiseOperator(binaryOperator.Operator.Kind)
                && GetEnumMemberOwner(binaryOperator.Left) is { } enumDeclaration
                && enumDeclaration == GetEnumMemberOwner(binaryOperator.Right)
                && _semanticModel.GetType(enumDeclaration) is ObjectType enumObjectType)
                return BindType(binaryOperator, enumObjectType.PropertyUnion());

            return BindType(binaryOperator, rule.ReturnType);
        }

        switch (binaryOperator.Operator.Kind)
        {
            case SyntaxKind.QuestionQuestion or SyntaxKind.QuestionQuestionEquals:
                return BindType(binaryOperator, ResolveNullCoalesceType(binaryOperator, leftType, rightType));
        }

        var suggestion = BinaryOperatorBinder.GetSuggestion(binaryOperator, leftType, rightType);
        var hint = Diagnostic.FormatBinaryHint(binaryOperator, leftType, rightType, suggestion);
        _diagnostics.Error(
            binaryOperator,
            InternalCodes.InvalidBinaryOp,
            $"No binary operation for '{leftType.Widen()}' {binaryOperator.Operator.Text} '{rightType.Widen()}'.",
            hint
        );

        return BindType(binaryOperator, Types.PrimitiveType.Never);
    }

    public override Type VisitUnaryOperator(UnaryOperator unaryOperator)
    {
        var operandType = Visit(unaryOperator.Operand);
        var rule = UnaryOperatorBinder.GetRule(unaryOperator, operandType);
        if (rule != null)
            return rule.ReturnType;

        var suggestion = UnaryOperatorBinder.GetSuggestion(unaryOperator, operandType);
        var hint = Diagnostic.FormatUnaryHint(unaryOperator, operandType, suggestion);
        _diagnostics.Error(unaryOperator, InternalCodes.InvalidUnaryOp, $"No unary operation for {unaryOperator.Operator.Text}{operandType.Widen()}.", hint);

        return BindType(unaryOperator, Types.PrimitiveType.Never);
    }
    
    private static bool TryBindBinaryOperatorOverload(BinaryOperator binaryOperator, Type leftType, Type rightType, out Type resultType)
    {
        resultType = Types.PrimitiveType.Never;
        if (BinaryOperatorBinder.GetMetamethodName(binaryOperator.Operator.Kind) is not { } metamethodName)
            return false;

        if (leftType is InstantiatedType instantiated)
            leftType = instantiated.Expand();

        if (leftType is not InterfaceType interfaceType || !interfaceType.Metamethods.TryGetValue(metamethodName, out var methodName))
            return false;

        if (interfaceType.GetProperty(methodName)?.ValueType is not Types.FunctionType { ParameterTypes: [var parameterType] } functionType
            || !rightType.IsAssignableTo(parameterType))
            return false;

        resultType = functionType.ReturnType;
        return true;
    }

    private static bool IsBitwiseOperator(SyntaxKind kind) =>
        kind is SyntaxKind.Ampersand or SyntaxKind.Pipe or SyntaxKind.Tilde or SyntaxKind.LArrowLArrow or SyntaxKind.RArrowRArrow or SyntaxKind.RArrowRArrowRArrow;

    private EnumDeclaration? GetEnumMemberOwner(Expression expression) =>
        expression switch
        {
            QualifiedName name when _semanticModel.GetDeclaringSymbol(name.Identifier) is { Declaration: EnumDeclaration declaration } => declaration,
            PropertyAccess access when _semanticModel.GetDeclaringSymbol(access.Expression) is { Declaration: EnumDeclaration declaration } => declaration,
            ElementAccess elementAccess when _semanticModel.GetDeclaringSymbol(elementAccess.Expression) is { Declaration: EnumDeclaration declaration } => declaration,
            _ => null
        };
}
