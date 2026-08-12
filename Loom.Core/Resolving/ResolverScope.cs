global using SymbolLookup = System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Loom.Core.Resolving.Symbols.Symbol>>;

namespace Loom.Core.Resolving;

/// <summary>
///     Which of a scope's two tables a name is looked up in. A type and a value may share a name without
///     colliding — <c>interface Point</c> declares both — so every declaration and every lookup has to say
///     which one it means.
/// </summary>
internal enum SymbolNamespace : byte
{
    Value,
    Type
}

internal sealed record ResolverScope
{
    private readonly SymbolLookup _values = [];
    private readonly SymbolLookup _types = [];

    /// <summary>
    ///     The table <paramref name="symbolNamespace" /> names. Callers ask for a namespace rather than
    ///     picking a table themselves, so "which table" is decided once here instead of at every call site.
    /// </summary>
    public SymbolLookup Lookup(SymbolNamespace symbolNamespace) => symbolNamespace == SymbolNamespace.Type ? _types : _values;

    /// <summary>
    ///     Puts <paramref name="symbol" /> in this scope under its own name, skipping the checks
    ///     <see cref="Resolver" />'s declaration path makes. For a scope built from symbols that were
    ///     already declared and checked somewhere else - see <see cref="AmbientIntrinsics" />.
    /// </summary>
    public void Declare(Symbols.Symbol symbol)
    {
        var lookup = Lookup(symbol.IsTypeSymbol ? SymbolNamespace.Type : SymbolNamespace.Value);
        if (!lookup.TryGetValue(symbol.Name, out var symbols))
            lookup[symbol.Name] = symbols = [];

        symbols.Add(symbol);
    }
}
