using System.Diagnostics.CodeAnalysis;
using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving.Symbols;
using Loom.Luau;

namespace Loom.Core.Resolving;

public sealed partial class Resolver
{
    private bool DeclareVariable(NamedDeclaration node, bool isMutable = false) => DeclareVariable(node, node.Name.Text, isMutable);

    private bool DeclareVariable(Node node, string name, bool isMutable = false) => DeclareVariable(node, new VariableSymbol(node, name, isMutable));

    private bool DeclareVariable(Node node, Symbol symbol)
    {
        if (HasDuplicateSymbol(node, symbol.Name, true, $"Variable '{symbol.Name}' is already declared in this scope."))
            return false;

        DeclareSymbol(symbol);
        return true;
    }

    private bool DeclareType(NamedDeclaration node) => DeclareType(node, new TypeAliasSymbol(node, node.Name.Text));

    private bool DeclareType(NamedDeclaration node, TypeSymbol symbol)
    {
        if (HasDuplicateSymbol(node, false, $"Type '{node.Name.Text}' is already declared in this scope."))
            return false;

        DeclareSymbol(symbol);
        return true;
    }

    private bool HasDuplicateSymbol(NamedDeclaration node, bool isVariable, string error) => HasDuplicateSymbol(node, node.Name.Text, isVariable, error);

    private bool HasDuplicateSymbol(Node node, string name, bool isVariable, string error)
    {
        var scope = CurrentScope();
        var lookup = isVariable ? scope.VariableLookup : scope.TypeLookup;
        if (!lookup.TryGetValue(name, out var existing) || IsAlreadyHoisted(node, existing))
            return false;

        _diagnostics.Error(node, InternalCodes.DuplicateName, error);
        return true;
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
        var scope = CurrentScope();
        var lookup = symbol.IsTypeSymbol ? scope.TypeLookup : scope.VariableLookup;
        if (!lookup.ContainsKey(name))
            lookup[name] = [];

        lookup[name].Add(symbol);
    }

    private void AddDeclaration(Symbol symbol)
    {
        var id = symbol.Declaration.Id;
        if (!_allDeclarations.ContainsKey(id))
            _allDeclarations[id] = [];

        _allDeclarations[id].Add(symbol);
    }

    private void AddReference(Node node, Symbol symbol)
    {
        if (!_allReferences.ContainsKey(node.Id))
            _allReferences[node.Id] = [];

        _allReferences[node.Id].Add(symbol);
        _semanticModel.MarkImportUsed(symbol);
        if (!node.File.IsIntrinsic)
            _semanticModel.NonIntrinsicReferenceNodes.Add(node.Id);
    }

    private Symbol? LookupTypeSymbol(string name) => LookupSymbol(name, true);
    private Symbol? LookupValueSymbol(string name) => LookupSymbol(name, false);
    private Symbol? LookupSymbol(string name, SymbolKind kind) => LookupSymbol(name, Symbol.IsTypeKind(kind));

    private Symbol? LookupSymbol(string name, bool isType)
    {
        foreach (var scope in _scopes)
        {
            var lookup = isType ? scope.TypeLookup : scope.VariableLookup;
            if (lookup.TryGetValue(name, out var symbols))
                return symbols[0];
        }

        return null;
    }

    private Symbol? LookupSymbolCurrentScope(string name, SymbolKind kind)
    {
        var lookup = GetLookup(kind, CurrentScope());
        return !lookup.TryGetValue(name, out var symbols) ? null : symbols.First();
    }

    private static bool IsAlreadyHoisted(Node node, List<Symbol> symbolsForName) => IsAlreadyHoisted<Symbol>(node, symbolsForName, out _);

    private static bool IsAlreadyHoisted<T>(Node node, List<Symbol> symbolsForName, [MaybeNullWhen(false)] out T hoistedSymbol)
        where T : Symbol
    {
        hoistedSymbol = symbolsForName.OfType<T>().FirstOrDefault(s => s.Declaration == node);
        return hoistedSymbol != null;
    }

    private static SymbolLookup GetLookup(SymbolKind kind, ResolverScope scope) => Symbol.IsTypeKind(kind) ? scope.TypeLookup : scope.VariableLookup;
}
