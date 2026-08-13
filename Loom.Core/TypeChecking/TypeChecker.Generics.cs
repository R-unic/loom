global using TypeParameterSubstitution = System.Collections.Generic.Dictionary<Loom.Core.TypeChecking.Types.TypeParameter, Loom.Core.TypeChecking.Types.Type>;
using System.Diagnostics.CodeAnalysis;
using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.TypeChecking.Types;
using FunctionType = Loom.Core.TypeChecking.Types.FunctionType;
using IndexedType = Loom.Core.TypeChecking.Types.IndexedType;
using PrimitiveType = Loom.Core.TypeChecking.Types.PrimitiveType;
using Type = Loom.Core.TypeChecking.Types.Type;
using TypeParameter = Loom.Core.TypeChecking.Types.TypeParameter;
using ConditionalType = Loom.Core.TypeChecking.Types.ConditionalType;

namespace Loom.Core.TypeChecking;

public sealed partial class TypeChecker
{
    private TypeParameterSubstitution? ResolveExplicitInterfaceTypeArguments(InterfaceInvocation node, GenericType generic)
    {
        var arguments = node.TypeArguments!.ArgumentsList.ConvertAll(Visit);
        if (!CheckGenericArity(node.TypeArguments, generic.Parameters, arguments, $"Interface '{generic}'"))
            return null;

        var resolved = FillGenericArguments(generic.Parameters, arguments);
        var substitution = new TypeParameterSubstitution();
        for (var i = 0; i < generic.Parameters.Count; i++)
            substitution[generic.Parameters[i]] = resolved[i];

        return substitution;
    }

    private static List<Type> FillGenericArguments(List<TypeParameter> parameters, List<Type> given) =>
        parameters.Select((t, i) => i < given.Count ? given[i] : t.DefaultType ?? PrimitiveType.Unknown).ToList();

    private TypeParameterSubstitution? ResolveTypeArguments(
        Invocation invocation,
        FunctionType functionType,
        List<Type> argumentTypes,
        Type? expectedReturnType)
    {
        var substitution = new TypeParameterSubstitution();
        if (invocation.TypeArguments != null)
        {
            var explicitArguments = invocation.TypeArguments.ArgumentsList.ConvertAll(Visit);
            if (!CheckGenericArity(invocation, functionType.TypeParameters, explicitArguments, "Function"))
                return null;

            for (var i = 0; i < explicitArguments.Count; i++)
                substitution[functionType.TypeParameters[i]] = explicitArguments[i];
        }
        else
        {
            var inferred = TypeInferrer.InferFunctionTypeArguments(functionType, argumentTypes, expectedReturnType);
            foreach (var (tp, type) in inferred)
                substitution[tp] = type;
        }

        var resolvedConstraints = ResolveConstraints(invocation, functionType.TypeParameters, substitution);
        foreach (var tp in functionType.TypeParameters)
            if (substitution.TryGetValue(tp, out var substitutedType) && tp.Constraint != null)
                if (!CheckTypeParameterConstraints(invocation, substitutedType, tp, resolvedConstraints.GetValueOrDefault(tp)))
                    return null;

        return substitution;
    }

    private Type InstantiateGenericType(Node node, TypeArguments? typeArguments, GenericType genericType)
    {
        var arguments = typeArguments?.ArgumentsList.ConvertAll(Visit) ?? [];
        if (!CheckGenericArity(typeArguments ?? node, genericType.Parameters, arguments, $"Type '{genericType}'"))
            return BindType(node, PrimitiveType.Never);

        var fullArguments = FillGenericArguments(genericType.Parameters, arguments);
        var substitution = new TypeParameterSubstitution();
        for (var i = 0; i < genericType.Parameters.Count; i++)
            substitution[genericType.Parameters[i]] = fullArguments[i];

        var resolvedConstraints = ResolveConstraints(node, genericType.Parameters, substitution);
        for (var i = 0; i < genericType.Parameters.Count; i++)
        {
            var parameter = genericType.Parameters[i];
            if (parameter.Constraint == null) continue;
            CheckTypeParameterConstraints(node, fullArguments[i], parameter, resolvedConstraints.GetValueOrDefault(parameter));
        }

        ConditionalTypeEvaluator.TakeOverflow();
        var instantiated = genericType.Construct(fullArguments);
        ReportUnresolvableKeyOf(node, instantiated.Expand());
        if (ConditionalTypeEvaluator.TakeOverflow())
            _diagnostics.Error(
                node,
                InternalCodes.ConditionalTypeTooDeep,
                $"Resolving '{genericType.Declaration.Name.Text}' here did not finish within the conditional type recursion limits.",
                "give the recursion a case that stops, or write the type out"
            );

        return BindType(node, instantiated);
    }

