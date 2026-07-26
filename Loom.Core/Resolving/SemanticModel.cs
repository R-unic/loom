global using SymbolTable = System.Collections.Generic.Dictionary<Loom.Core.Parsing.AST.NodeId, System.Collections.Generic.List<Loom.Core.Resolving.Symbol>>;
using System.Diagnostics.CodeAnalysis;
using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.TypeChecking;
using Loom.Core.TypeChecking.Types;
using LiteralType = Loom.Core.TypeChecking.Types.LiteralType;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.Core.Resolving;

public sealed record SemanticModel(Tree Tree, DiagnosticBag Diagnostics, SymbolTable Declarations, SymbolTable References)
    : DiagnosedResult(Diagnostics)
{
    internal int RuntimeReferences = 0;
    public List<ExportBinding> Exports { get; } = [];

    private Dictionary<string, List<ExportBinding>> ExportsByName { get; } = [];

    public List<ImportBinding> ImportBindings { get; } = [];

    private HashSet<Symbol> ImportedSymbols { get; } = [];
    public bool DisableRuntimeLibraryImport { get; set; }
    public bool EmitDebugDiagnostics { get; set; }
    public bool MustImportRuntimeLibrary =>
        !DisableRuntimeLibraryImport
        && !Tree.File.IsIntrinsic
        && (
            RuntimeReferences > 0
            || References.Any(pair => NonIntrinsicReferenceNodes.Contains(pair.Key)
                && pair.Value.Any(s => s is { File.Name: "runtime.loom", IsIntrinsic: true, IsTypeSymbol: true })
            )
        );

    /// <summary>
    ///     Node IDs of reference-site nodes that originate from a non-intrinsic source file.
    ///     Populated by the resolver as references are recorded, so
    ///     <see cref="MustImportRuntimeLibrary" /> can filter references by the file of the
    ///     referencing node without holding on to the node instances themselves.
    /// </summary>
    internal HashSet<NodeId> NonIntrinsicReferenceNodes { get; } = [];
    internal TypeSolver TypeSolver { get; } = new(new DiagnosticBag());
    private SymbolLookup DeclarationsByName => field ??= Declarations.Values.SelectMany(s => s).GroupBy(s => s.Name).ToDictionary(g => g.Key, g => g.ToList());

    public List<NamespaceImportBinding> NamespaceImports { get; } = [];

    internal void AddExport(ExportBinding binding)
    {
        Exports.Add(binding);
        if (!ExportsByName.TryGetValue(binding.Name, out var bindings))
            ExportsByName[binding.Name] = bindings = [];

        bindings.Add(binding);
    }

    public List<ExportBinding> FindExports(string name) => ExportsByName.GetValueOrDefault(name, []);

    internal void AddImportBinding(ImportBinding binding)
    {
        ImportBindings.Add(binding);
        ImportedSymbols.Add(binding.Symbol);
    }

    internal void AddNamespaceImport(NamespaceImportBinding binding)
    {
        NamespaceImports.Add(binding);
        ImportedSymbols.Add(binding.Symbol);
    }

    /// <summary>
    ///     Whether the symbol was declared by another module and brought in by an import. Its declaration
    ///     belongs to a tree this model never walked, so anything reasoning about the declaration's position —
    ///     flow analysis in particular — has to treat it as coming from outside.
    /// </summary>
    public bool IsImported(Symbol symbol) => ImportedSymbols.Contains(symbol);

    internal void MarkImportUsed(Symbol symbol)
    {
        if (!ImportedSymbols.Contains(symbol))
            return;

        foreach (var binding in ImportBindings.Where(binding => binding.Symbol == symbol))
            binding.MarkUsed();

        foreach (var binding in NamespaceImports.Where(binding => binding.Symbol == symbol))
            binding.MarkUsed();
    }

    public bool IsCompileTimeConstant(Expression expression) =>
        expression is Literal or NameOf
        || expression is QualifiedName name && GetDeclaringSymbol(name.Identifier) is { Declaration: EnumDeclaration }
        || expression is PropertyAccess access && GetDeclaringSymbol(access.Expression) is { Declaration: EnumDeclaration }
        || expression is ElementAccess elementAccess && GetDeclaringSymbol(elementAccess.Expression) is { Declaration: EnumDeclaration };

    public object? GetConstantValue(Expression expression) =>
        expression switch
        {
            QualifiedName qn when GetType(qn.Identifier) is ObjectType objectType
                && objectType.GetProperty(qn.Names.First().Name.Text) is { ValueType: LiteralType literalType } =>
                literalType.Value,
            _ when GetType(expression) is LiteralType literalType => literalType.Value,
            _ => null
        };

    public List<Symbol> GetDeclarationSymbols(Node node)
    {
        while (true)
        {
            if (node is not Declare declare)
                return Declarations.GetValueOrDefault(node.Id, []);

            node = declare.Signature;
        }
    }

    public Symbol? GetSymbol(Node node, SymbolKind? kind = null) => FindSymbol(node, kind, References);

    public Symbol? GetDeclarationSymbol(Node node, SymbolKind? kind = null)
    {
        while (true)
        {
            if (node is not Declare declare)
                return FindSymbol(node, kind, Declarations);

            node = declare.Signature;
        }
    }

    public Symbol? GetDeclaringSymbol(Node node, SymbolKind? kind = null)
    {
        var referenceSymbol = GetSymbol(node, kind);
        return referenceSymbol == null ? null : GetDeclarationSymbol(referenceSymbol.Declaration, kind);
    }

    public bool TryGetIntrinsicAttribute(Expression expression, string name, [MaybeNullWhen(false)] out AttributeSymbol attribute)
    {
        attribute = null;
        var property = GetPropertySymbol(expression);
        return property != null && property.TryGetIntrinsicAttribute(name, out attribute);
    }

    public PropertySymbol? GetPropertySymbol(Expression expression)
    {
        var (objectExpression, names) = expression switch
        {
            QualifiedName qualified => (qualified.Identifier as Expression, qualified.Names.Select(d => d.Name.Text).ToArray()),
            PropertyAccess propertyAccess => (propertyAccess.Expression, propertyAccess.Names.Select(d => d.Name.Text).ToArray()),
            ElementAccess { IndexExpression: Literal { Value: string propertyName } } elementAccess => (elementAccess.Expression, [propertyName]),
            _ => (expression, [])
        };

        if (names.Length == 0)
            return null;

        var objectType = GetType(objectExpression);
        if (objectType is InstantiatedType instantiated)
            objectType = instantiated.Expand();

        if (objectType is not InterfaceType interfaceType)
            return null;

        var interfaceSymbol = FindDeclarationSymbol<InterfaceSymbol>(interfaceType.Name);
        return interfaceSymbol?.GetPropertyAtPath(names);
    }

    public Type GetType(Node node) => TypeSolver.GetType(node);
    public Type? GetDeclarationType(Node node) => GetSymbol(node) is { } symbol ? TypeSolver.GetType(symbol.Declaration) : null;
    public T? FindIntrinsicDeclarationSymbol<T>(string name) where T : Symbol => FindDeclarationSymbol<T>(name, s => s.IsIntrinsic);
    private T? FindDeclarationSymbol<T>(string name) where T : Symbol => FindDeclarationSymbol<T>(name, static _ => true);

    private T? FindDeclarationSymbol<T>(string name, Func<T, bool> predicate) where T : Symbol =>
        DeclarationsByName.TryGetValue(name, out var symbols) ? symbols.OfType<T>().FirstOrDefault(predicate) : null;

    private static Symbol? FindSymbol(Node node, SymbolKind? kind, SymbolTable table)
    {
        var symbols = table.GetValueOrDefault(node.Id, []);
        return kind != null ? symbols.Find(s => s.Kind == kind) : symbols.FirstOrDefault();
    }
}