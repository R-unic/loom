namespace Loom.Core.TypeChecking.Types;

public sealed class OptionalType(Type nonNullableType)
    : UnionType([nonNullableType, PrimitiveType.None])
{
    public Type NonNullableType { get; } = nonNullableType;

    public override bool Equals(Type? other)
    {
        if (ReferenceEquals(this, other)) return true;
        return other is OptionalType optional && NonNullableType.Equals(optional.NonNullableType);
    }

    public override int GetHashCode() => HashCode.Combine(typeof(OptionalType), NonNullableType.GetHashCode());

    public override string ToString() => ParenthesizeIfNeeded(NonNullableType) + "?";
}