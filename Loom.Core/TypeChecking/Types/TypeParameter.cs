namespace Loom.Core.TypeChecking.Types;

public sealed class TypeParameter(string name, Type? constraint = null, Type? defaultType = null, Variance variance = Variance.Invariant) : Type
{
    public string Name { get; } = name;
    public Type? Constraint { get; } = constraint;
    public Type? DefaultType { get; } = defaultType;
    public Variance Variance { get; } = variance;

    public TypeParameter WithVariance(Variance newVariance) => new(Name, Constraint, DefaultType, newVariance);

    public override bool IsAssignableTo(Type other) => base.IsAssignableTo(other) || other is TypeParameter parameter && Name == parameter.Name && Equals(parameter);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        if (Constraint != null)
            hash.Add(Constraint.GetHashCode());

        if (DefaultType != null)
            hash.Add(DefaultType.GetHashCode());

        return hash.ToHashCode();
    }

    /// <summary>
    ///     Deliberately name-blind: two type parameters of the same shape are the same type whatever their
    ///     binders called them, which is what makes <c>fn&lt;T&gt;(T): T</c> and <c>fn&lt;U&gt;(U): U</c> the
    ///     same function type. <see cref="GenericType.Construct" /> is the one place that must not use this,
    ///     and does not - it matches arguments by reference.
    /// </summary>
    public override bool Equals(Type? other) =>
        other is TypeParameter parameter
        && Equals(Constraint, parameter.Constraint)
        && Equals(DefaultType, parameter.DefaultType);

    public override string ToString() => Name + (Constraint != null ? ": " + Constraint : "") + (DefaultType != null ? " = " + DefaultType : "");
}