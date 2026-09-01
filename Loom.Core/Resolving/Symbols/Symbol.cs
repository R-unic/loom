using System.Diagnostics.CodeAnalysis;
using Loom.Core.Parsing.AST;
using Loom.Core.Text;

namespace Loom.Core.Resolving.Symbols;

/// <summary>
///     What a name in the source stands for. One class per <see cref="SymbolKind" />, so
///     <see cref="Kind" /> is a fact about the class rather than an argument a caller passes: a
///     <see cref="FunctionSymbol" /> cannot be built claiming to be a variable, and a kind-specific
///     member has a place to live other than the base.
///     <para>
///         The hierarchy divides on what a name can be looked up as. <see cref="TypeSymbol" /> is
///         reachable from type position, <see cref="ValueSymbol" /> from value position, and the resolver
///         keeps a separate lookup table for each (see <c>ResolverScope</c>); a symbol that is neither -
///         <see cref="AttributeSymbol" /> - is reached only through the declaration it annotates.
///     </para>
/// </summary>
public abstract class Symbol(Node declaration, string name, bool isMutable = false)
{
    public SourceFile File { get; } = declaration.File;
    public Node Declaration { get; } = declaration;
    public abstract SymbolKind Kind { get; }
    public string Name { get; } = name;
    public bool IsMutable { get; } = isMutable;
    public bool IsAmbient { get; internal set; }
    public bool IsIntrinsic { get; internal set; }
    public bool IsGlobal { get; internal set; }
    public int? AttributeUsageFlags { get; internal set; }

    public bool IsTypeSymbol => this is TypeSymbol;
    public bool IsValueSymbol => this is ValueSymbol;

    /// <summary>
    ///     The attributes written on this declaration, in source order. Empty for the kinds that cannot
    ///     carry any, and for a declaration that simply has none - which is why it lives here rather than
    ///     on the subset of classes whose declarations may be annotated: reading an attribute off a symbol
    ///     should not depend on first knowing what kind of symbol it is.
    /// </summary>
    public virtual IReadOnlyList<AttributeSymbol> Attributes => [];

    // an event is a runtime value like any other, so it crosses a module boundary through the export
    // table - the generator sends its connection store across alongside it (see EventConnectionTracker).
    // A declared signature crosses one too when it is actually exported: an ambient declare that is never
    // exported names a value trusted to already be a Luau global (see GlobalSymbols) and needs no binding
    // of its own, but an exported one is looked up on whatever module declared it - a hand-written runtime
    // sibling of a '.d.loom' file, for a package like a native binding - the same as any other export.
    public bool EmitsRuntimeBinding => Declaration is VariableDeclaration or FunctionDeclaration or EventDeclaration
        or DeclareVariableSignature or DeclareFunctionSignature;

    public bool HasIntrinsicAttribute(string name) => TryGetIntrinsicAttribute(name, out _);

    public bool TryGetIntrinsicAttribute(string name, [MaybeNullWhen(false)] out AttributeSymbol attribute)
    {
        attribute = Attributes.FirstOrDefault(a => a is { IsIntrinsic: true } && a.Name == name);
        return attribute != null;
    }

    /// <summary>
    ///     Whether a name of this kind is looked up in type position, for the callers that hold a kind rather
    ///     than a symbol. Mirrors which side of <see cref="TypeSymbol" />/<see cref="ValueSymbol" /> the class
    ///     for that kind sits on - <c>SymbolHierarchyTest</c> pins the two together.
    /// </summary>
    public static bool IsTypeKind(SymbolKind kind) => kind is SymbolKind.Interface or SymbolKind.Type or SymbolKind.EnumType or SymbolKind.Trait;

    public override string ToString() => $"Symbol({Kind}, {Name})";
}
