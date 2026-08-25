using System.Collections.Concurrent;
using Loom.Config;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving.Symbols;
using Loom.Core.TypeChecking;
using Loom.Core.TypeChecking.Intrinsic;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.Core.Resolving;

/// <summary>
///     The intrinsic half of every file's ambient scope, built once per project type and shared by every
///     file compiled against it.
/// </summary>
/// <remarks>
///     What the intrinsics put in scope does not depend on the file looking at them: the same ~1,600
///     symbols, under the same names, with the same types. Each file used to rebuild all three tables that
///     describe them - the scope's name lookups, the declaration table behind "which symbol is this node",
///     and the solver's node-to-type map - which cost about 2 ms a file and bought nothing, since every
///     copy was identical. Now each per-file table falls back to this one, and a file only stores what is
///     its own.
///     <para>
///         Immutable once built. A file must not be able to reach in and change what the next file sees,
///         which is also why this holds the tables rather than merging them into each file's.
///     </para>
/// </remarks>
internal sealed class AmbientIntrinsics
{
    /// <summary>The ambient scope every file pushes below its own, rather than declaring into.</summary>
    public ResolverScope Scope { get; } = new();

    /// <summary>Which symbols each intrinsic declaration node declares, for <see cref="SemanticModel" /> to fall back to.</summary>
    public SymbolTable Declarations { get; } = [];

    /// <summary>The type of each intrinsic declaration node, for <see cref="TypeChecking.TypeSolver" /> to fall back to.</summary>
    public Dictionary<NodeId, Type> Types { get; } = [];

    /// <summary>
    ///     What every node below an intrinsic file's own hand-written declarations resolves to - not just
    ///     the declarations themselves - for <see cref="SemanticModel.GetSymbol" /> to fall back to. A
    ///     defaulted trait method's body is real code a consuming file's own generator reads back out (see
    ///     <see cref="Symbols.TraitSymbol.Defaults" /> and <see cref="Generation.LuauGenerator" />'s
    ///     <c>GetOrCreateTraitDefault</c>), and any name or type it uses was only ever resolved by the
    ///     intrinsic bootstrap's own <see cref="SemanticModel" />, never by the consuming file's.
    /// </summary>
    public SymbolTable References { get; } = [];

    /// <summary>Every intrinsic symbol, in no particular order.</summary>
    public IReadOnlyCollection<Symbol> Symbols { get; }

    private static readonly ConcurrentDictionary<ProjectType, Lazy<AmbientIntrinsics>> _cache = new();

    /// <summary>An empty set, for the intrinsic bootstrap itself - it has no intrinsics to resolve against.</summary>
    public static readonly AmbientIntrinsics None = new(IntrinsicRegistration.Empty);

    private AmbientIntrinsics(IntrinsicRegistration registration)
    {
        var symbols = new List<Symbol>(registration.Symbols.Count);
        foreach (var (symbol, type) in registration.Symbols)
        {
            symbols.Add(symbol);
            Scope.Declare(symbol);

            var id = symbol.Declaration.Id;
            if (!Declarations.TryGetValue(id, out var declared))
                Declarations[id] = declared = [];

            declared.Add(symbol);
            Types[id] = type;
        }

        Symbols = symbols;

        var (nodeReferences, nodeTypes) = registration.NodeBindings;
        foreach (var (id, referenced) in nodeReferences)
            References[id] = referenced;

        foreach (var (id, type) in nodeTypes)
            Types.TryAdd(id, type);
    }

    /// <inheritdoc cref="AmbientIntrinsics" />
    public static AmbientIntrinsics For(Pipeline.CompilationUnit unit)
    {
        var registration = Intrinsics.Register(unit);
        if (registration.Symbols.Count == 0)
            return None;

        return _cache
            .GetOrAdd(
                unit.Config.ProjectType,
                _ => new Lazy<AmbientIntrinsics>(() => new AmbientIntrinsics(registration), LazyThreadSafetyMode.ExecutionAndPublication)
            )
            .Value;
    }
}
