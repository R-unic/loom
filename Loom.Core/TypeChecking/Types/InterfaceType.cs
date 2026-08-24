namespace Loom.Core.TypeChecking.Types;

public sealed class InterfaceType(
    string name,
    List<InterfaceType> constraints,
    ObjectType objectType,
    HashSet<string>? traitMethodNames = null,
    Dictionary<string, string>? metamethods = null
) : NativelyIndexableType
{
    private Type? _cachedAssignabilityType;
    private List<ObjectProperty>? _cachedProperties;

    private int _assignabilityVersion = -1;
    private int _propertiesVersion = -1;
    private Dictionary<string, ObjectProperty>? _propertyMap;
    public string Name { get; } = name;
    public List<InterfaceType> Constraints { get; } = constraints;
    public ObjectType ObjectType { get; } = objectType;
    public Type AssignabilityType
    {
        get
        {
            if (_cachedAssignabilityType != null && _assignabilityVersion == EffectiveVersion)
                return _cachedAssignabilityType;

            _cachedAssignabilityType = Constraints.Count > 0
                ? new IntersectionType([ObjectType, ..Constraints.Select(c => c.AssignabilityType)])
                : ObjectType;

            _assignabilityVersion = EffectiveVersion;
            return _cachedAssignabilityType;
        }
    }

    public HashSet<string> TraitMethodNames { get; init; } = traitMethodNames ?? [];

    /// <summary>
    ///     The element type this yields when iterated, for a type implementing <c>Iterator&lt;T&gt;</c>, or
    ///     null when it iterates the ordinary way - by its own keys and values.
    /// </summary>
    /// <remarks>
    ///     Carried on the type the same way <see cref="Metamethods" /> is, and for the same reason: a trait
    ///     implementation is written outside the interface's own declaration, so it reaches neither
    ///     <see cref="Constraints" /> - which is what the interface inherits from - nor the property list.
    ///     <see cref="TraitMethodNames" /> is no good either, being populated only where a value is
    ///     constructed: a type reaching a loop through a function's return type would answer "not an
    ///     iterator" and be walked field by field, which is a wrong loop rather than a rejected one.
    /// </remarks>
    public Type? IteratedElementType { get; init; }

    /// <summary>Metamethod name (e.g. "__add") to member name, merged from this interface's own properties plus every trait it implements.</summary>
    public Dictionary<string, string> Metamethods { get; init; } = metamethods ?? [];

    /// <summary>
    ///     Whether this interface is one of the compiler's own intrinsics (<see cref="Resolving.Symbols.InterfaceSymbol.IsIntrinsic" />),
    ///     rather than a user-declared interface that merely shares an intrinsic's name. A macro provider
    ///     matching by bare <see cref="Name" /> alone would otherwise hijack a user's own <c>Future</c>,
    ///     <c>Set</c> or <c>Result</c> - names the resolver deliberately lets a module shadow - so every
    ///     <c>Supports</c> check needs this alongside the name match.
    /// </summary>
    public bool IsIntrinsic { get; init; }
    public override ObjectIndexer? Indexer
    {
        get => Indexers.FirstOrDefault();
        internal set => throw new NotImplementedException();
    }

    /// <summary>Own indexer first, then each constraint's - recursively, so a multi-level inheritance chain still surfaces every one.</summary>
    public override IEnumerable<ObjectIndexer> Indexers =>
        ObjectType.Indexer is { } own
            ? [own, ..Constraints.SelectMany(c => c.Indexers)]
            : Constraints.SelectMany(c => c.Indexers);

    /// <summary>
    ///     Cheap-to-recompute version signal combining this interface's own <see cref="ObjectType.Version" />
    ///     with each constraint's effective version, so caches invalidate when a constraint's properties
    ///     grow (via <see cref="ObjectType.AddProperties" />) after this interface was constructed. Constraint
    ///     lists are small (0-3 typically), so summing across them on every access is cheap - only the
    ///     expensive merged-list rebuild below is actually guarded by it.
    /// </summary>
    private int EffectiveVersion => ObjectType.Version + Constraints.Sum(c => c.EffectiveVersion);

    public override List<ObjectProperty> Properties
    {
        get
        {
            EnsureCaches();
            return _cachedProperties!;
        }
    }

    private void EnsureCaches()
    {
        var currentVersion = EffectiveVersion;
        if (_cachedProperties != null && _propertiesVersion == currentVersion)
            return;

        _cachedProperties = [..ObjectType.Properties, ..Constraints.SelectMany(c => c.Properties)];
        _propertyMap = new Dictionary<string, ObjectProperty>(_cachedProperties.Count);
        foreach (var property in _cachedProperties)
            _propertyMap.TryAdd(property.Name, property);

        _propertiesVersion = currentVersion;
    }

    protected override ObjectProperty? FindProperty(string name)
    {
        EnsureCaches();
        return _propertyMap!.GetValueOrDefault(name);
    }

    public override Type PropertyKeyUnion()
    {
        // Each constraint's own PropertyKeyUnion, not its ObjectType's: recursively, the way Properties and
        // Indexers reach through a chain of inheritance. Stopping one level short left the keys of a
        // multi-level chain out of the union while its values and its property lookups still had them.
        var baseType = ObjectType.PropertyKeyUnion();
        var constraintTypes = Constraints.Select(constraint => constraint.PropertyKeyUnion());
        var unionTypes = new List<Type>([baseType, ..constraintTypes]);
        return TypeSimplifier.Simplify(new UnionType(unionTypes));
    }

    public override bool Equals(Type? other) =>
        GuardedEquals(
            this,
            other,
            () => other is InterfaceType interfaceType
                && ListEquals(Constraints, interfaceType.Constraints)
                && ObjectType.Equals(interfaceType.ObjectType)
        );

    public override int GetHashCode() => HashCode.Combine(Name, Constraints.Count, ObjectType.GetHashCode());
    public override bool IsAssignableTo(Type other) => GuardedAssignableTo(this, other, () => AssignabilityType.IsAssignableTo(other));
    public override string ToString() => Name;

    internal bool MatchOrMatchConstraint(Predicate<InterfaceType> predicate) => predicate(this) || Constraints.Any(c => c.MatchOrMatchConstraint(predicate));
}