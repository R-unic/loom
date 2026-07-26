using System.Diagnostics.CodeAnalysis;
using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Luau;

namespace Loom.Core.Resolving;

public sealed partial class Resolver
{
    private bool DeclareVariable(NamedDeclaration node, SymbolKind symbolKind, bool isMutable = false) =>
        DeclareVariable(node, node.Name.Text, symbolKind, isMutable);

    private bool DeclareVariable(Node node, string name, SymbolKind symbolKind, bool isMutable = false) =>
        DeclareVariable(node, new Symbol(node, symbolKind, name, isMutable));

    private bool DeclareVariable(Node node, Symbol symbol)
    {
        if (HasDuplicateSymbol(node, symbol.Name, true, $"Variable '{symbol.Name}' is already declared in this scope."))
            return false;

        DeclareSymbol(symbol);
        return true;
    }

    private bool DeclareType(NamedDeclaration node, SymbolKind symbolKind = SymbolKind.Type)
    {
        var name = node.Name.Text;
        if (HasDuplicateSymbol(node, false, $"Type '{name}' is already declared in this scope."))
            return false;

        var symbol = new Symbol(node, symbolKind, name);
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

        if (_semanticModel.EmitDebugDiagnostics)
            _diagnostics.Debug(symbol.Declaration, DescribeDeclaration(symbol));
    }

    private static string DescribeDeclaration(Symbol symbol)
    {
        var flags = new List<string>();
        if (symbol.IsGlobal) flags.Add("global");
        if (symbol.IsAmbient) flags.Add("ambient");
        if (symbol.IsIntrinsic) flags.Add("intrinsic");

        var suffix = flags.Count > 0 ? $" [{string.Join(", ", flags)}]" : "";
        return $"Declared '{symbol.Name}' ({symbol.Kind}){suffix}";
    }

    private void AddToLookup(Symbol symbol) => AddToLookup(symbol.Name, symbol);

    private void AddToLookup(string name, Symbol symbol)
    {
        var scope = CurrentScope();
        var lookup = GetLookup(symbol.Kind, scope);
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
        var lookups = _scopes.Select(scope => isType ? scope.TypeLookup : scope.VariableLookup);
        foreach (var lookup in lookups)
        {
            if (!lookup.TryGetValue(name, out var symbols)) continue;
            return symbols.First();
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
