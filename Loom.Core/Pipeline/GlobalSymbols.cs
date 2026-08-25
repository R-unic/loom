using Loom.Core.Resolving.Symbols;
using Loom.Core.Text;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.Core.Pipeline;

/// <summary>
///     The ambient declarations of a unit's <c>.d.loom</c> files, partitioned by the root they were declared
///     in. A file resolves against its own root's globals and no other's, so a dependency's declaration files
///     furnish that dependency alone.
/// </summary>
/// <remarks>
///     A package therefore cannot hand ambient names to the projects depending on it: its public surface is
///     what it exports, which is versioned, named at the import site and shadowable, none of which is true of
///     a name that simply appears in scope. Intrinsics are not partitioned this way — they belong to the
///     language rather than to any project, and reach every file of every root through
///     <see cref="TypeChecking.Intrinsic.Intrinsics.Register" />.
/// </remarks>
public sealed class GlobalSymbols(SourceRootSet roots)
{
    private static readonly Dictionary<Symbol, Type> _none = [];

    private readonly Dictionary<SourceRoot, RootGlobals> _byRoot = [];

    /// <summary>One root's ambient declarations, kept both by symbol and by the name each one occupies.</summary>
    private sealed class RootGlobals
    {
        public Dictionary<Symbol, Type> Types { get; } = [];

        /// <summary>
        ///     The symbol holding each ambient name, keyed by namespace as well: types and values are looked up
        ///     separately, so an interface and a variable of one name are not in each other's way.
        /// </summary>
        public Dictionary<(string Name, bool IsTypeSymbol), Symbol> ByName { get; } = [];
    }

    /// <summary>The globals <paramref name="file" /> resolves against: its own root's, and only those.</summary>
    public IReadOnlyDictionary<Symbol, Type> Of(SourceFile file) => Of(roots.Of(file));

    public IReadOnlyDictionary<Symbol, Type> Of(SourceRoot root) => _byRoot.TryGetValue(root, out var globals) ? globals.Types : _none;

    /// <summary>Registers <paramref name="symbol" /> as an ambient declaration of <paramref name="root" />.</summary>
    /// <returns>
    ///     The declaration already holding that name in this root, which is the collision to report — the
    ///     symbol registered first stays, since it is the one every file analyzed so far has resolved against.
    ///     Null when the name was free, or when the symbol is simply being re-registered.
    /// </returns>
    public Symbol? Declare(SourceRoot root, Symbol symbol, Type type)
    {
        if (!_byRoot.TryGetValue(root, out var globals))
            _byRoot[root] = globals = new RootGlobals();

        var name = (symbol.Name, symbol.IsTypeSymbol);
        if (globals.ByName.TryGetValue(name, out var existing) && existing != symbol)
            return existing;

        globals.Types[symbol] = type;
        globals.ByName[name] = symbol;
        return null;
    }

    public void Clear() => _byRoot.Clear();
}
