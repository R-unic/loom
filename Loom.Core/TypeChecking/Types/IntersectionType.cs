namespace Loom.Core.TypeChecking.Types;

public sealed class IntersectionType(List<Type> types) : Type
{
    public List<Type> Types { get; } = types;

    public override bool Equals(Type? other) => other is IntersectionType intersection && Types.All(intersection.Types.Contains);
    public override int GetHashCode() => HashCode.Combine(typeof(IntersectionType), Types.Count, GetTypeListHash(Types));
    public override bool IsAssignableTo(Type other) => base.IsAssignableTo(other) || Types.Exists(t => t.IsAssignableTo(other));
    public override string ToString() => string.Join(" & ", Types.ConvertAll(ParenthesizeIfNeeded));
}