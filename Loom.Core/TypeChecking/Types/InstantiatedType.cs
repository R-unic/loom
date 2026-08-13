namespace Loom.Core.TypeChecking.Types;

/// <summary>
///     A generic definition applied to type arguments. Never constructed directly - <see cref="GenericType.Construct" />
///     hands back the one instance per (definition, arguments) pair, which is what stops a self-referential
///     generic from expanding forever.
/// </summary>
public sealed class InstantiatedType : Type
{
    private Type? _instantiatedBase;

    internal InstantiatedType(GenericType genericType, List<Type> arguments)
    {
        GenericType = genericType;
        Arguments = arguments;
    }

    public GenericType GenericType { get; }
    public List<Type> Arguments { get; }

    public override bool Equals(Type? other) =>
        GuardedEquals(
            this,
            other,
            () => other is InstantiatedType instantiated
                && GenericType.Equals(instantiated.GenericType)
                && ListEquals(Arguments, instantiated.Arguments)
        );

    public override bool IsAssignableTo(Type other) => ReferenceEquals(this, other) || Expand().IsAssignableTo(other);
    public override int GetHashCode() => HashCode.Combine(GenericType.GetHashCode(), Arguments.Count, GetTypeListHash(Arguments));
    public override string ToString() => GenericType.Declaration.Name.Text + "<" + string.Join(", ", Arguments.ConvertAll(p => p.ToString())) + ">";

    public Type Expand()
    {
        if (_instantiatedBase != null)
            return _instantiatedBase;

        var substitution = new TypeParameterSubstitution();
        for (var i = 0; i < GenericType.Parameters.Count; i++)
        {
            var parameter = GenericType.Parameters[i];
            substitution[parameter] = Arguments.ElementAtOrDefault(i) ?? parameter.DefaultType!;
        }

        var baseType = GenericType.UnderlyingType;
        _instantiatedBase = SubstituteTypeParameters(baseType, substitution);

        return _instantiatedBase;
    }

    // FunctionType needs its own case (unlike every other composite type) because a nested function's own
    // type parameters must be filtered out of its declaration once substitution binds them, not merely have
    // their usages replaced - TypeSolver.Transform's generic per-child recursion has no way to know that.
    private static Type SubstituteTypeParameters(Type type, TypeParameterSubstitution substitution) =>
        type switch
        {
            TypeParameter typeParameter when substitution.TryGetValue(typeParameter, out var substituted) => substituted,
            IndexedType indexedType => SubstituteIndexedType(indexedType, substitution),
            KeyOfType keyOfType => SubstituteKeyOfType(keyOfType, substitution),
            FunctionType functionType => SubstituteFunctionType(functionType, substitution),
            _ => TypeSolver.Transform(type, t => SubstituteTypeParameters(t, substitution), simplify: false)
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
            functionType.ParameterTypes.ConvertAll(p => SubstituteTypeParameters(p, substitution)),
            SubstituteTypeParameters(functionType.ReturnType, substitution),
            functionType.HasRestParameter
        );
    }

    /// <summary>The substitution key that binds <paramref name="parameter" />, or null where none does.</summary>
    private static TypeParameter? Binder(TypeParameterSubstitution substitution, TypeParameter parameter) =>
        substitution.Keys.FirstOrDefault(key => key.Equals(parameter));

    private static Type SubstituteKeyOfType(KeyOfType keyOfType, TypeParameterSubstitution substitution)
    {
        var target = SubstituteTypeParameters(keyOfType.Target, substitution);
        var substituted = ReferenceEquals(target, keyOfType.Target) ? keyOfType : new KeyOfType(target);
        return TypeSimplifier.ResolveKeys(substituted) ?? substituted;
    }

    private static Type SubstituteIndexedType(IndexedType indexedType, TypeParameterSubstitution substitution)
    {
        var target = SubstituteTypeParameters(indexedType.Target, substitution);
        var index = SubstituteTypeParameters(indexedType.Index, substitution);
        if (TypeSimplifier.ResolveIndex(target, index) is { } resolved)
            return resolved;

        return ReferenceEquals(target, indexedType.Target) && ReferenceEquals(index, indexedType.Index)
            ? indexedType
            : new IndexedType(target, index);
    }
}