    /// <summary>
    ///     Whether <paramref name="type" /> satisfies <paramref name="parameter" />'s constraint, measured
    ///     against <paramref name="resolvedConstraint" /> where the caller has one.
    /// </summary>
    /// <remarks>
    ///     A constraint that names another parameter - <c>K: keyof(T)</c> - only means anything once T is
    ///     bound, so an instantiation resolves it in the scope it is being instantiated into and passes it
    ///     here. Comparing against the declared form instead rejects every argument, since nothing is
    ///     assignable to an unresolved <c>keyof(T)</c>.
    /// </remarks>
    private bool CheckTypeParameterConstraints(Node node, Type type, TypeParameter parameter, Type? resolvedConstraint = null)
    {
        var constraint = resolvedConstraint ?? parameter.Constraint;
        if (constraint == null) return true;
        if (type is TypeParameter otherParameter)
            type = otherParameter.Constraint ?? PrimitiveType.Unknown;

        if (type.IsAssignableTo(constraint)) return true;

        _diagnostics.Error(
            node,
            InternalCodes.ConstraintViolation,
            $"Type '{type}' does not satisfy constraint '{constraint}' for type parameter '{parameter.Name}'."
        );

        return false;
    }

    /// <summary>
    ///     Reports a <c>keyof</c> left unresolved by expansion because the argument it landed on has no keys.
    /// </summary>
    /// <remarks>
    ///     <c>VisitKeyOf</c> catches this where the target is written out, but inside a generic the target is
    ///     a parameter and there is nothing to complain about until an argument arrives - so
    ///     <c>type Keys&lt;T&gt; = keyof(T); Keys&lt;number&gt;</c> would otherwise be silently inert.
    /// </remarks>
    private void ReportUnresolvableKeyOf(Node node, Type type)
    {
        var visited = new HashSet<Type>(ReferenceEqualityComparer.Instance);
        report(type);

        return;

        void report(Type current)
        {
            if (!visited.Add(current))
                return;

            if (current is KeyOfType { Target: not (TypeParameter or TypeVariable or IndexedType or KeyOfType) } unresolved)
            {
                _diagnostics.Error(node, InternalCodes.InvalidKeyOf, $"Cannot access keys of type '{unresolved.Target.Widen()}'.");
                return;
            }

            TypeSolver.Transform(
                current,
                child =>
                {
                    report(child);
                    return child;
                }
            );
        }
    }

    /// <summary>
    ///     Each parameter's constraint with every parameter of the same generic substituted, so one written
    ///     over another is measured against what that other turned out to be.
    /// </summary>
    private Dictionary<TypeParameter, Type> ResolveConstraints(Node node, List<TypeParameter> parameters, TypeParameterSubstitution substitution)
    {
        var resolved = new Dictionary<TypeParameter, Type>();
        foreach (var parameter in parameters)
            // Only a constraint carrying a deferred operator is substituted. Substitution rebuilds every
            // composite it walks, and an interface put back together from its parts is no longer the one the
            // argument was checked against - 'Instance' would stop satisfying 'Instance'. A constraint with
            // no 'keyof' or 'T[K]' in it has nothing an argument could resolve anyway.
            if (parameter.Constraint != null && ContainsDeferredOperator(parameter.Constraint))
                resolved[parameter] = SubstituteTypeParameters(node, parameter.Constraint, substitution);

        return resolved;
    }

    private static bool ContainsDeferredOperator(Type type)
    {
        var visited = new HashSet<Type>(ReferenceEqualityComparer.Instance);
        return contains(type);

        bool contains(Type current)
        {
            if (current is KeyOfType or IndexedType or ConditionalType or MappedType)
                return true;

            if (!visited.Add(current))
                return false;

            var found = false;
            TypeSolver.Transform(
                current,
                child =>
                {
                    found |= contains(child);
                    return child;
                }
            );

            return found;
        }
    }

    private bool CheckGenericArity(Node node, List<TypeParameter> parameters, List<Type> arguments, string genericKind)
    {
        var minimum = parameters.Count(p => p.DefaultType == null);
        var maximum = parameters.Count;
        var arityDisplay = minimum == maximum ? minimum.ToString() : $"{minimum}-{maximum}";
        if (arguments.Count >= minimum && arguments.Count <= maximum)
            return true;

        _diagnostics.Error(
            node,
            InternalCodes.GenericArity,
            $"{genericKind} expects {arityDisplay} type argument{(minimum != maximum || maximum != 1 ? "s" : "")}, but {arguments.Count} were provided."
        );

        return false;
    }

    private ObjectType SubstituteObjectType(Node failNode, ObjectType objectType, TypeParameterSubstitution substitution)
    {
        var newProperties = objectType.Properties.ConvertAll(property => new ObjectProperty(
                property.IsMutable,
                property.Name,
                SubstituteTypeParameters(failNode, property.ValueType, substitution)
            )
        );

        ObjectIndexer? newIndexer = null;
        if (objectType.Indexer != null)
            newIndexer = new ObjectIndexer(
                objectType.Indexer.IsMutable,
                SubstituteTypeParameters(failNode, objectType.Indexer.KeyType, substitution),
                SubstituteTypeParameters(failNode, objectType.Indexer.ValueType, substitution)
            );

        return new ObjectType(newIndexer, newProperties);
    }

