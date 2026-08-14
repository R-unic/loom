using Loom.Core.Pipeline;
using Loom.Core.Resolving.Symbols;

namespace Loom.LanguageServer;

/// <summary>
///     What an interface or trait sits above and below. An interface's supertypes are the interfaces it
///     extends and the traits it implements; a trait has none, since traits do not extend one another. A
///     trait's subtypes are every interface implementing it; an interface's are every interface extending it -
///     the resolver already computed both directions once (<see cref="InterfaceSymbol.Constraints" />,
///     <see cref="TraitSymbol.ImplementedBy" />), so this only has to invert the one relationship it did not
///     keep a back-reference for.
/// </summary>
public static class TypeHierarchy
{
    /// <summary>
    ///     The interface- or trait-shaped symbol under the cursor, or null when there is none to build a
    ///     hierarchy from.
    /// </summary>
    /// <remarks>
    ///     An interface declares a type and a value under one name (<c>new X { … }</c> needs a value to
    ///     construct), so on the declaration itself the first symbol found is not reliably the type - every
    ///     symbol the node declares has to be considered, the same problem <see cref="ImplementationHandler" />
    ///     solves the same way. A use elsewhere has no such ambiguity: a name written in type position only
    ///     ever resolves to the type half, so <see cref="SymbolReferences.At" /> is trusted there.
    /// </remarks>
    public static TypeSymbol? At(CompiledFile file, int offset)
    {
        var node = NodeFinder.FindAt(file.Tree, offset);
        if (node != null && file.SemanticModel.GetDeclarationSymbols(node).OfType<TypeSymbol>().FirstOrDefault(symbol => symbol is InterfaceSymbol or TraitSymbol) is { } declared)
            return declared;

        return SymbolReferences.At(file, offset) switch
        {
            InterfaceSymbol symbol => symbol,
            TraitSymbol symbol => symbol,
            _ => null
        };
    }

    public static IReadOnlyList<TypeSymbol> Supertypes(TypeSymbol symbol) =>
        symbol switch
        {
            InterfaceSymbol @interface => [..@interface.Constraints ?? [], ..@interface.Implements],
            _ => []
        };

    /// <summary>
    ///     Every interface extending this one, if it is an interface; every interface implementing it, if it
    ///     is a trait. Answered by scanning the unit rather than a stored list: nothing above keeps a
    ///     "extended by" back-reference the way <see cref="TraitSymbol.ImplementedBy" /> does for a trait.
    /// </summary>
    public static IReadOnlyList<TypeSymbol> Subtypes(TypeSymbol symbol, CompilationUnit unit)
    {
        if (symbol is TraitSymbol trait)
            return trait.ImplementedBy;

        if (symbol is not InterfaceSymbol @interface)
            return [];

        return unit.AnalyzedModules.Values
            .SelectMany(model => model.DeclaredSymbols)
            .OfType<InterfaceSymbol>()
            .Where(candidate => candidate.Constraints?.Contains(@interface) == true)
            .Distinct()
            .ToArray();
    }
}
