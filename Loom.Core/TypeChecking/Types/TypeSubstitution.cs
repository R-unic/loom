namespace Loom.Core.TypeChecking.Types;

/// <summary>
///     Replaces type parameters with whatever they were bound to, resolving each deferred operator the
///     replacement makes answerable - a <c>keyof(T)</c> whose T has arrived, an index whose key is now a
///     literal, a conditional whose subject is now concrete.
/// </summary>
/// <remarks>
///     Shared by <see cref="InstantiatedType.Expand" />, <see cref="MappedType.Resolve" /> and
///     <see cref="ConditionalTypeEvaluator" /> so binding a generic's argument, a mapped type's key and a
///     pattern's <c>let</c> all mean the same thing. The type checker keeps its own copy
///     (<c>TypeChecker.Generics.cs</c>) because it reports where an unresolvable <c>keyof</c> was written,
///     which needs a node this has no access to.
/// </remarks>
internal static class TypeSubstitution
{
    // FunctionType needs its own case (unlike every other composite type) because a nested function's own
    // type parameters must be filtered out of its declaration once substitution binds them, not merely have
    // their usages replaced - TypeSolver.Transform's generic per-child recursion has no way to know that.
    public static Type Apply(Type type, TypeParameterSubstitution substitution) =>
        type switch
        {
            TypeParameter typeParameter when substitution.TryGetValue(typeParameter, out var substituted) => substituted,
            IndexedType indexedType => SubstituteIndexedType(indexedType, substitution),
            KeyOfType keyOfType => SubstituteKeyOfType(keyOfType, substitution),
            FunctionType functionType => SubstituteFunctionType(functionType, substitution),
            MappedType mappedType => SubstituteMappedType(mappedType, substitution),
            ConditionalType conditionalType => SubstituteConditionalType(conditionalType, substitution),
            _ => TypeSolver.Transform(type, t => Apply(t, substitution), simplify: false)
        };

    /// <summary>
    ///     Substitutes into a function type, leaving the parameters it declares for itself alone.
    /// </summary>
    /// <remarks>
    ///     A function that declares a parameter of its own shadows the enclosing generic's: in
    ///     <c>interface Box&lt;T&gt; { map: fn&lt;T&gt;(other: T): T }</c> the two Ts are separate bindings,
    ///     and since <see cref="TypeParameter.Equals" /> is name-blind they are indistinguishable to the
    ///     substitution. Binding the inner one to whatever <c>Box</c> was instantiated with typed
    ///     <c>b.map::&lt;string&gt;("hi")</c> as <c>number</c> - the call's own argument had nothing left to
    ///     bind. A function that merely <em>uses</em> the enclosing parameter does not declare it, so it is
    ///     not in this list and is substituted as usual.
    /// </remarks>
    private static Type SubstituteFunctionType(FunctionType functionType, TypeParameterSubstitution substitution)
    {
        // Which of the two a parameter is comes down to identity: the resolver hands the enclosing generic's
        // own parameter to a function that uses it, and a fresh one to a function that rebinds it. Structural
        // equality cannot tell them apart, being name-blind, so the instance is the only thing that can.
        var shadowed = functionType.TypeParameters.FindAll(parameter => Binder(substitution, parameter) is { } key && !ReferenceEquals(key, parameter));
        if (shadowed.Count > 0)
        {
            substitution = new TypeParameterSubstitution(substitution);
            foreach (var parameter in shadowed)
                substitution.Remove(parameter);
        }

        return new FunctionType(
            functionType.TypeParameters.FindAll(parameter => shadowed.Contains(parameter) || !substitution.ContainsKey(parameter)),
            functionType.ParameterTypes.ConvertAll(p => Apply(p, substitution)),
            Apply(functionType.ReturnType, substitution),
            functionType.HasRestParameter,
            functionType.IsAsync
        );
    }

    /// <summary>The substitution key that binds <paramref name="parameter" />, or null where none does.</summary>
    private static TypeParameter? Binder(TypeParameterSubstitution substitution, TypeParameter parameter) =>
        substitution.Keys.FirstOrDefault(key => key.Equals(parameter));

    private static Type SubstituteKeyOfType(KeyOfType keyOfType, TypeParameterSubstitution substitution)
    {
        var target = Apply(keyOfType.Target, substitution);
        var substituted = ReferenceEquals(target, keyOfType.Target) ? keyOfType : new KeyOfType(target);
        return TypeSimplifier.ResolveKeys(substituted) ?? substituted;
    }

    private static Type SubstituteIndexedType(IndexedType indexedType, TypeParameterSubstitution substitution)
    {
        var target = Apply(indexedType.Target, substitution);
        var index = Apply(indexedType.Index, substitution);
        if (TypeSimplifier.ResolveIndex(target, index) is { } resolved)
            return resolved;

        return ReferenceEquals(target, indexedType.Target) && ReferenceEquals(index, indexedType.Index)
            ? indexedType
            : new IndexedType(target, index);
    }

    /// <summary>
    ///     The binder is left out of the substitution deliberately: it is bound by the mapped type itself,
    ///     one key at a time, and only <see cref="MappedType.Resolve" /> knows what to bind it to.
    /// </summary>
    private static Type SubstituteMappedType(MappedType mappedType, TypeParameterSubstitution substitution)
    {
        var substituted = new MappedType(
            mappedType.Binder,
            Apply(mappedType.Source, substitution),
            Apply(mappedType.ValueType, substitution),
            mappedType.IsMutable
        );

        return substituted.Resolve() ?? substituted;
    }

    internal static ConditionalType SubstituteArms(ConditionalType conditionalType, TypeParameterSubstitution substitution) =>
        new(
            Apply(conditionalType.Subject, substitution),
            conditionalType.Arms.ConvertAll(arm => new ConditionalArm(Apply(arm.Pattern, substitution), Apply(arm.Result, substitution), arm.Binders)),
            conditionalType.Distributes
        );

    private static Type SubstituteConditionalType(ConditionalType conditionalType, TypeParameterSubstitution substitution)
    {
        var substituted = SubstituteArms(conditionalType, substitution);
        return ConditionalTypeEvaluator.TryEvaluate(substituted) ?? substituted;
    }
}
