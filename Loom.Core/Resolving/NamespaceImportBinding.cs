using Loom.Core.Parsing.AST;
using Loom.Core.Resolving.Symbols;
using Loom.Core.Text;

namespace Loom.Core.Resolving;

/// <summary>
///     A module bound to a single local name by <c>import * as name</c>. Unlike an
///     <see cref="ImportBinding" /> the symbol is this module's own, since it stands for the required table
///     rather than for any one declaration in the module it came from.
/// </summary>
public sealed record NamespaceImportBinding(NamespaceImport Import, Symbol Symbol, SourceFile Module)
{
    public string LocalName => Import.Name.Text;
    public string ModulePath => Import.ModulePath!;

    public bool IsUsed { get; private set; }

    internal void MarkUsed() => IsUsed = true;
}