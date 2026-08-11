using System.Diagnostics.CodeAnalysis;
using Loom.Core.Resolving;
using Loom.Core.Resolving.Symbols;

namespace Loom.Core.Parsing.AST;

public static class WithAttributesExtensions
{
    /// <summary>
    ///     The compiler-defined attribute of this name written on <paramref name="declaration" />, if there
    ///     is one.
    ///     <para>
    ///         Answered off the declaration's <see cref="Symbol" /> rather than off
    ///         <see cref="IWithAttributes.Attributes" /> directly, because only resolution knows which of the
    ///         names written there refer to intrinsic attributes. Every <see cref="IWithAttributes" /> is a
    ///         <see cref="Node" />, which is what makes that lookup possible.
    ///     </para>
    /// </summary>
    public static bool TryGetIntrinsicAttribute(
        this IWithAttributes declaration,
        SemanticModel semanticModel,
        string name,
        [MaybeNullWhen(false)] out AttributeSymbol attribute)
    {
        foreach (var symbol in semanticModel.GetDeclarationSymbols((Node)declaration))
            if (symbol.TryGetIntrinsicAttribute(name, out attribute))
                return true;

        attribute = null;
        return false;
    }
}
