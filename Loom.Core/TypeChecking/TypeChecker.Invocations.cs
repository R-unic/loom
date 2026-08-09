using Loom.Core.Diagnostics;
using Loom.Core.Generation.Macros;
using Loom.Core.Parsing.AST;
using Loom.Core.Text;
using Loom.Core.TypeChecking.Types;

namespace Loom.Core.TypeChecking;

using Type = Types.Type;

public sealed partial class TypeChecker
{
    public override Type VisitInvocation(Invocation invocation)
    {
        var type = Visit(invocation.Expression);
        CheckPanicIsDeclared(invocation);
        CheckDeprecation(invocation);

        // a?.b() short-circuits to nil at runtime before ever calling 'b', so the callee is
        // checked against its non-nullable type and the call's own result gains '| none' back.
        var isOptionalChainCallee = IsOptionalChainAccess(invocation.Expression);
        if (isOptionalChainCallee)
            type = type.NonNullable();

        if (IsEventType(invocation, type, true, out _))
        {
            _diagnostics.Error(invocation, InternalCodes.InvalidInvocation, "Consumer events may only be observed, not fired.");
            return BindType(invocation, Types.PrimitiveType.Never);
        }

        if (_semanticModel.TryGetIntrinsicAttribute(invocation.Expression, "luau_metamethod", out _))
        {
            _diagnostics.Error(invocation, InternalCodes.InvalidInvocation, "Cannot call a metamethod-backed property directly; use the corresponding operator instead.");
            return BindType(invocation, Types.PrimitiveType.Never);
        }

        Type resultType;
        if (type is Types.FunctionType functionType)
        {
            resultType = functionType.TypeParameters.Count == 0
                ? CheckNonGenericInvocation(invocation, functionType)
                : CheckGenericInvocation(invocation, functionType);
        }
        else if (type is Types.IntersectionType { Types.Count: > 0 } intersection && intersection.Types.TrueForAll(t => t is Types.FunctionType))
        {
            resultType = CheckOverloadedInvocation(invocation, intersection.Types.ConvertAll(t => (Types.FunctionType)t));
        }
        else if (IsEventType(invocation, type, false, out var eventType))
        {
            resultType = CheckEventInvocation(invocation, eventType);
        }
        else
        {
            _diagnostics.Error(invocation, InternalCodes.InvalidInvocation, $"Cannot call value of type '{type}'.");
            return BindType(invocation, Types.PrimitiveType.Never);
        }

        return isOptionalChainCallee && !Type.IsNever(resultType)
            ? BindType(invocation, TypeSimplifier.Simplify(new Types.UnionType([resultType, Types.PrimitiveType.None])))
            : resultType;
    }

    private static bool IsOptionalChainAccess(Expression expression) =>
        expression switch
        {
            QualifiedName qualifiedName => qualifiedName.Names.Exists(n => n.IsOptional),
            PropertyAccess propertyAccess => propertyAccess.Names.Exists(n => n.IsOptional),
            ElementAccess elementAccess => elementAccess.IsOptional,
            _ => false
        };

    // A callee typed as an intersection of function signatures is an overload set (MergeOverloadedProperties); first candidate whose required/optional parameter count fits and whose arguments are assignable wins.
    private Type CheckOverloadedInvocation(Invocation invocation, List<Types.FunctionType> candidates)
    {
        var argumentList = invocation.Arguments.ArgumentList;
        var argumentTypes = argumentList.ConvertAll(Visit);

        var match = candidates.Find(candidate =>
            {
                var fixedCount = candidate.HasRestParameter ? candidate.ParameterTypes.Count - 1 : candidate.ParameterTypes.Count;
                var requiredCount = candidate.ParameterTypes.Take(fixedCount).Count(Type.IsNotOptional);
                var exactRestArity = GetRestExactArity(candidate.ParameterTypes, candidate.HasRestParameter, fixedCount);
                var arityOk = exactRestArity is { } exact
                    ? argumentTypes.Count == exact
                    : candidate.HasRestParameter || argumentTypes.Count <= fixedCount;

                return argumentTypes.Count >= requiredCount
                    && arityOk
                    && !argumentTypes.Where((argumentType, i) =>
                    {
                        var expected = GetArgumentExpectedType(candidate.ParameterTypes, candidate.HasRestParameter, i, fixedCount);
                        return expected != null && !argumentType.IsAssignableTo(expected);
                    }).Any();
            }
        );

        if (match == null)
        {
            _diagnostics.Error(
                invocation,
                InternalCodes.NoOverloadMatch,
                $"No overload matches this call. Candidates:\n{string.Join("\n", candidates.Select(c => "  " + c))}"
            );

            return BindType(invocation, Types.PrimitiveType.Never);
        }

        return match.TypeParameters.Count == 0
            ? CheckNonGenericInvocation(invocation, match)
            : CheckGenericInvocation(invocation, match);
    }

