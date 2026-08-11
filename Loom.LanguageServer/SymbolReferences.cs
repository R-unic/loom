using Loom.Core.Parsing.AST;
using Loom.Core.Pipeline;
using Loom.Core.Resolving;
using Loom.Core.Resolving.Symbols;
using Loom.Core.Text;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.LanguageServer;

/// <summary>One place a symbol's name is written, and whether that place is the declaration itself.</summary>
public sealed record SymbolReference(SourceFile File, Token Name, bool IsDeclaration)
{
    /// <summary>Whether the reference is being assigned to rather than read.</summary>
    public bool IsWrite { get; init; }
}

/// <summary>
///     Every place a symbol is written, across the whole unit. The resolver already answers "what does this
///     name mean"; this asks it the other way round, by walking each analyzed tree and keeping the nodes whose
///     answer was this symbol - an import binds the exporting module's own symbol instance rather than a copy,
///     so one identity spans every file that uses it.
/// </summary>
public static class SymbolReferences
{
    /// <summary>The symbol named at <paramref name="offset" />, whether the cursor is on its declaration or on a use of it.</summary>
    public static Symbol? At(CompiledFile file, int offset)
    {
        var node = NodeFinder.FindAt(file.Tree, offset);
        if (node == null)
            return null;

        var semanticModel = file.SemanticModel;
        return semanticModel.GetSymbol(node)
            ?? semanticModel.GetDeclarationSymbol(node)
            ?? SpecifierAt(node, semanticModel)
            ?? MemberAt(node, offset, semanticModel);
    }

    /// <summary>
    ///     The symbol an import or export specifier names. A specifier declares nothing of its own - it binds
    ///     the exporting module's symbol into this scope - so neither symbol table has an entry under its node.
    /// </summary>
    private static Symbol? SpecifierAt(Node node, SemanticModel semanticModel) =>
        node switch
        {
            ImportSpecifier specifier => semanticModel.ImportBindings.Find(binding => binding.Specifier == specifier)?.Symbol,
            ExportSpecifier specifier => semanticModel.Exports.Find(export => export.SourceName == specifier.Name.Text)?.Symbol,
            _ => null
        };

    /// <summary>
    ///     Every reference in every file the unit analyzed, declaration included. The whole project is walked,
    ///     so the caller's cancellation is checked between files - this is the one request whose cost grows
    ///     with the project rather than with the document.
    /// </summary>
    public static IReadOnlyList<SymbolReference> Of(Symbol symbol, CompilationUnit unit, CancellationToken cancellationToken = default)
    {
        var references = new List<SymbolReference>();
        foreach (var (file, semanticModel) in unit.AnalyzedModules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Collect(references, symbol, file, semanticModel);
        }

        return references;
    }

    /// <summary>Every reference in one file, for the questions that are only ever about the open document.</summary>
    public static IReadOnlyList<SymbolReference> In(Symbol symbol, CompiledFile file)
    {
        var references = new List<SymbolReference>();
        Collect(references, symbol, file.SourceFile, file.SemanticModel);
        return references;
    }

    private static void Collect(List<SymbolReference> references, Symbol symbol, SourceFile file, SemanticModel semanticModel)
    {
        foreach (var node in Walk(semanticModel.Tree))
            switch (node)
            {
                case NamedDeclaration declaration when Declares(semanticModel, declaration, symbol):
                    Add(references, file, declaration.Name, symbol, isDeclaration: true);
                    break;
                case Identifier identifier when Refers(semanticModel, identifier, symbol):
                    Add(references, file, identifier.Name, symbol, isDeclaration: false, IsAssignedTo(identifier));
                    break;
                case TypeName typeName when Refers(semanticModel, typeName, symbol):
                    Add(references, file, typeName.Name, symbol, isDeclaration: false);
                    break;
                case QualifiedName qualified:
                    CollectMembers(references, symbol, file, semanticModel, qualified.Identifier, qualified.Names);
                    break;
                case PropertyAccess propertyAccess:
                    CollectMembers(references, symbol, file, semanticModel, propertyAccess.Expression, propertyAccess.Names);
                    break;
            }

        CollectSpecifiers(references, symbol, file, semanticModel);
    }

