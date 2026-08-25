using Loom.Core.TypeChecking.Solving;

namespace Loom.Core.TypeChecking.Types;

public sealed class TupleType(List<Type> elementTypes) : NativelyIndexableType
{
    public List<Type> ElementTypes { get; } = elementTypes;
    public override ObjectIndexer? Indexer { get; internal set; } = new(false, PrimitiveType.Number, TypeSimplifier.Simplify(new UnionType(elementTypes)));
    public override List<ObjectProperty> Properties { get; } = [new(false, "length", new LiteralType((long)elementTypes.Count))];

    public override Type PropertyKeyUnion() => new LiteralType("length");

    public override int GetHashCode() => HashCode.Combine(typeof(TupleType), GetTypeListHash(ElementTypes));

    public override bool Equals(Type? other) => GuardedEquals(this, other, () => other is TupleType tuple && ListEquals(ElementTypes, tuple.ElementTypes));

    public override bool IsAssignableTo(Type other) =>
        GuardedAssignableTo(
            this,
            other,
            () =>
            {
                if (base.IsAssignableTo(other))
                    return true;

                return other switch
                {
                    TupleMarkerType => true,
                    TupleType tuple =>
                        ElementTypes.Count == tuple.ElementTypes.Count
                        && ElementTypes.Zip(tuple.ElementTypes).All(pair => pair.First.IsAssignableTo(pair.Second)),
                    ArrayType { IsMutable: false } array => ElementTypes.TrueForAll(elementType => elementType.IsAssignableTo(array.ElementType)),
                    _ => false
                };
            }
        );

    public override string ToString() => $"({string.Join(", ", ElementTypes)})";
}
