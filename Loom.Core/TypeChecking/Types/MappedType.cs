namespace Loom.Core.TypeChecking.Types;

/// <summary>
///     <c>interface AsMut&lt;T&gt; { mut [K from keyof(T)]: T[K] }</c> - one member per key of another
///     type, written once in terms of the key rather than listed out.
/// </summary>
/// <remarks>
///     Deferred the same way <see cref="KeyOfType" /> and <see cref="IndexedType" /> are: while the keys
///     are still a type parameter there is nothing to expand, and <see cref="Resolve" /> answers once an
///     instantiation supplies them. The binder is matched by reference rather than by name, so a mapped
///     type whose value expression names a parameter of the enclosing generic with the same letter is
///     still two separate bindings.
/// </remarks>
public sealed class MappedType(TypeParameter binder, Type source, Type valueType, bool isMutable) : Type
{
    public TypeParameter Binder { get; } = binder;

    /// <summary>The keys to map over - <c>keyof(T)</c> above.</summary>
    /// <remarks>
    ///     Settable, with <see cref="ValueType" />, so a mapped type whose body names itself can be published
    ///     before that body is known - the same reason <see cref="GenericType.UnderlyingType" /> is. Resolving
    ///     the two first and constructing afterwards left <c>interface Rec&lt;T&gt; { [K from "a"]: Rec&lt;T&gt; }</c>
    ///     looking up its own name before anything had bound it.
    /// </remarks>
    public Type Source { get; internal set; } = source;

    /// <summary>What one key maps to, in terms of <see cref="Binder" /> - <c>T[K]</c> above.</summary>
    public Type ValueType { get; internal set; } = valueType;
    public bool IsMutable { get; } = isMutable;

    /// <summary>
    ///     The object this describes once its keys are known, or null while they are not.
    /// </summary>
    /// <remarks>
    ///     Concrete properties rather than an indexer, because an indexer keyed on the whole key union
    ///     loses the correspondence between a key and its own value type - <c>AsMut&lt;Point&gt;</c> would
    ///     answer <c>number | string</c> for every key rather than the one that key actually has. Luau's
    ///     own output has no way to say that (see the generator), but nothing inside Loom has to give it up.
    /// </remarks>
    public Type? Resolve()
    {
        var keys = ResolvedKeys();
        if (keys == null)
            return null;

        if (IsNever(keys))
            return new ObjectType(null, []);

        var members = keys is UnionType union ? union.Types : [keys];
        var properties = new List<ObjectProperty>();
        foreach (var key in members)
        {
            if (key is not LiteralType { Value: string name })
                return null;

            properties.Add(new ObjectProperty(IsMutable, name, TypeSubstitution.Apply(ValueType, new TypeParameterSubstitution { [Binder] = key })));
        }

        return new ObjectType(null, properties);
    }

    private Type? ResolvedKeys()
    {
        var keys = TypeSimplifier.ResolveKeys(Source) ?? TypeSimplifier.Expanded(Source);
        if (keys is KeyOfType keyOf)
            keys = TypeSimplifier.ResolveKeys(keyOf);

        return keys is TypeParameter or TypeVariable or IndexedType or KeyOfType or ConditionalType or MappedType ? null : keys;
    }

    public override int GetHashCode() => HashCode.Combine(typeof(MappedType), Source, ValueType, IsMutable);

    public override bool Equals(Type? other) =>
        GuardedEquals(
            this,
            other,
            () => other is MappedType mapped
                && IsMutable == mapped.IsMutable
                && Source.Equals(mapped.Source)
                && ValueType.Equals(mapped.ValueType)
        );

    public override bool IsAssignableTo(Type other) => Resolve() is { } resolved ? resolved.IsAssignableTo(other) : base.IsAssignableTo(other);
    public override string ToString() => $"{{ {(IsMutable ? "mut " : "")}[{Binder.Name} from {Source}]: {ValueType} }}";
}
