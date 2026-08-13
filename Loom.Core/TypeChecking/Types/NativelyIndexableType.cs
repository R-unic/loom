namespace Loom.Core.TypeChecking.Types;

public abstract class NativelyIndexableType : Type
{
    public abstract ObjectIndexer? Indexer { get; internal set; }
    public abstract List<ObjectProperty> Properties { get; }

    /// <summary>
    ///     Every indexer this type carries, not just the first. A plain <see cref="ObjectType" /> only
    ///     ever has the one, but an <see cref="InterfaceType" /> merged from several single-key constraints
    ///     - <c>declare interface MessageData: ShootGunEntry, ReloadEntry;</c>, each contributing its own
    ///     <c>[Message["..."]]: ...Packet</c> - carries one per constraint, and <see cref="Indexer" />'s
    ///     single slot can only ever expose one of them.
    /// </summary>
    public virtual IEnumerable<ObjectIndexer> Indexers => Indexer is { } indexer ? [indexer] : [];

    public abstract Type PropertyKeyUnion();
    public Type PropertyUnion() => TypeSimplifier.Simplify(new UnionType(Properties.ConvertAll(p => p.ValueType)));

    /// <summary>Every key this type accepts: its property names, plus each indexer's key type.</summary>
    /// <remarks>
    ///     Over <see cref="Indexers" /> rather than <see cref="Indexer" />, so an interface merged from
    ///     several single-key constraints answers with all of their keys and not just the first one's -
    ///     the difference between <c>keyof(MessageData)</c> naming every message and naming one of them.
    /// </remarks>
    public Type KeyUnion() => UnionOfMembers(PropertyKeyUnion(), Indexers.Select(indexer => indexer.KeyType));

    /// <summary>Every value this type holds: its property types, plus each indexer's value type.</summary>
    /// <inheritdoc cref="KeyUnion" path="/remarks" />
    public Type ValueUnion() => UnionOfMembers(PropertyUnion(), Indexers.Select(indexer => indexer.ValueType));

    private static Type UnionOfMembers(Type propertyType, IEnumerable<Type> indexerTypes)
    {
        List<Type> types = [..indexerTypes];
        if (types.Count == 0)
            return propertyType;

        if (propertyType is UnionType propertyUnion)
            types.AddRange(propertyUnion.Types);
        else
            types.Add(propertyType);

        return TypeSimplifier.Simplify(new UnionType(types));
    }

    public ObjectProperty? GetProperty(string name) => FindProperty(name);

    protected virtual ObjectProperty? FindProperty(string name) => Properties.Find(p => p.Name == name);

    public (ObjectBodyType? BodyType, string CannotFindReason) GetTypeAtIndex(Type indexType)
    {
        if (indexType is TypeParameter parameter)
        {
            if (parameter.Constraint == null)
            {
                var unconstrained = Indexers.FirstOrDefault(i => indexType.IsAssignableTo(i.KeyType));
                return unconstrained != null
                    ? (unconstrained, "")
                    : (null, $" Type parameter '{parameter.Name}' is unconstrained.");
            }

            indexType = parameter.Constraint;
        }

        if (indexType is LiteralType { Value: string name })
        {
            var property = GetProperty(name);
            if (property != null)
                return (property, "");

            if (!Indexers.Any())
                return (null, $" Property '{name}' does not exist on type '{this}'.");
        }

        if (Properties.Count > 0 && indexType.Equals(PropertyKeyUnion()))
            return (new ObjectIndexer(
                Properties.Any(p => p.IsMutable),
                indexType,
                ValueUnionForKeyType(indexType)
            ), "");

        // An exact key match - the shape a specific enum-member literal takes indexing into a mapping
        // interface merged from several single-key constraints - wins over a looser assignability check,
        // so two indexers with distinct literal keys each resolve to their own value type rather than
        // whichever one happens to be reachable first.
        if (Indexers.FirstOrDefault(i => indexType.Equals(i.KeyType)) is { } exact)
            return (exact, "");

        if (Indexers.FirstOrDefault(i => indexType.IsAssignableTo(i.KeyType)) is { } assignable)
            return (assignable, "");

        // A union of keys - what KeyUnion answers over an interface merged from single-key constraints -
        // reaches every member it unions, so the value type is every one of theirs. The checks above are
        // each looking for one member that covers the whole index, and no single-key indexer ever does.
        if (indexType is UnionType indexUnion)
        {
            var reached = indexUnion.Types.ConvertAll(member => GetTypeAtIndex(member).BodyType);
            if (reached.TrueForAll(body => body != null))
                return (new ObjectIndexer(
                    reached.Exists(body => body!.IsMutable),
                    indexType,
                    TypeSimplifier.Simplify(new UnionType(reached.ConvertAll(body => body!.ValueType)))
                ), "");
        }

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

        matches.AddRange(Indexers.Select(i => i.ValueType));

        return TypeSimplifier.Simplify(new UnionType(matches));
    }
}