namespace Loom.Core.TypeChecking.Types;

public sealed class TypeParameter(string name, Type? constraint = null, Type? defaultType = null, Variance variance = Variance.Invariant) : Type
{
    public string Name { get; } = name;
    public Type? Constraint { get; } = constraint;
    public Type? DefaultType { get; } = defaultType;
    public Variance Variance { get; } = variance;

    public TypeParameter WithVariance(Variance newVariance) => new(Name, Constraint, DefaultType, newVariance);

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

    public override bool Equals(Type? other) =>
        other is TypeParameter parameter
        && Equals(Constraint, parameter.Constraint)
        && Equals(DefaultType, parameter.DefaultType);

    public override string ToString() => Name + (Constraint != null ? ": " + Constraint : "") + (DefaultType != null ? " = " + DefaultType : "");
}