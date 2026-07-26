namespace Loom.Core.TypeChecking.Types;

public class UnionType(List<Type> types) : Type
{
    public List<Type> Types { get; } = types;

    public override Type Widen() => TypeSimplifier.Simplify(new UnionType(Types.ConvertAll(t => t.Widen())));
    public override bool Equals(Type? other) => other is UnionType union && Types.All(union.Types.Contains);
    public override int GetHashCode() => HashCode.Combine(typeof(UnionType), Types.Count, GetTypeListHash(Types));

    public override bool IsAssignableTo(Type other) =>
        base.IsAssignableTo(other)
        || other is UnionType && Types.Exists(t => t.IsAssignableTo(other))
        || other is not UnionType && Types.TrueForAll(t => t.IsAssignableTo(other));

    public override string ToString() => string.Join(" | ", Types.ConvertAll(ParenthesizeIfNeeded));
}