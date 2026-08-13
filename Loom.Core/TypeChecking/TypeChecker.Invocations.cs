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

        // calling an 'async fn' starts it and hands back the future it settles, so the call is typed as a
        // Future over the declared return type rather than as the return type itself - 'await' is what
        // takes the value back out
        if (IsAsyncCallee(type) && !Type.IsNever(resultType))
            resultType = BindType(invocation, InstantiateFutureType(invocation, resultType));

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
                        var expected = Types.FunctionType.ParameterTypeAt(candidate.ParameterTypes, candidate.HasRestParameter, i);
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
        var deferred = argumentList.ConvertAll(IsContextSensitive);
        var argumentTypes = argumentList.Select((argument, i) => deferred[i] ? Types.PrimitiveType.Unknown : Visit(argument)).ToList();
        var substitution = ResolveTypeArguments(invocation, functionType, argumentTypes, expectedReturnType);
        if (substitution == null)
            return BindType(invocation, Types.PrimitiveType.Never);

        if (deferred.Contains(true))
        {
            TypeDeferredArguments(invocation, functionType, substitution, argumentList, argumentTypes, deferred);
            substitution = ResolveTypeArguments(invocation, functionType, argumentTypes, expectedReturnType);
            if (substitution == null)
                return BindType(invocation, Types.PrimitiveType.Never);
        }

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

    private static bool IsContextSensitive(Expression argument) =>
        argument switch
        {
            Parenthesized parenthesized => IsContextSensitive(parenthesized.Expression),
            FunctionExpression { Parameters: { } parameters } => parameters.ParameterList.Exists(
                parameter => parameter.ColonTypeClause == null && parameter.EqualsValueClause == null
            ),

            _ => false
        };

    private void TypeDeferredArguments(
        Invocation invocation,
        Types.FunctionType functionType,
        TypeParameterSubstitution substitution,
        List<Expression> argumentList,
        List<Type> argumentTypes,
        List<bool> deferred)
    {
        var parameterTypes = SubstituteTypeParameters(invocation.Arguments, functionType.ParameterTypes, substitution);
        for (var i = 0; i < argumentList.Count; i++)
        {
            if (!deferred[i]) continue;

            var expected = ExpectedArgumentType(argumentList, i, parameterTypes, functionType.HasRestParameter);
            argumentTypes[i] = expected != null ? Check(argumentList[i], expected) : Visit(argumentList[i]);
        }
    }

    private Types.PrimitiveType CheckEventInvocation(Invocation invocation, InstantiatedType eventType)
    {
        var argumentList = invocation.Arguments.ArgumentList;
        var argumentTypes = argumentList.ConvertAll(Visit);
        var declaration = GetEventDeclaration(invocation.Expression);

        // The declared parameters alone: Event<T1..T8> pads to eight, and the unused ones are 'none', which
        // a rest parameter would otherwise be measured against as seven more parameters to fill first.
        CheckArguments(
            invocation.Arguments,
            declaration?.Parameters,
            argumentTypes,
            eventType.Arguments.TakeWhile(Type.IsDefined).ToList(),
            argumentList,
            HasRestParameter(declaration?.Parameters)
        );

        return BindType(invocation, Types.PrimitiveType.Void);
    }

    /// <summary>
    ///     The declaration of the event <paramref name="expression" /> names, whether it is a bare name or a
    ///     member of something. An event's rest parameter lives here and not on its type: <c>Event&lt;T1..T8&gt;</c>
    ///     is positional, so the array a rest parameter declares arrives as just another type argument.
    /// </summary>
    private EventDeclaration? GetEventDeclaration(Expression expression) =>
        _semanticModel.GetSymbol(expression)?.Declaration as EventDeclaration
        ?? _semanticModel.GetPropertySymbol(expression)?.Declaration as EventDeclaration;

    /// <summary>Whether the event <paramref name="expression" /> names was declared with a rest parameter.</summary>
    private bool IsVariadicEvent(Expression expression) => HasRestParameter(GetEventDeclaration(expression)?.Parameters);

    /// <summary>
    ///     The parameter type an argument is checked against, or null where checking it would only mislead.
    /// </summary>
    /// <remarks>
    ///     A spread that lands short of the rest parameter is already reported by
    ///     <see cref="CheckSpreadArguments" />, and comparing it against the fixed parameter it cannot fill
    ///     would go on to say its element type is wrong when its placement is the whole of what is wrong.
    /// </remarks>
    private static Type? ExpectedArgumentType(List<Expression> argumentList, int index, List<Type> parameterTypes, bool hasRestParameter)
    {
        var fixedCount = hasRestParameter ? parameterTypes.Count - 1 : parameterTypes.Count;
        return argumentList[index] is SpreadElement && (!hasRestParameter || index < fixedCount)
            ? null
            : Types.FunctionType.ParameterTypeAt(parameterTypes, hasRestParameter, index);
    }

    private List<Type> BuildArgumentTypes(List<Expression> argumentList, List<Type> parameterTypes, bool hasRestParameter = false)
    {
        var argumentTypes = new List<Type>(argumentList.Count);
        argumentTypes.AddRange(
            argumentList.Select((t, i) =>
            {
                var expected = ExpectedArgumentType(argumentList, i, parameterTypes, hasRestParameter);
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
        for (var i = 0; i < args.Count; i++)
        {
            var expected = ExpectedArgumentType(args, i, parameterTypes, hasRestParameter);
            if (expected != null)
                Check(args[i], expected);
        }
    }

    /// <summary>
    ///     The exact total argument count required when the rest parameter is a fixed-arity tuple (rather than
    ///     an array, which accepts any count), or null when arity isn't constrained to an exact number.
    /// </summary>
    private static int? GetRestExactArity(List<Type> parameterTypes, bool hasRestParameter, int fixedCount) =>
        hasRestParameter && parameterTypes.Count > 0 && parameterTypes[^1] is Types.TupleType restTuple
            ? fixedCount + restTuple.ElementTypes.Count
            : null;

    /// <summary>
    ///     Reports a spread argument that does not land in an array rest parameter.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A rest parameter is the only place a count nobody knows until runtime can go: everything from
    ///         it on arrives as one array, so how many elements the spread carries changes nothing about
    ///         which parameter anything lands on. A fixed parameter has to know which argument it is being
    ///         handed, and a tuple rest parameter has an exact arity, so neither can be given one.
    ///     </para>
    ///     <para>
    ///         Reported here because <see cref="CheckArity" /> is the one thing every invocation path calls
    ///         exactly once, whichever overload or instantiation it settled on first.
    ///     </para>
    /// </remarks>
    private void CheckSpreadArguments(Arguments arguments, List<Type> parameterTypes, bool hasRestParameter)
    {
        var argumentList = arguments.ArgumentList;
        var fixedCount = hasRestParameter ? parameterTypes.Count - 1 : parameterTypes.Count;
        for (var i = 0; i < argumentList.Count; i++)
        {
            if (argumentList[i] is not SpreadElement spreadElement)
                continue;

            if (!hasRestParameter)
            {
                _diagnostics.Error(
                    spreadElement,
                    InternalCodes.InvalidSpreadArgument,
                    "Only a rest parameter may be given a spread argument.",
                    "this function takes a fixed number of arguments, so pass them one at a time"
                );
            }
            else if (parameterTypes is [.., Types.TupleType restTuple])
            {
                _diagnostics.Error(
                    spreadElement,
                    InternalCodes.InvalidSpreadArgument,
                    $"Rest parameter of type '{restTuple}' expects an exact number of arguments, so it cannot be given a spread argument."
                );
            }
            else if (i < fixedCount)
            {
                _diagnostics.Error(
                    spreadElement,
                    InternalCodes.InvalidSpreadArgument,
                    $"A spread argument must come after every fixed parameter, and {fixedCount - i} of them {(fixedCount - i == 1 ? "is" : "are")} still unfilled."
                );
            }
        }
    }

    private void CheckArity(Arguments arguments, Parameters? parameters, List<Type> argumentTypes, List<Type> parameterTypes, bool hasRestParameter = false)
    {
        CheckSpreadArguments(arguments, parameterTypes, hasRestParameter);

        // A spread stands for however many arguments it carries, so there is no count left to compare. Where
        // one is placed as it must be, every fixed parameter is already filled by an argument ahead of it and
        // the rest parameter takes any number; where it is not, that is what CheckSpreadArguments just said.
        if (arguments.ArgumentList.Exists(argument => argument is SpreadElement))
            return;

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

        return functionType.ParameterTypeAt(index);
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

        if (InvocationMacroReference.IsDirectInvocationCallee(expression))
            return true;

        if (InvocationMacroReference.IsValidReferenceContext(expression, _semanticModel))
        {
            // Referencing a macro emits a lambda with one parameter per declared parameter, which cannot
            // stand for a variadic one - the lambda would take a single argument and silently drop the
            // rest. There is nothing to fall back on, since the macro has no runtime definition to pass.
            if (_semanticModel.GetType(expression) is not Types.FunctionType { HasRestParameter: true })
                return true;

            _diagnostics.Error(
                expression,
                InternalCodes.InvalidMacroReference,
                $"Invocation macro '{memberName}' takes a variable number of arguments, so it cannot be passed as a value.",
                $"call it directly (e.g. {memberName}(...)), or wrap it in a function of your own"
            );

            return true;
        }

        _diagnostics.Error(
            expression,
            InternalCodes.InvalidMacroReference,
            $"Invocation macro '{memberName}' cannot be used as a value. Call it directly (e.g. {memberName}(...)) or pass it as a function argument."
        );

        return true;
    }
}
