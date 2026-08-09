using Loom.Core.Resolving.Symbols;

namespace Loom.LanguageServer;

public sealed record VisibleSymbol(string Name, SymbolKind Kind, string TypeDescription);
