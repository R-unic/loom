using System.Collections.Immutable;
using Loom.Core.Resolving.Symbols;
using Loom.Core.TypeChecking.Types;
using Type = Loom.Core.TypeChecking.Types.Type;
using Loom.Core.TypeChecking.Solving;

namespace Loom.Core.FlowAnalysis;

/// <summary>
///     An immutable snapshot of definite-assignment and type-narrowing information at a point
///     in the control flow graph.
///     For code that needs to accumulate several edits before publishing a snapshot (see
///     <see cref="TypeNarrower" />), use <see cref="ToBuilder" /> rather than repeatedly indexing into
///     NarrowedTypes/etc. on an already-built FlowState.
/// </summary>
public sealed class FlowState(
    ImmutableHashSet<Symbol>? definitelyInitialized = null,
    ImmutableHashSet<Symbol>? maybeInitialized = null,
    bool isUnreachable = false,
    ImmutableDictionary<FlowAddress, Type>? narrowedTypes = null
)
{
    public static readonly FlowState Empty = new();

    public FlowState(FlowState from)
        : this(from.DefinitelyInitialized, from.MaybeInitialized, from.IsUnreachable, from.NarrowedTypes)
    {
    }

    public ImmutableHashSet<Symbol> DefinitelyInitialized { get; private init; } = definitelyInitialized ?? [];
    public ImmutableHashSet<Symbol> MaybeInitialized { get; private init; } = maybeInitialized ?? [];
    public bool IsUnreachable { get; init; } = isUnreachable;
    public ImmutableDictionary<FlowAddress, Type> NarrowedTypes { get; } = narrowedTypes ?? [];

    /// <summary>Returns a new state with <paramref name="symbol" /> marked definitely and maybe initialized.</summary>
    public FlowState WithInitialized(Symbol symbol) =>
        new(this) { DefinitelyInitialized = DefinitelyInitialized.Add(symbol), MaybeInitialized = MaybeInitialized.Add(symbol) };

    /// <summary>Returns a new state with <paramref name="address" /> narrowed to <paramref name="type" />.</summary>
    public FlowState WithNarrowedType(FlowAddress address, Type type) =>
        new(DefinitelyInitialized, MaybeInitialized, IsUnreachable, NarrowedTypes.SetItem(address, type));

    /// <summary>Returns a new state with any narrowing on <paramref name="address" /> removed.</summary>
    public FlowState WithoutNarrowedType(FlowAddress address) =>
        NarrowedTypes.ContainsKey(address) ? new FlowState(DefinitelyInitialized, MaybeInitialized, IsUnreachable, NarrowedTypes.Remove(address)) : this;

    /// <summary>Batch form of <see cref="WithInitialized(Symbol)" /> for parameter lists, tuple patterns, etc.</summary>
    public FlowState WithInitialized(IEnumerable<Symbol> symbols)
    {
        var definitelyBuilder = DefinitelyInitialized.ToBuilder();
        var maybeBuilder = MaybeInitialized.ToBuilder();
        foreach (var symbol in symbols)
        {
            definitelyBuilder.Add(symbol);
            maybeBuilder.Add(symbol);
        }

        return new FlowState(this) { DefinitelyInitialized = definitelyBuilder.ToImmutable(), MaybeInitialized = maybeBuilder.ToImmutable() };
    }

    public FlowState Merge(FlowState other) =>
        new(
            DefinitelyInitialized.Intersect(other.DefinitelyInitialized),
            MaybeInitialized.Union(other.MaybeInitialized),
            IsUnreachable && other.IsUnreachable,
            MergeNarrowedTypes(other)
        );

    private ImmutableDictionary<FlowAddress, Type> MergeNarrowedTypes(FlowState other)
    {
        if (NarrowedTypes.IsEmpty || other.NarrowedTypes.IsEmpty)
            return [];

        var (smaller, larger) = NarrowedTypes.Count <= other.NarrowedTypes.Count
            ? (NarrowedTypes, other.NarrowedTypes)
            : (other.NarrowedTypes, NarrowedTypes);

        var builder = ImmutableDictionary.CreateBuilder<FlowAddress, Type>();
        foreach (var (address, type) in smaller)
            if (larger.TryGetValue(address, out var otherType))
                builder[address] = TypeSimplifier.Simplify(new UnionType([type, otherType]));

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Starts a mutable builder seeded from this state, for code that needs to apply several
    ///     edits (e.g. narrowing a handful of addresses across a property-access chain) before
    ///     publishing a final snapshot via <see cref="Builder.ToImmutable" />.
    /// </summary>
    public Builder ToBuilder() => new(this);

    public sealed class Builder
    {
        internal Builder(FlowState from)
        {
            DefinitelyInitialized = from.DefinitelyInitialized.ToBuilder();
            MaybeInitialized = from.MaybeInitialized.ToBuilder();
            IsUnreachable = from.IsUnreachable;
            NarrowedTypes = from.NarrowedTypes.ToBuilder();
        }

        private ImmutableHashSet<Symbol>.Builder DefinitelyInitialized { get; }
        private ImmutableHashSet<Symbol>.Builder MaybeInitialized { get; }
        private bool IsUnreachable { get; }
        public ImmutableDictionary<FlowAddress, Type>.Builder NarrowedTypes { get; }

        public FlowState ToImmutable() =>
            new(
                DefinitelyInitialized.ToImmutable(),
                MaybeInitialized.ToImmutable(),
                IsUnreachable,
                NarrowedTypes.ToImmutable()
            );
    }
}
