using Loom.Core.Diagnostics;
using Loom.Core.Generation.Macros;
using Loom.Core.Parsing.AST;
using Loom.Core.Text;
using Loom.Core.TypeChecking.Types;

namespace Loom.Core.TypeChecking;

using Type = Types.Type;
using Loom.Core.TypeChecking.Solving;

public sealed partial class TypeChecker
{
    public override Type VisitInvocation(Invocation invocation)
    {
        var type = Visit(invocation.Expression);
        CheckPanicIsDeclared(invocation);
        CheckDeprecation(invocation);
        CheckSimplifiableToSet(invocation);

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

    /// <summary>
    ///     <c>Set::of(...)</c> folds straight to a table literal at compile time; <c>.to_set()</c> on
    ///     anything else has to build the array first and then walk it in a runtime loop
    ///     (<c>ArrayMacroProvider.GenerateToSet</c>). The two are only equivalent when the receiver
    ///     really is an array literal - a variable or a call result still needs the loop.
    /// </summary>
    private void CheckSimplifiableToSet(Invocation invocation)
    {
        if (!TryGetMemberCall(invocation, out var receiver, out var member) || member != "to_set")
            return;

        if (receiver is ArrayLiteral { Expressions: var elements } && elements.TrueForAll(element => element is not SpreadElement))
            _diagnostics.Warn(invocation, InternalCodes.SimplifiableCode, "Use 'Set::of(...)' instead of '.to_set()' on an array literal.");
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
        if (argumentList.Exists(argument => argument is NamedArgument))
        {
            _diagnostics.Error(
                invocation.Arguments,
                InternalCodes.NamedArgumentWithOverload,
                "Named arguments cannot be used when calling an overloaded function."
            );

            foreach (var argument in argumentList)
                Visit(argument);

            return BindType(invocation, Types.PrimitiveType.Never);
        }

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

                        // A position naming one of the candidate's own type parameters has nothing to check
                        // yet - what it would accept depends on the substitution CheckGenericInvocation is
                        // about to infer for whichever candidate arity picks, not the bare, unbound parameter
                        // sitting here. Measuring assignability against that rejects every generic candidate
                        // whose inference would have succeeded, which is the whole overload set whenever more
                        // than one arity is generic.
                        return expected != null && !ContainsTypeParameter(expected, candidate.TypeParameters) && !argumentType.IsAssignableTo(expected);
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

    /// <summary>
    ///     Whether <paramref name="type" /> mentions any of <paramref name="parameters" /> - the same walk
    ///     <see cref="TypeMatcher" />'s own <c>ContainsBinder</c> does, for the same reason: nothing general
    ///     enumerates a type's children except <see cref="TypeSolver.Transform" />.
    /// </summary>
    private static bool ContainsTypeParameter(Type type, List<Types.TypeParameter> parameters)
    {
        if (type is Types.TypeParameter typeParameter && parameters.Exists(p => ReferenceEquals(p, typeParameter)))
            return true;

        var found = false;
        TypeSolver.Transform(
            type,
            child =>
            {
                found |= ContainsTypeParameter(child, parameters);
                return child;
            },
            simplify: false
        );

        return found;
    }

    private Type CheckNonGenericInvocation(Invocation invocation, Types.FunctionType functionType)
    {
        var declaration = _semanticModel.GetSymbol(invocation.Expression)?.Declaration as DeclareFunctionSignature;
        var canonicalized = CanonicalizeArguments(invocation.Arguments, declaration?.Parameters, functionType.HasRestParameter);
        var argumentList = canonicalized ?? invocation.Arguments.ArgumentList;
        var argumentTypes = BuildArgumentTypes(argumentList, functionType.ParameterTypes, functionType.HasRestParameter);

        return BindNonGenericInvocation(invocation, argumentTypes, functionType, declaration, canonicalized != null);
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
        var canonicalized = CanonicalizeArguments(invocation.Arguments, declaration?.Parameters, functionType.HasRestParameter);
        var argumentList = canonicalized ?? invocation.Arguments.ArgumentList;
        var argumentTypes = BuildArgumentTypes(argumentList, substitutedParameterTypes, functionType.HasRestParameter);

        if (canonicalized == null)
            CheckArity(invocation.Arguments, declaration?.Parameters, argumentTypes, substitutedParameterTypes, functionType.HasRestParameter);

        return BindType(invocation, substitutedReturnType);
    }

    private Type CheckInferredGenericInvocation(
        Invocation invocation,
        Types.FunctionType functionType,
        DeclareFunctionSignature? declaration,
        Type? expectedReturnType)
    {
        var canonicalized = CanonicalizeArguments(invocation.Arguments, declaration?.Parameters, functionType.HasRestParameter);
        var argumentList = canonicalized ?? invocation.Arguments.ArgumentList;
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
            functionType.HasRestParameter,
            checkArity: canonicalized == null
        );

        return BindType(invocation, substitutedReturnType);
    }

    private static bool IsContextSensitive(Expression argument) =>
        argument switch
        {
            Parenthesized parenthesized => IsContextSensitive(parenthesized.Expression),
            NamedArgument namedArgument => IsContextSensitive(namedArgument.Value),
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
        var declaration = GetEventDeclaration(invocation.Expression);
        var hasRestParameter = HasRestParameter(declaration?.Parameters);
        var canonicalized = CanonicalizeArguments(invocation.Arguments, declaration?.Parameters, hasRestParameter);
        var argumentList = canonicalized ?? invocation.Arguments.ArgumentList;
        var argumentTypes = argumentList.ConvertAll(Visit);

        // The declared parameters alone: Event<T1..T8> pads to eight, and the unused ones are 'none', which
        // a rest parameter would otherwise be measured against as seven more parameters to fill first.
        CheckArguments(
            invocation.Arguments,
            declaration?.Parameters,
            argumentTypes,
            [.. eventType.Arguments.TakeWhile(Type.IsDefined)],
            argumentList,
            hasRestParameter,
            checkArity: canonicalized == null
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

    /// <summary>The declared parameters a named argument can target - every parameter but a trailing rest one, which collects positional overflow and is never addressed by name.</summary>
    private static List<Parameter>? FixedParameters(Parameters? parameters, bool hasRestParameter)
    {
        if (parameters == null)
            return null;

        return hasRestParameter ? parameters.ParameterList.GetRange(0, parameters.ParameterList.Count - 1) : parameters.ParameterList;
    }

    /// <summary>
    ///     Reorders a call's arguments into declared-parameter order, or returns null when the call has no
    ///     named argument and the raw, positional <see cref="Arguments.ArgumentList" /> can be used exactly as
    ///     it always has been.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Once reordered, every existing positional consumer - <see cref="BuildArgumentTypes" />,
    ///         <see cref="CheckArguments" />, generic inference in <see cref="CheckInferredGenericInvocation" /> -
    ///         keeps working unmodified: index <c>i</c> of the result is the argument for declared parameter
    ///         <c>i</c>, exactly the invariant a purely positional call already satisfied. A defaulted
    ///         parameter skipped by name but followed by one that was supplied becomes an
    ///         <see cref="OmittedArgument" /> so the slot still exists to keep that indexing intact; a
    ///         defaulted parameter with nothing after it is simply left off the end, the same as an ordinary
    ///         trailing omission today.
    ///     </para>
    ///     <para>
    ///         Ill-formed placement (a positional argument after a named one, a named argument alongside a
    ///         spread, a duplicate name) was already reported by the parser
    ///         (<see cref="Parser.ValidateNamedArgumentPlacement" />) from the token shapes alone, so this only
    ///         tolerates it well enough not to crash - it does not re-report it.
    ///     </para>
    /// </remarks>
    private List<Expression>? CanonicalizeArguments(Arguments arguments, Parameters? parameters, bool hasRestParameter)
    {
        var argumentList = arguments.ArgumentList;
        if (!argumentList.Exists(argument => argument is NamedArgument))
            return null;

        if (parameters == null)
        {
            _diagnostics.Error(
                arguments,
                InternalCodes.NamedArgumentUnknownDeclaration,
                "Named arguments can only be used to call a function whose declaration is statically known."
            );

            foreach (var argument in argumentList)
                Visit(argument);

            return argumentList.ConvertAll(argument => argument is NamedArgument named ? named.Value : argument);
        }

        var fixedParameters = FixedParameters(parameters, hasRestParameter)!;
        var slots = new Expression?[fixedParameters.Count];
        var positionalCount = 0;
        foreach (var argument in argumentList)
        {
            if (argument is NamedArgument namedArgument)
            {
                var index = fixedParameters.FindIndex(p => p.Name.Text == namedArgument.Name.Text);
                if (index < 0)
                {
                    _diagnostics.Error(
                        namedArgument,
                        InternalCodes.UnknownArgumentName,
                        $"'{namedArgument.Name.Text}' is not a parameter of this function."
                    );

                    Visit(namedArgument.Value);
                    continue;
                }

                if (slots[index] != null)
                {
                    _diagnostics.Error(
                        namedArgument,
                        InternalCodes.ArgumentSpecifiedMultipleTimes,
                        $"Parameter '{namedArgument.Name.Text}' is already specified."
                    );

                    Visit(namedArgument.Value);
                    continue;
                }

                slots[index] = namedArgument.Value;
                continue;
            }

            if (positionalCount < slots.Length)
            {
                if (slots[positionalCount] != null)
                    _diagnostics.Error(
                        argument,
                        InternalCodes.ArgumentSpecifiedMultipleTimes,
                        $"Parameter '{fixedParameters[positionalCount].Name.Text}' is already specified."
                    );

                slots[positionalCount] = argument;
            }
            else
            {
                _diagnostics.Error(
                    argument,
                    InternalCodes.TooManyPositionalArgumentsWithNamed,
                    "A call cannot mix named arguments with more positional arguments than the function has fixed parameters."
                );

                Visit(argument);
            }

            positionalCount++;
        }

        for (var i = 0; i < slots.Length; i++)
        {
            if (slots[i] != null) continue;

            var parameter = fixedParameters[i];
            if (parameter.EqualsValueClause == null)
                _diagnostics.Error(
                    arguments,
                    InternalCodes.MissingRequiredArgument,
                    $"Missing required argument for parameter '{parameter.Name.Text}'."
                );
        }

        var lastSupplied = Array.FindLastIndex(slots, slot => slot != null);
        var result = new List<Expression>(lastSupplied + 1);
        for (var i = 0; i <= lastSupplied; i++)
            result.Add(slots[i] ?? new OmittedArgument(arguments.RightParen));

        return result;
    }

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
        bool hasRestParameter = false,
        bool checkArity = true)
    {
        if (checkArity)
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

        var fixedParameterTypes = hasRestParameter ? [.. parameterTypes.Take(parameterTypes.Count - 1)] : parameterTypes;
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
        var hint = argumentTypes.Count < minimum
            ? $"pass {minimum - argumentTypes.Count} more argument{(minimum - argumentTypes.Count == 1 ? "" : "s")}"
            : $"remove {argumentTypes.Count - maximum} argument{(argumentTypes.Count - maximum == 1 ? "" : "s")}";

        _diagnostics.Error(
            arguments,
            InternalCodes.InvocationArity,
            $"Function expects {arityDisplay} argument{s}, but {argumentTypes.Count} were provided.",
            hint
        );
    }

    private Type BindNonGenericInvocation(
        Invocation invocation,
        List<Type> argumentTypes,
        Types.FunctionType functionType,
        DeclareFunctionSignature? declaration,
        bool arityAlreadyChecked = false)
    {
        if (!arityAlreadyChecked)
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

            NamedArgument namedArgument when namedArgument.Parent is Arguments arguments
                && arguments.Parent is Invocation invocation =>
                GetInvocationArgumentType(invocation, namedArgument),

            _ => null
        };

    /// <summary>
    ///     The declared parameter type <paramref name="argument" /> - a positional expression or a whole
    ///     <see cref="NamedArgument" /> - lands on. A named argument's position in the call means nothing
    ///     (that is the point of naming it), so its target is looked up by name against the callee's own
    ///     declaration instead of by where it sits in <see cref="Arguments.ArgumentList" />.
    /// </summary>
    private Type? GetInvocationArgumentType(Invocation invocation, Expression argument)
    {
        if (_semanticModel.GetType(invocation.Expression) is not Types.FunctionType functionType)
            return null;

        if (argument is NamedArgument namedArgument)
        {
            var declaration = _semanticModel.GetSymbol(invocation.Expression)?.Declaration as DeclareFunctionSignature;
            var fixedParameters = FixedParameters(declaration?.Parameters, functionType.HasRestParameter);
            var namedIndex = fixedParameters?.FindIndex(p => p.Name.Text == namedArgument.Name.Text) ?? -1;
            return namedIndex < 0 ? null : functionType.ParameterTypeAt(namedIndex);
        }

        var index = invocation.Arguments.ArgumentList.IndexOf(argument);
        return index < 0 ? null : functionType.ParameterTypeAt(index);
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
