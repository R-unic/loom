using System.Diagnostics.CodeAnalysis;

namespace Loom.Core.TypeChecking.Types;

public abstract record ObjectBodyType(bool IsMutable, Type ValueType);

public sealed record ObjectIndexer(bool IsMutable, Type KeyType, Type ValueType)
    : ObjectBodyType(IsMutable, ValueType);

public sealed record ObjectProperty(bool IsMutable, string Name, Type ValueType, bool IsStatic = false)
    : ObjectBodyType(IsMutable, ValueType);

public class ObjectType(ObjectIndexer? indexer, List<ObjectProperty> properties) : NativelyIndexableType
{
    public static readonly ObjectType Empty = new(null, []);
    private int _cachedHash;
    private int _hashVersion = -1;

    private int _propertyMapVersion = -1;

    public override ObjectIndexer? Indexer { get; internal set; } = indexer;
    public override List<ObjectProperty> Properties { get; } = properties;

    /// <summary>
    ///     Bumped whenever <see cref="Properties" /> is mutated via <see cref="AddProperties" />, since
    ///     <see cref="Properties" /> is populated incrementally during interface/trait resolution rather
    ///     than fully at construction time. Cached derived structures (property map, hash) are keyed on
    ///     this so they never observe a stale, partially-populated property list.
    /// </summary>
    public int Version { get; private set; } = properties.Count;

    private Dictionary<string, ObjectProperty> PropertyMap
    {
        get
        {
            if (field != null && _propertyMapVersion == Version)
                return field;

            field = new Dictionary<string, ObjectProperty>(Properties.Count);
            foreach (var property in Properties)
                field[property.Name] = property;

            _propertyMapVersion = Version;
            return field;
        }
    }

    public void AddProperties(IEnumerable<ObjectProperty> newProperties)
    {
        Properties.AddRange(newProperties);
        Version = Properties.Count;
    }

    protected override ObjectProperty? FindProperty(string name) => PropertyMap.GetValueOrDefault(name);

    /// <summary>
    ///     Whether <paramref name="source" /> may stand in for <paramref name="target" />, by the rule
    ///     <see cref="ArrayType.IsAssignableTo" /> already applies to an array's elements - an array being an
    ///     object with an indexer, the two had no business disagreeing.
    ///     <para>
    ///         <c>mut</c> is a capability, so giving one up is always safe: a mutable member satisfies an
    ///         immutable one, and because the target can then only be read through, its type is covariant.
    ///         Gaining one is not: an immutable member cannot satisfy a mutable one, and a mutable target is
    ///         invariant, since anything written through it is also visible to the source's own type.
    ///     </para>
    ///     <para>
    ///         Loom cannot promise that an immutably-typed value never changes - it does not track who else
    ///         holds a mutable alias - so reading <c>mut</c> as a guarantee rather than a capability would cost
    ///         every widening here and buy nothing.
    ///     </para>
    /// </summary>
    private static bool IsMemberAssignable(ObjectBodyType source, ObjectBodyType target) =>
        target.IsMutable
            ? source.IsMutable && source.ValueType.Equals(target.ValueType)
            : source.ValueType.IsAssignableTo(target.ValueType);

    public override Type PropertyKeyUnion() => TypeSimplifier.Simplify(new UnionType(Properties.ConvertAll(Type (p) => new LiteralType(p.Name))));

    [SuppressMessage("ReSharper", "NonReadonlyMemberInGetHashCode")]
    public override int GetHashCode()
    {
        if (_hashVersion == Version)
            return _cachedHash;

        var hash = new HashCode();
        hash.Add(Properties.Count);
        foreach (var property in Properties.OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            hash.Add(property.Name);
            hash.Add(property.IsMutable);
        }

        _cachedHash = hash.ToHashCode();
        _hashVersion = Version;
        return _cachedHash;
    }

    public override bool Equals(Type? other) =>
        GuardedEquals(
            this,
            other,
            () =>
            {
                if (other is not ObjectType objectType)
                    return false;

                if (Properties.Count != objectType.Properties.Count)
                    return false;

                var otherProps = objectType.PropertyMap;
                foreach (var prop in Properties)
                {
                    if (!otherProps.TryGetValue(prop.Name, out var otherProp))
                        return false;

                    if (prop.IsMutable != otherProp.IsMutable)
                        return false;

                    if (!prop.ValueType.Equals(otherProp.ValueType))
                        return false;
                }

                if (Indexer == null)
                    return objectType.Indexer == null;

                if (objectType.Indexer == null)
                    return false;

                return Indexer.KeyType.Equals(objectType.Indexer.KeyType)
                    && Indexer.ValueType.Equals(objectType.Indexer.ValueType)
                    && Indexer.IsMutable == objectType.Indexer.IsMutable;
            }
        );

    public override bool IsAssignableTo(Type other) =>
        GuardedAssignableTo(
            this,
            other,
            () =>
            {
                if (base.IsAssignableTo(other))
                    return true;

                if (other is not ObjectType objectType)
                    return false;

                if (Properties.Count < objectType.Properties.Count)
                    return false;

                var sourcePropertyMap = PropertyMap;
                foreach (var targetProperty in objectType.Properties)
                {
                    if (!sourcePropertyMap.TryGetValue(targetProperty.Name, out var sourceProperty))
                        return false;

                    if (!IsMemberAssignable(sourceProperty, targetProperty))
                        return false;
                }

                if (objectType.Indexer == null)
                    return true;

                if (Indexer == null)
                    return false;

                var keyOk = objectType.Indexer.IsMutable
                    ? Indexer.KeyType.Equals(objectType.Indexer.KeyType)
                    : Indexer.KeyType.IsAssignableTo(objectType.Indexer.KeyType);

                return keyOk && IsMemberAssignable(Indexer, objectType.Indexer);
            }
        );

    public override string ToString()
    {
        if (Indexer == null && Properties.Count == 0)
            return "object";

        var properties = string.Join(", ", Properties.Select(p => $"{(p.IsMutable ? "mut " : "")}{p.Name}: {p.ValueType}"));
        var indexer = Indexer != null
            ? $"{(Indexer.IsMutable ? "mut " : "")}[{Indexer.KeyType}]: {Indexer.ValueType}"
            : "";

        return $"{{ {indexer}{(Indexer != null && properties.Length > 0 ? ", " : "")}{properties} }}";
    }
}