    private Type SubstituteIndexedType(Node failNode, TypeParameterSubstitution substitution, IndexedType indexedType, Dictionary<Type, Type> cache)
    {
        var target = SubstituteTypeParameters(failNode, indexedType.Target, substitution, cache);
        var index = SubstituteTypeParameters(failNode, indexedType.Index, substitution, cache);

        // A substitution that lands on another type parameter has not resolved the index, only renamed it.
        // Resolving it anyway would pick one of the mapping's value types and drop its correspondence with
        // the key, which is the whole of what T[K] says - and 'Serializer<MessageData[K]>' would then not
        // accept what 'serializer_of::<MessageData, K>' hands back.
        if (target is TypeParameter || index is TypeParameter)
            return new IndexedType(target, index);

        return GetTypeAtIndex(failNode, target, index);
    }

    /// <summary>
    ///     Resolves a deferred <c>keyof(T)</c> once <c>T</c> is known, leaving it deferred where the
    ///     substitution only renamed the parameter.
    /// </summary>
    private Type SubstituteKeyOfType(Node failNode, TypeParameterSubstitution substitution, KeyOfType keyOfType, Dictionary<Type, Type> cache)
    {
        var target = SubstituteTypeParameters(failNode, keyOfType.Target, substitution, cache);
        var substituted = new KeyOfType(target);
        if (TypeSimplifier.ResolveKeys(substituted) is { } keys)
            return keys;

        if (target is TypeParameter or TypeVariable or IndexedType or KeyOfType)
            return substituted;

        _diagnostics.Error(failNode, InternalCodes.InvalidKeyOf, $"Cannot access keys of type '{target.Widen()}'.");
        return PrimitiveType.Never;
    }

    private List<Type> SubstituteTypeParameters(Node failNode, List<Type> types, TypeParameterSubstitution substitution) =>
        types.ConvertAll(t => SubstituteTypeParameters(failNode, t, substitution));

    private Type SubstituteTypeParameters(Node failNode, Type type, TypeParameterSubstitution substitution) =>
        SubstituteTypeParameters(
            failNode,
            type,
            substitution,
            new Dictionary<Type, Type>()
        );

    private Type SubstituteTypeParameters(Node failNode, Type type, TypeParameterSubstitution substitution, Dictionary<Type, Type> cache)
    {
        if (cache.TryGetValue(type, out var cached))
            return cached;

        cache[type] = type;
        var substitutedType = TrySubstituteTypeParameter(type, substitution, out var substituted)
            ? substituted
            : type is IndexedType indexedType
                ? SubstituteIndexedType(failNode, substitution, indexedType, cache)
                : type is KeyOfType keyOfType
                ? SubstituteKeyOfType(failNode, substitution, keyOfType, cache)
                // Deferred the same way, but with nothing about them to report where the substitution does
                // not resolve them - so they go through the shared substituter rather than a copy here.
                : type is ConditionalType or MappedType
                ? TypeSubstitution.Apply(type, substitution)
                : TypeSolver.Transform(
                    type,
                    t => t switch
                    {
                        _ when TrySubstituteTypeParameter(t, substitution, out var substituted2) => substituted2,
                        _ => SubstituteTypeParameters(failNode, t, substitution, cache)
                    }
                );

        cache[type] = substitutedType;
        return substitutedType;
    }

    private static bool TrySubstituteTypeParameter(Type type, TypeParameterSubstitution substitution, [MaybeNullWhen(false)] out Type substituted)
    {
        substituted = null;
        return type is TypeParameter tp && substitution.TryGetValue(tp, out substituted);
    }

    /// <summary>
    ///     A generic-valued argument (e.g. passing `id` where `id&lt;T&gt;(n: T): T`) is otherwise
    ///     compared structurally against its expected type with no attempt to specialize it first,
    ///     so a type-parameter-count mismatch (the argument has its own free type parameters, the
    ///     expected shape has none) fails immediately even when the expected shape fully determines
    ///     what the argument's type parameters should be. Infer and substitute them here so `id`
    ///     becomes e.g. `fn(number): number` before the normal assignability/unification check runs.
    /// </summary>
    private bool TryInstantiateGenericFunctionArgument(Node failNode, Type actual, Type expected, [NotNullWhen(true)] out Type? instantiated)
    {
        instantiated = null;
        if (actual is not FunctionType { TypeParameters.Count: > 0 } genericFunction
            || expected is not FunctionType expectedFunction
            || genericFunction.TypeParameters.Count == expectedFunction.TypeParameters.Count)
            return false;

        var substitution = TypeInferrer.InferFunctionTypeArguments(genericFunction, expectedFunction.ParameterTypes);
        foreach (var typeParameter in genericFunction.TypeParameters)
            if (substitution.TryGetValue(typeParameter, out var substitutedType)
                && typeParameter.Constraint != null
                && !substitutedType.IsAssignableTo(typeParameter.Constraint))
                return false;

        var substitutedParameterTypes = SubstituteTypeParameters(failNode, genericFunction.ParameterTypes, substitution);
        var substitutedReturnType = SubstituteTypeParameters(failNode, genericFunction.ReturnType, substitution);
        instantiated = new FunctionType([], substitutedParameterTypes, substitutedReturnType, genericFunction.HasRestParameter, genericFunction.IsAsync);
        return true;
    }
}