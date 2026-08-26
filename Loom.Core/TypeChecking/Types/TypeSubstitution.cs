using Loom.Core.TypeChecking.Solving;

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
    ///     and since <see cref="TypeParameter.Equals(Type?)" /> is name-blind they are indistinguishable to the
    ///     substitution. Binding the inner one to whatever <c>Box</c> was instantiated with typed
    ///     <c>b.map::&lt;string&gt;("hi")</c> as <c>number</c> - the call's own argument had nothing left to
    ///     bind. A function that merely <em>uses</em> the enclosing parameter does not declare it, so it is
    ///     not in this list and is substituted as usual.
    /// </remarks>
    private static FunctionType SubstituteFunctionType(FunctionType functionType, TypeParameterSubstitution substitution)
    {
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
        var source = Apply(mappedType.Source, substitution);
        var valueType = Apply(mappedType.ValueType, substitution);
        var substituted = ReferenceEquals(source, mappedType.Source) && ReferenceEquals(valueType, mappedType.ValueType)
            ? mappedType
            : new MappedType(mappedType.Binder, source, valueType, mappedType.IsMutable);

        return substituted.Resolve() ?? substituted;
    }

    internal static ConditionalType SubstituteArms(ConditionalType conditionalType, TypeParameterSubstitution substitution)
    {
        var subject = Apply(conditionalType.Subject, substitution);
        var changed = !ReferenceEquals(subject, conditionalType.Subject);
        var arms = conditionalType.Arms.ConvertAll(arm =>
            {
                var (binders, armSubstitution) = SubstituteBinders(arm.Binders, substitution);
                var pattern = Apply(arm.Pattern, armSubstitution);
                var result = Apply(arm.Result, armSubstitution);
                changed |= !ReferenceEquals(pattern, arm.Pattern) || !ReferenceEquals(result, arm.Result) || !ReferenceEquals(binders, arm.Binders);
                return new ConditionalArm(pattern, result, binders);
            }
        );

        return changed ? new ConditionalType(subject, arms, conditionalType.Distributes) : conditionalType;
    }

    /// <summary>
    ///     A binder's own constraint can name the enclosing generic's parameter - <c>let Kept: U</c> - and
    ///     <see cref="TypeSolver.Transform" /> leaves a bare <see cref="TypeParameter" /> alone, so that
    ///     constraint would otherwise never see this substitution. A binder is identified by reference
    ///     everywhere else, though (see <see cref="ConditionalArm.Binders" />), so a binder whose constraint
    ///     actually changes gets rebuilt as a new instance, and every occurrence of the old one - in the
    ///     pattern and in the result - is remapped to it by extending the substitution with that one pair.
    /// </summary>
    private static (IReadOnlyList<TypeParameter> Binders, TypeParameterSubstitution Substitution) SubstituteBinders(
        IReadOnlyList<TypeParameter> binders, TypeParameterSubstitution substitution)
    {
        List<TypeParameter>? rebuilt = null;
        TypeParameterSubstitution? extended = null;

        for (var i = 0; i < binders.Count; i++)
        {
            var binder = binders[i];
            var constraint = binder.Constraint == null ? null : Apply(binder.Constraint, substitution);
            if (ReferenceEquals(constraint, binder.Constraint))
            {
                rebuilt?.Add(binder);
                continue;
            }

            if (rebuilt == null)
            {
                rebuilt = [.. binders.Take(i)];
                extended = new TypeParameterSubstitution(substitution);
            }

            var replacement = new TypeParameter(binder.Name, constraint, binder.DefaultType, binder.Variance);
            rebuilt.Add(replacement);
            extended![binder] = replacement;
        }

        return rebuilt == null ? (binders, substitution) : (rebuilt, extended!);
    }

    /// <remarks>
    ///     Evaluated even where the substitution changed nothing: a conditional written over a concrete
    ///     subject is answerable the moment it exists, and the substitution it happens to sit inside is not
    ///     what makes it so.
    /// </remarks>
    private static Type SubstituteConditionalType(ConditionalType conditionalType, TypeParameterSubstitution substitution)
    {
        var substituted = SubstituteArms(conditionalType, substitution);
        return ConditionalTypeEvaluator.TryEvaluate(substituted) ?? substituted;
    }
}