    private Type CheckNonGenericInvocation(Invocation invocation, Types.FunctionType functionType)
    {
        var declaration = _semanticModel.GetSymbol(invocation.Expression)?.Declaration as DeclareFunctionSignature;
        var argumentList = invocation.Arguments.ArgumentList;
        var argumentTypes = BuildArgumentTypes(argumentList, functionType.ParameterTypes, functionType.HasRestParameter);

        return BindNonGenericInvocation(invocation, argumentTypes, functionType, declaration);
    }

    private Type CheckGenericInvocation(Invocation invocation, Types.FunctionType functionType)
    {
        var declaration = _semanticModel.GetSymbol(invocation.Expression)?.Declaration as DeclareFunctionSignature;
        var expectedReturnType = GetContextualType(invocation);

        return invocation.TypeArguments != null
            ? CheckExplicitGenericInvocation(invocation, functionType, declaration, expectedReturnType)
            : CheckInferredGenericInvocation(invocation, functionType, declaration, expectedReturnType);
    }

    private Type CheckExplicitGenericInvocation(
        Invocation invocation,
        Types.FunctionType functionType,
        DeclareFunctionSignature? declaration,
        Type? expectedReturnType)
    {
        var substitution = ResolveTypeArguments(invocation, functionType, [], expectedReturnType);
        if (substitution == null)
            return BindType(invocation, Types.PrimitiveType.Never);

        var substitutedParameterTypes = SubstituteTypeParameters(invocation.Arguments, functionType.ParameterTypes, substitution);
        var substitutedReturnType = SubstituteTypeParameters(invocation, functionType.ReturnType, substitution);
        var argumentList = invocation.Arguments.ArgumentList;
        var argumentTypes = BuildArgumentTypes(argumentList, substitutedParameterTypes, functionType.HasRestParameter);

        CheckArity(invocation.Arguments, declaration?.Parameters, argumentTypes, substitutedParameterTypes, functionType.HasRestParameter);
        return BindType(invocation, substitutedReturnType);
    }

    private Type CheckInferredGenericInvocation(
        Invocation invocation,
        Types.FunctionType functionType,
        DeclareFunctionSignature? declaration,
        Type? expectedReturnType)
    {
        var argumentList = invocation.Arguments.ArgumentList;
        var argumentTypes = argumentList.ConvertAll(Visit);
        var substitution = ResolveTypeArguments(invocation, functionType, argumentTypes, expectedReturnType);
        if (substitution == null)
            return BindType(invocation, Types.PrimitiveType.Never);

        var substitutedParameterTypes = SubstituteTypeParameters(invocation.Arguments, functionType.ParameterTypes, substitution);
        var substitutedReturnType = SubstituteTypeParameters(invocation, functionType.ReturnType, substitution);
        CheckArguments(
            invocation.Arguments,
            declaration?.Parameters,
            argumentTypes,
            substitutedParameterTypes,
            argumentList,
            functionType.HasRestParameter
        );

        return BindType(invocation, substitutedReturnType);
    }

