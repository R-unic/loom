namespace Loom.Core.TypeChecking.Types;

public abstract class NativelyIndexableType : Type
{
    public abstract ObjectIndexer? Indexer { get; internal set; }
    public abstract List<ObjectProperty> Properties { get; }

    public abstract Type PropertyKeyUnion();

    public ObjectProperty? GetProperty(string name) => FindProperty(name);

    protected virtual ObjectProperty? FindProperty(string name) => Properties.Find(p => p.Name == name);

    public (ObjectBodyType? BodyType, string CannotFindReason) GetTypeAtIndex(Type indexType)
    {
        if (indexType is TypeParameter parameter)
        {
            if (parameter.Constraint == null)
                return Indexer != null && indexType.IsAssignableTo(Indexer.KeyType)
                    ? (Indexer, "")
                    : (null, $" Type parameter '{parameter.Name}' is unconstrained.");

            indexType = parameter.Constraint;
        }

        if (indexType is LiteralType { Value: string name })
        {
            var property = GetProperty(name);
            if (property != null)
                return (property, "");

            if (Indexer == null)
                return (null, $" Property '{name}' does not exist on type '{this}'.");
        }

        if (Properties.Count > 0 && indexType.Equals(PropertyKeyUnion()))
            return (new ObjectIndexer(
                Properties.Any(p => p.IsMutable),
                indexType,
                ValueUnionForKeyType(indexType)
            ), "");

        if (Indexer != null && indexType.IsAssignableTo(Indexer.KeyType))
            return (Indexer, "");

        return Indexer == null
            ? (null, "")
            : (null, $" Index is not of type '{Indexer.KeyType}'.");
    }

    private Type ValueUnionForKeyType(Type keyType)
    {
        var matches = (
                from property in Properties
                let literal = new LiteralType(property.Name)
                where literal.IsAssignableTo(keyType)
                select property.ValueType
            )
            .ToList();

        if (Indexer != null)
            matches.Add(Indexer.ValueType);

        return TypeSimplifier.Simplify(new UnionType(matches));
    }
}