using Loom.Core.Parsing.AST;
using Loom.Core.Resolving.Symbols;
using Loom.Core.Text;

namespace Loom.Core.Resolving;

/// <summary>
///     One name a module exports. <c>Name</c> is what importers see and <c>SourceName</c> is what it is called
///     where it comes from an <c>as</c> clause makes the two differ. <c>Module</c> is set only for a
///     re-export, naming the module whose export is being forwarded. <c>IsInternal</c> is true for one written
///     with <c>internal</c> rather than <c>export</c> - visible the same as any other export to a file in the
///     same <see cref="Pipeline.SourceRoot" />, invisible to one reaching it as a package dependency of a
///     different root.
/// </summary>
public sealed record ExportBinding(
    string Name,
    string SourceName,
    Symbol Symbol,
    IReExport? Export = null,
    SourceFile? Module = null,
    bool IsInternal = false
)
{
    public bool IsReExport => Module != null;
    public bool EmitsRuntimeBinding => Symbol.EmitsRuntimeBinding;
    public string? ModulePath => Export?.ModulePath;

    public static ExportBinding OfDeclaration(Symbol symbol, bool isInternal = false) => new(symbol.Name, symbol.Name, symbol, IsInternal: isInternal);
}