    private Types.PrimitiveType CheckEventInvocation(Invocation invocation, InstantiatedType eventType)
    {
        var argumentList = invocation.Arguments.ArgumentList;
        var argumentTypes = argumentList.ConvertAll(Visit);
        var declaration = _semanticModel.GetSymbol(invocation.Expression)?.Declaration as EventDeclaration
            ?? _semanticModel.GetPropertySymbol(invocation.Expression)?.Declaration as EventDeclaration;

        CheckArguments(invocation.Arguments, declaration?.Parameters, argumentTypes, eventType.Arguments, argumentList);
        return BindType(invocation, Types.PrimitiveType.Void);
    }

    private List<Type> BuildArgumentTypes(List<Expression> argumentList, List<Type> parameterTypes, bool hasRestParameter = false)
    {
        var fixedCount = hasRestParameter ? parameterTypes.Count - 1 : parameterTypes.Count;
        var argumentTypes = new List<Type>(argumentList.Count);
        argumentTypes.AddRange(
            argumentList.Select((t, i) =>
            {
                var expected = GetArgumentExpectedType(parameterTypes, hasRestParameter, i, fixedCount);
                return expected != null ? Check(t, expected) : Visit(t);
            })
        );

        return argumentTypes;
    }

    private void CheckArguments(
        Arguments arguments,
        Parameters? parameters,
        List<Type> argumentTypes,
        List<Type> parameterTypes,
        List<Expression> args,
        bool hasRestParameter = false)
    {
        CheckArity(arguments, parameters, argumentTypes, parameterTypes, hasRestParameter);
        var fixedCount = hasRestParameter ? parameterTypes.Count - 1 : parameterTypes.Count;
        for (var i = 0; i < args.Count; i++)
        {
            var expected = GetArgumentExpectedType(parameterTypes, hasRestParameter, i, fixedCount);
            if (expected != null)
                Check(args[i], expected);
        }
    }

    /// <summary>
    ///     The type argument <paramref name="index" /> should be checked against, or null when it's an extra
    ///     rest-position argument with no uniform element type to check (an array rest with no element type
    ///     information reaching this point, which shouldn't happen but is handled permissively).
    /// </summary>
    private static Type? GetArgumentExpectedType(List<Type> parameterTypes, bool hasRestParameter, int index, int fixedCount)
    {
        if (index < fixedCount)
            return index < parameterTypes.Count ? parameterTypes[index] : null;

        if (!hasRestParameter || parameterTypes.Count == 0)
            return null;

        return parameterTypes[^1] switch
        {
            Types.TupleType restTuple => index - fixedCount < restTuple.ElementTypes.Count ? restTuple.ElementTypes[index - fixedCount] : null,
            Types.ArrayType restArray => restArray.ElementType,
            _ => null
        };
    }

    /// <summary>
    ///     The exact total argument count required when the rest parameter is a fixed-arity tuple (rather than
    ///     an array, which accepts any count), or null when arity isn't constrained to an exact number.
    /// </summary>
    private static int? GetRestExactArity(List<Type> parameterTypes, bool hasRestParameter, int fixedCount) =>
        hasRestParameter && parameterTypes.Count > 0 && parameterTypes[^1] is Types.TupleType restTuple
            ? fixedCount + restTuple.ElementTypes.Count
            : null;

