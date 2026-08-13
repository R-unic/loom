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
        _instantiatedBase = TypeSubstitution.Apply(baseType, substitution);

        return _instantiatedBase;
    }
}