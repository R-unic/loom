global using SymbolLookup = System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Loom.Core.Resolving.Symbols.Symbol>>;

namespace Loom.Core.Resolving;

internal sealed record ResolverScope
{
    public SymbolLookup VariableLookup { get; } = [];
    public SymbolLookup TypeLookup { get; } = [];
}