    private void CheckArity(Arguments arguments, Parameters? parameters, List<Type> argumentTypes, List<Type> parameterTypes, bool hasRestParameter = false)
    {
        var fixedParameterTypes = hasRestParameter ? parameterTypes.Take(parameterTypes.Count - 1).ToList() : parameterTypes;
        var requiredParameterTypes = new List<Type>();
        if (parameters == null)
        {
            requiredParameterTypes = fixedParameterTypes.FindAll(Type.IsNotOptional);
        }
        else
        {
            var declaredCount = hasRestParameter ? parameters.ParameterList.Count - 1 : parameters.ParameterList.Count;
            var loopBound = Math.Min(declaredCount, fixedParameterTypes.Count);
            for (var i = 0; i < loopBound; i++)
            {
                var parameterType = fixedParameterTypes[i];
                var parameter = parameters.ParameterList[i];
                if (parameter.EqualsValueClause != null || !Type.IsNotOptional(parameterType)) continue;

                requiredParameterTypes.Add(parameterType);
            }
        }

        var minimum = requiredParameterTypes.Count;
        var maximum = fixedParameterTypes.Count;
        var exactRestArity = GetRestExactArity(parameterTypes, hasRestParameter, maximum);
        if (exactRestArity is { } exact)
        {
            if (argumentTypes.Count == exact) return;

            var tupleArity = exact - maximum;
            _diagnostics.Error(
                arguments,
                InternalCodes.TupleRestArityMismatch,
                $"Tuple rest parameter expects exactly {tupleArity} argument{(tupleArity == 1 ? "" : "s")}, but {Math.Max(argumentTypes.Count - maximum, 0)} were provided."
            );

            return;
        }

        var arityDisplay = hasRestParameter
            ? $"{minimum}+"
            : minimum == maximum
                ? maximum.ToString()
                : $"{minimum}-{maximum}";

        if ((hasRestParameter || argumentTypes.Count <= maximum) && argumentTypes.Count >= minimum) return;

        var s = hasRestParameter || minimum != maximum || maximum != 1 ? "s" : "";
        _diagnostics.Error(arguments, InternalCodes.InvocationArity, $"Function expects {arityDisplay} argument{s}, but {argumentTypes.Count} were provided.");
    }

    private Type BindNonGenericInvocation(Invocation invocation, List<Type> argumentTypes, Types.FunctionType functionType, DeclareFunctionSignature? declaration)
    {
        CheckArity(invocation.Arguments, declaration?.Parameters, argumentTypes, functionType.ParameterTypes, functionType.HasRestParameter);

        // Dropping AddArgumentConstraints here because the Check method already adds equivalent constraints
        return BindType(invocation, functionType.ReturnType);
    }

    private Type? GetContextualType(Expression expression) =>
        expression.Parent switch
        {
            EqualsValueClause equalsValueClause when equalsValueClause.Value == expression
                && equalsValueClause.Parent is VariableDeclaration { ColonTypeClause: not null } variableDeclaration =>
                _semanticModel.GetType(variableDeclaration.ColonTypeClause.Type),

            EqualsValueClause equalsValueClause when equalsValueClause.Value == expression
                && equalsValueClause.Parent is Parameter { ColonTypeClause: not null } parameter =>
                _semanticModel.GetType(parameter.ColonTypeClause.Type),

            Return @return when @return.Expression == expression =>
                GetEnclosingDeclaredReturnType(@return),

            AssignmentOperator { Operator.Kind: SyntaxKind.Equals } assignment
                when assignment.Right == expression =>
                _semanticModel.GetType(assignment.Left),

            Arguments arguments when arguments.ArgumentList.Contains(expression)
                && arguments.Parent is Invocation invocation =>
                GetInvocationArgumentType(invocation, expression),

            _ => null
        };

    private Type? GetInvocationArgumentType(Invocation invocation, Expression argument)
    {
        var index = invocation.Arguments.ArgumentList.IndexOf(argument);
        if (index < 0 || _semanticModel.GetType(invocation.Expression) is not Types.FunctionType functionType)
            return null;

        var fixedCount = functionType.HasRestParameter ? functionType.ParameterTypes.Count - 1 : functionType.ParameterTypes.Count;
        return GetArgumentExpectedType(functionType.ParameterTypes, functionType.HasRestParameter, index, fixedCount);
    }

    /// <summary>
    ///     Reports an invalid macro-reference diagnostic if needed and returns whether
    ///     <paramref name="expression" /> classifies as an invocation macro reference, so
    ///     callers can avoid re-running the classification.
    /// </summary>
    private bool CheckInvocationMacroReference(Expression expression)
    {
        if (!InvocationMacroReference.TryClassify(_semanticModel, expression, out _, out var memberName))
            return false;

        if (InvocationMacroReference.IsValidReferenceContext(expression, _semanticModel) || InvocationMacroReference.IsDirectInvocationCallee(expression))
            return true;

        _diagnostics.Error(
            expression,
            InternalCodes.InvalidMacroReference,
            $"Invocation macro '{memberName}' cannot be used as a value. Call it directly (e.g. {memberName}(...)) or pass it as a function argument."
        );

        return true;
    }
}
