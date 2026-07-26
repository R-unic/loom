namespace Loom.Core.TypeChecking.Types;

public sealed class InstantiatedType(GenericType genericType, List<Type> arguments) : Type
{
    private Type? _instantiatedBase;
    public GenericType GenericType { get; } = genericType;
    public List<Type> Arguments { get; } = arguments;

    public override bool Equals(Type? other) =>
        GuardedEquals(
            this,
            other,
            () => other is InstantiatedType instantiated
                && GenericType.Equals(instantiated.GenericType)
                && ListEquals(Arguments, instantiated.Arguments)
        );

    public override bool IsAssignableTo(Type other) => Expand().IsAssignableTo(other);
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
    private Type SubstituteTypeParameters(Type type, TypeParameterSubstitution substitution) =>
        type switch
        {
            TypeParameter typeParameter when substitution.TryGetValue(typeParameter, out var substituted) => substituted,
            FunctionType functionType => new FunctionType(
                functionType.TypeParameters.FindAll(tp => !substitution.ContainsKey(tp)),
                functionType.ParameterTypes.ConvertAll(p => SubstituteTypeParameters(p, substitution)),
                SubstituteTypeParameters(functionType.ReturnType, substitution),
                functionType.HasRestParameter
            ),
            _ => TypeSolver.Transform(type, t => SubstituteTypeParameters(t, substitution), simplify: false)
        };
}