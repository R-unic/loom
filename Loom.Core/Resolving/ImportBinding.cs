using Loom.Core.Parsing.AST;
using Loom.Core.Text;

namespace Loom.Core.Resolving;

/// <summary>
///     One name brought into a module by an import.
/// </summary>
/// <remarks>
///     <see cref="Symbol" /> is the exporting module's own symbol instance rather than a copy, so that code
///     asking for an <see cref="InterfaceSymbol" /> or a <see cref="TraitSymbol" /> still gets one for an
///     imported declaration. Because the symbol carries the exported name, an alias only exists in the
///     importing scope's lookup — this binding is what tells the generator which name to require it under.
/// </remarks>
public sealed record ImportBinding(ImportDeclaration Import, ImportSpecifier Specifier, Symbol Symbol, SourceFile Module)
{
    public string LocalName => Specifier.LocalName.Text;

    public string ExportedName => Specifier.Name.Text;

    public bool IsTypeOnly => Import.IsTypeOnly;

    public bool RequiresModuleAtRuntime => !IsTypeOnly && Symbol.EmitsRuntimeBinding;

    public bool IsUsed { get; private set; }

    internal void MarkUsed() => IsUsed = true;
}