using System.Diagnostics.CodeAnalysis;
using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving.Symbols;
using Loom.Luau;

namespace Loom.Core.Resolving;

public sealed partial class Resolver
{
    /// <summary>What declaring a name in the current scope would do.</summary>
    private enum DeclarationOutcome
    {
        /// <summary>Nothing holds the name here yet.</summary>
        Fresh,

        /// <summary>
        ///     This very declaration already has a symbol in this scope, put there by the hoisting pass. The
        ///     symbol standing for it exists and everything referring to the name is already bound to it.
        /// </summary>
        AlreadyHoisted,

        /// <summary>Something else holds the name here.</summary>
        Duplicate
    }

    private bool DeclareVariable(NamedDeclaration node, bool isMutable = false) => DeclareVariable(node, node.Name.Text, isMutable);

    private bool DeclareVariable(Node node, string name, bool isMutable = false) => DeclareVariable(node, new VariableSymbol(node, name, isMutable));

    private bool DeclareVariable(Node node, Symbol symbol) =>
        DeclareOnce(node, symbol, SymbolNamespace.Value, $"Variable '{symbol.Name}' is already declared in this scope.");

    private bool DeclareType(NamedDeclaration node) => DeclareType(node, new TypeAliasSymbol(node, node.Name.Text));

    private bool DeclareType(NamedDeclaration node, TypeSymbol symbol) =>
        DeclareOnce(node, symbol, SymbolNamespace.Type, $"Type '{node.Name.Text}' is already declared in this scope.");

    /// <summary>
    ///     Puts <paramref name="symbol" /> in the current scope, or reports <paramref name="duplicateError" />
    ///     and declares nothing when the name is taken.
    /// </summary>
    /// <remarks>
    ///     A declaration the hoisting pass already handled succeeds without declaring anything a second time:
    ///     the symbol it made is the one every reference resolved against, and adding another for the same
    ///     node would leave two symbols standing for one declaration — equal in nothing but name, with only
    ///     the first reachable through a lookup and any state written to the other simply lost.
    ///     <see cref="DeclareInterface" /> has always worked this way; this is the same rule for the rest.
    /// </remarks>
    private bool DeclareOnce(Node node, Symbol symbol, SymbolNamespace symbolNamespace, string duplicateError)
    {
        switch (Classify(node, symbol.Name, symbolNamespace))
        {
            case DeclarationOutcome.AlreadyHoisted:
                return true;

            case DeclarationOutcome.Duplicate:
                _diagnostics.Error(node, InternalCodes.DuplicateName, duplicateError);
                return false;

            default:
                DeclareSymbol(symbol);
                return true;
        }
    }

    /// <summary>
    ///     Reports <paramref name="error" /> when <paramref name="name" /> is already taken in the current
    ///     scope. For callers that put the symbol in the table themselves — an import binds the exporting
    ///     module's own symbol, under whatever local name the specifier chose — so the check has to be
    ///     separable from the declaring.
    /// </summary>
    private bool HasDuplicateSymbol(Node node, string name, SymbolNamespace symbolNamespace, string error)
    {
        if (Classify(node, name, symbolNamespace) != DeclarationOutcome.Duplicate)
            return false;

        _diagnostics.Error(node, InternalCodes.DuplicateName, error);
        return true;
    }

    private DeclarationOutcome Classify(Node node, string name, SymbolNamespace symbolNamespace)
    {
        if (!CurrentScope().Lookup(symbolNamespace).TryGetValue(name, out var existing))
            return DeclarationOutcome.Fresh;

        return IsAlreadyHoisted(node, existing) ? DeclarationOutcome.AlreadyHoisted : DeclarationOutcome.Duplicate;
    }

    private void DeclareSymbol(Symbol symbol)
    {
        AddToLookup(symbol);
        AddDeclaration(symbol);
        if (LuauFactory.Keywords.Contains(symbol.Name))
            _diagnostics.Error(
                symbol.Declaration,
                InternalCodes.ReservedLuauKeyword,
                $"'{symbol.Name}' is a reserved Luau keyword and cannot be used as a declaration name."
            );

        if (IsDeclarationFile())
            symbol.IsGlobal = true;

        if (_context == ResolverContext.Ambient)
            symbol.IsAmbient = true;

        if (parserResult.Tree.File.IsIntrinsic)
            symbol.IsIntrinsic = true;
    }

    private void AddToLookup(Symbol symbol) => AddToLookup(symbol.Name, symbol);

    private void AddToLookup(string name, Symbol symbol)
    {
        var lookup = CurrentScope().Lookup(NamespaceOf(symbol));
        if (!lookup.TryGetValue(name, out var symbols))
            lookup[name] = symbols = [];

        symbols.Add(symbol);
    }

    private void AddDeclaration(Symbol symbol)
    {
        var id = symbol.Declaration.Id;
        if (!_allDeclarations.TryGetValue(id, out var symbols))
            _allDeclarations[id] = symbols = [];

        symbols.Add(symbol);
    }

    private void AddReference(Node node, Symbol symbol)
    {
        if (!_allReferences.TryGetValue(node.Id, out var symbols))
            _allReferences[node.Id] = symbols = [];

        symbols.Add(symbol);
        _semanticModel.MarkImportUsed(symbol);
        if (!node.File.IsIntrinsic)
            _semanticModel.NonIntrinsicReferenceNodes.Add(node.Id);
    }

    private Symbol? LookupTypeSymbol(string name) => LookupSymbol(name, SymbolNamespace.Type);
    private Symbol? LookupValueSymbol(string name) => LookupSymbol(name, SymbolNamespace.Value);
    private Symbol? LookupSymbol(string name, SymbolKind kind) => LookupSymbol(name, NamespaceOf(kind));

    /// <remarks>
    ///     Innermost scope first, so a local shadows an outer name rather than colliding with it. Only the
    ///     first symbol under a name is returned; a name bound more than once in one scope is an overload set,
    ///     which the type checker resolves from the call site rather than from here.
    /// </remarks>
    private Symbol? LookupSymbol(string name, SymbolNamespace symbolNamespace)
    {
        foreach (var scope in _scopes)
            if (scope.Lookup(symbolNamespace).TryGetValue(name, out var symbols))
                return symbols[0];

        return null;
    }

    private Symbol? LookupSymbolCurrentScope(string name, SymbolKind kind) =>
        CurrentScope().Lookup(NamespaceOf(kind)).TryGetValue(name, out var symbols) ? symbols[0] : null;

    private static bool IsAlreadyHoisted(Node node, List<Symbol> symbolsForName) => IsAlreadyHoisted<Symbol>(node, symbolsForName, out _);

    private static bool IsAlreadyHoisted<T>(Node node, List<Symbol> symbolsForName, [MaybeNullWhen(false)] out T hoistedSymbol)
        where T : Symbol
    {
        hoistedSymbol = symbolsForName.OfType<T>().FirstOrDefault(s => s.Declaration == node);
        return hoistedSymbol != null;
    }

    private static SymbolNamespace NamespaceOf(Symbol symbol) => symbol.IsTypeSymbol ? SymbolNamespace.Type : SymbolNamespace.Value;
    private static SymbolNamespace NamespaceOf(SymbolKind kind) => Symbol.IsTypeKind(kind) ? SymbolNamespace.Type : SymbolNamespace.Value;
}
