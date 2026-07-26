using System.Diagnostics.CodeAnalysis;

namespace Loom.Core.TypeChecking.Types;

public abstract record ObjectBodyType(bool IsMutable, Type ValueType);

public sealed record ObjectIndexer(bool IsMutable, Type KeyType, Type ValueType)
    : ObjectBodyType(IsMutable, ValueType);

public sealed record ObjectProperty(bool IsMutable, string Name, Type ValueType)
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

    public Type KeyUnion()
    {
        var propertyKeyType = PropertyKeyUnion();
        if (Indexer == null)
            return propertyKeyType;

        var types = new List<Type> { Indexer.KeyType };
        if (propertyKeyType is UnionType propertyKeyUnion)
            types.AddRange(propertyKeyUnion.Types);
        else
            types.Add(propertyKeyType);

        return TypeSimplifier.Simplify(new UnionType(types));
    }

    public Type ValueUnion()
    {
        var propertyValueType = PropertyUnion();
        if (Indexer == null)
            return propertyValueType;

        var types = new List<Type> { Indexer.ValueType };
        if (propertyValueType is UnionType propertyUnion)
            types.AddRange(propertyUnion.Types);
        else
            types.Add(propertyValueType);

        return TypeSimplifier.Simplify(new UnionType(types));
    }

    public override Type PropertyKeyUnion() => TypeSimplifier.Simplify(new UnionType(Properties.ConvertAll(Type (p) => new LiteralType(p.Name))));
    public Type PropertyUnion() => TypeSimplifier.Simplify(new UnionType(Properties.ConvertAll(p => p.ValueType)));

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

                    if (sourceProperty.IsMutable && !targetProperty.IsMutable)
                        return false;

                    if (!sourceProperty.ValueType.IsAssignableTo(targetProperty.ValueType))
                        return false;
                }

                if (objectType.Indexer == null)
                    return true;

                if (Indexer == null)
                    return false;

                if (Indexer.IsMutable || objectType.Indexer.IsMutable)
                {
                    if (!Indexer.IsMutable && objectType.Indexer.IsMutable
                        || !Indexer.KeyType.Equals(objectType.Indexer.KeyType)
                        || !Indexer.ValueType.Equals(objectType.Indexer.ValueType))
                        return false;
                }
                else
                {
                    if (!Indexer.KeyType.IsAssignableTo(objectType.Indexer.KeyType)
                        || !Indexer.ValueType.IsAssignableTo(objectType.Indexer.ValueType))
                        return false;
                }

                return true;
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