    /// <summary>
    ///     An import or export list names the symbol at its source, which is the name a rename has to follow.
    ///     The local half of an <c>as</c> clause is a different name for the same symbol and is left alone -
    ///     it belongs to the importing file, not to the declaration.
    /// </summary>
    private static void CollectSpecifiers(List<SymbolReference> references, Symbol symbol, SourceFile file, SemanticModel semanticModel)
    {
        foreach (var binding in semanticModel.ImportBindings)
            if (binding.Symbol == symbol)
                Add(references, file, binding.Specifier.Name, symbol, isDeclaration: false);

        foreach (var export in semanticModel.Exports)
            if (export.Symbol == symbol && export.Export is ExportList list)
                foreach (var specifier in list.Specifiers.Where(specifier => specifier.Name.Text == symbol.Name))
                    Add(references, file, specifier.Name, symbol, isDeclaration: false);
    }

    /// <summary>
    ///     The links of a dotted name that resolve to <paramref name="symbol" />. A member name is a token of
    ///     its access expression rather than a node of its own, so nothing in the tree carries its resolution -
    ///     the receiver's type has to be walked forward one link at a time.
    /// </summary>
    private static void CollectMembers(
        List<SymbolReference> references,
        Symbol symbol,
        SourceFile file,
        SemanticModel semanticModel,
        Node receiver,
        List<DotName> names)
    {
        if (symbol is not PropertySymbol)
            return;

        var receiverType = TypeOf(semanticModel, receiver);
        foreach (var dotName in names)
        {
            var name = dotName.Name.Text;
            if (receiverType != null && semanticModel.GetPropertySymbol(receiverType, [name]) == symbol)
                Add(references, file, dotName.Name, symbol, isDeclaration: false);

            receiverType = TypeMembers.PropertyType(receiverType, name);
        }
    }

    /// <summary>
    ///     Records the reference, unless the name written there is a different one. An <c>as</c> clause makes a
    ///     file call an imported symbol by a local name while still resolving to the same symbol, and renaming
    ///     the declaration must not rewrite those - the alias is the importing file's own choice.
    /// </summary>
    private static void Add(List<SymbolReference> references, SourceFile file, Token name, Symbol symbol, bool isDeclaration, bool isWrite = false)
    {
        if (name.Text != symbol.Name)
            return;

        references.Add(new SymbolReference(file, name, isDeclaration) { IsWrite = isWrite || isDeclaration });
    }

    private static bool Declares(SemanticModel semanticModel, Node node, Symbol symbol) =>
        semanticModel.Declarations.GetValueOrDefault(node.Id, []).Contains(symbol);

    private static bool Refers(SemanticModel semanticModel, Node node, Symbol symbol) =>
        semanticModel.References.GetValueOrDefault(node.Id, []).Contains(symbol);

    private static bool IsAssignedTo(Node node) => node.Parent is AssignmentOperator assignment && assignment.Left == node;

    /// <summary>The member named at <paramref name="offset" /> in a dotted chain, for a cursor sitting on a property rather than on a name.</summary>
    private static Symbol? MemberAt(Node node, int offset, SemanticModel semanticModel)
    {
        var (receiver, names) = node switch
        {
            QualifiedName qualified => ((Node?)qualified.Identifier, qualified.Names),
            PropertyAccess propertyAccess => (propertyAccess.Expression, propertyAccess.Names),
            _ => (null, null)
        };

        if (receiver == null || names == null)
            return null;

        var receiverType = TypeOf(semanticModel, receiver);
        foreach (var dotName in names)
        {
            var name = dotName.Name.Text;
            if (TextSpan.FromStartEnd(dotName.Dot.Span.End, dotName.Name.Span.End).Contains(offset))
                return receiverType == null ? null : semanticModel.GetPropertySymbol(receiverType, [name]);

            receiverType = TypeMembers.PropertyType(receiverType, name);
        }

        return null;
    }

    private static IEnumerable<Node> Walk(Tree tree) => tree.EnumerateDescendants().Prepend(tree);

    private static Type? TypeOf(SemanticModel semanticModel, Node node)
    {
        try
        {
            return semanticModel.GetType(node);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
