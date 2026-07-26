using System.Diagnostics.CodeAnalysis;
using Loom.Core.Diagnostics;
using Loom.Core.Parsing;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving.Symbols;
using Loom.Core.Text;
using Loom.Core.TypeChecking;
using Loom.Core.TypeChecking.Types;
using Loom.Luau;
using Attribute = Loom.Core.Parsing.AST.Attribute;
using FunctionType = Loom.Core.Parsing.AST.FunctionType;
using Type = Loom.Core.TypeChecking.Types.Type;
using TypeParameter = Loom.Core.Parsing.AST.TypeParameter;

namespace Loom.Core.Resolving;

public sealed partial class Resolver
{
    public override bool VisitExportDeclaration(ExportDeclaration export)
    {
        if (_scopes.Count > 1)
        {
            _diagnostics.Error(
                export,
                InternalCodes.ExportOutsideModuleScope,
                "Declarations can only be exported at the top level of a module.",
                "move the 'export' declaration out of the enclosing block"
            );

            return false;
        }

        if (export.Declaration is VariableDeclaration { Keyword.Kind: SyntaxKind.MutKeyword })
        {
            _diagnostics.Error(
                export,
                InternalCodes.CannotExportMutable,
                "Mutable variables cannot be exported.",
                "use 'let' instead of 'mut'"
            );

            return false;
        }

        if (!Visit(export.Declaration))
            return false;

        foreach (var symbol in _semanticModel.GetDeclarationSymbols(export.Declaration))
            AddExport(export.Declaration, ExportBinding.OfDeclaration(symbol));

        return true;
    }

    public override bool VisitExportList(ExportList export)
    {
        if (_scopes.Count > 1)
        {
            _diagnostics.Error(
                export,
                InternalCodes.ExportOutsideModuleScope,
                "Declarations can only be exported at the top level of a module.",
                "move the 'export' declaration out of the enclosing block"
            );

            return false;
        }

        if (!export.IsReExport)
        {
            foreach (var specifier in export.Specifiers)
                ResolveLocalExport(export, specifier);

            return true;
        }

        var module = compilationUnit.ModuleGraph?.GetResolvedModule(export);
        if (module == null)
            return true; // an unresolvable specifier was already reported while building the module graph

        if (!compilationUnit.AnalyzedModules.TryGetValue(module, out var moduleModel))
            return true; // only reachable through a dependency cycle, which the module graph reported

        foreach (var specifier in export.Specifiers)
            ResolveReExport(export, specifier, module, moduleModel);

        return true;
    }

    public override bool VisitNamespaceImport(NamespaceImport import)
    {
        if (_scopes.Count > 1)
        {
            _diagnostics.Error(
                import,
                InternalCodes.ImportOutsideModuleScope,
                "Modules can only be imported at the top level of a module.",
                "move the 'import' declaration out of the enclosing block"
            );

            return false;
        }

        var module = compilationUnit.ModuleGraph?.GetResolvedModule(import);
        if (module == null)
            return true; // an unresolvable specifier was already reported while building the module graph

        if (!compilationUnit.AnalyzedModules.TryGetValue(module, out var moduleModel))
            return true; // only reachable through a dependency cycle, which the module graph reported

        var name = import.Name.Text;
        if (HasDuplicateSymbol(import, name, true, $"Variable '{name}' is already declared in this scope."))
            return true;

        // the symbol stands for the required table, so unlike a named import it is declared on this node
        var symbol = new Symbol(import, SymbolKind.Variable, name);
        DeclareSymbol(symbol);
        _semanticModel.AddNamespaceImport(new NamespaceImportBinding(import, symbol, module));
        _semanticModel.TypeSolver.SetType(import, GetNamespaceType(moduleModel));

        return true;
    }

    /// <summary>
    ///     The type of a namespace import: an object whose properties are the module's runtime exports, so
    ///     member access on it type-checks against what the module actually returns.
    /// </summary>
    private static Type GetNamespaceType(SemanticModel moduleModel) =>
        new ObjectType(
            null,
            moduleModel.Exports
                .FindAll(export => export.EmitsRuntimeBinding)
                .ConvertAll(export => new ObjectProperty(false, export.Name, moduleModel.GetType(export.Symbol.Declaration)))
        );

    /// <summary>Exports a name the module already declares, without introducing a new binding.</summary>
    private void ResolveLocalExport(ExportList export, ExportSpecifier specifier)
    {
        var name = specifier.Name.Text;
        var typeSymbol = LookupTypeSymbol(name);
        var valueSymbol = export.IsTypeOnly ? null : LookupValueSymbol(name);
        if (typeSymbol == null && valueSymbol == null)
        {
            _diagnostics.Error(
                specifier,
                export.IsTypeOnly ? InternalCodes.TypeOnlyExportOfValue : InternalCodes.CannotFindSymbol,
                export.IsTypeOnly && LookupValueSymbol(name) != null
                    ? $"'{name}' is a value, not a type."
                    : $"Cannot find symbol '{name}'.",
                export.IsTypeOnly && LookupValueSymbol(name) != null ? "remove 'type' from the export" : null
            );

            return;
        }

        foreach (var symbol in new[] { valueSymbol, typeSymbol }.OfType<Symbol>())
        {
            AddReference(specifier, symbol);
            AddExport(specifier, new ExportBinding(specifier.ExportedName.Text, name, symbol, export));
        }
    }

    /// <summary>Forwards another module's export without binding it in this module's scope.</summary>
    private void ResolveReExport(ExportList export, ExportSpecifier specifier, SourceFile module, SemanticModel moduleModel)
    {
        var name = specifier.Name.Text;
        var exports = moduleModel.FindExports(name);
        if (exports.Count == 0)
        {
            _diagnostics.Error(specifier, InternalCodes.NoExportedMember, $"Module '{module.Name}' does not export '{name}'.");
            return;
        }

        if (export.IsTypeOnly)
        {
            var typeExports = exports.FindAll(binding => binding.Symbol.IsTypeSymbol);
            if (typeExports.Count == 0)
            {
                _diagnostics.Error(
                    specifier,
                    InternalCodes.TypeOnlyExportOfValue,
                    $"'{name}' is a value, not a type.",
                    "remove 'type' from the export"
                );

                return;
            }

            exports = typeExports;
        }

        foreach (var binding in exports)
            AddExport(specifier, new ExportBinding(specifier.ExportedName.Text, name, binding.Symbol, export, module));
    }

    private void AddExport(Node node, ExportBinding binding)
    {
        var existing = _semanticModel.FindExports(binding.Name);
        if (existing.Exists(other => other.Symbol.IsTypeSymbol == binding.Symbol.IsTypeSymbol))
        {
            _diagnostics.Error(node, InternalCodes.DuplicateExport, $"'{binding.Name}' is already exported.");
            return;
        }

        _semanticModel.AddExport(binding);
    }

    public override bool VisitImportDeclaration(ImportDeclaration import)
    {
        if (_scopes.Count > 1)
        {
            _diagnostics.Error(
                import,
                InternalCodes.ImportOutsideModuleScope,
                "Modules can only be imported at the top level of a module.",
                "move the 'import' declaration out of the enclosing block"
            );

            return false;
        }

        var module = compilationUnit.ModuleGraph?.GetResolvedModule(import);
        if (module == null)
            return true; // an unresolvable specifier was already reported while building the module graph

        if (!compilationUnit.AnalyzedModules.TryGetValue(module, out var moduleModel))
            return true; // only reachable through a dependency cycle, which the module graph reported

        var localNames = new HashSet<string>();
        return import.Specifiers.All(specifier => ResolveImportSpecifier(import, specifier, module, moduleModel, localNames));
    }

    private bool ResolveImportSpecifier(
        ImportDeclaration import,
        ImportSpecifier specifier,
        SourceFile module,
        SemanticModel moduleModel,
        HashSet<string> localNames)
    {
        var name = specifier.Name.Text;
        var localName = specifier.LocalName.Text;
        var exports = moduleModel.FindExports(name);
        if (exports.Count == 0)
        {
            var exported = moduleModel.Exports.Select(symbol => symbol.Name).Distinct().ToList();
            _diagnostics.Error(
                specifier,
                InternalCodes.NoExportedMember,
                $"Module '{module.Name}' does not export '{name}'.",
                exported.Count > 0 ? $"it exports {string.Join(", ", exported.Select(n => $"'{n}'"))}" : "it exports nothing"
            );

            return false;
        }

        if (!localNames.Add(localName))
        {
            _diagnostics.Error(specifier, InternalCodes.DuplicateImport, $"'{localName}' is imported more than once.");
            return false;
        }

        if (!import.IsTypeOnly)
            return exports.All(export => DeclareImportedSymbol(import, specifier, export.Symbol, module, moduleModel));

        var typeExports = exports.FindAll(export => export.Symbol.IsTypeSymbol);
        if (typeExports.Count != 0)
            return typeExports.All(export => DeclareImportedSymbol(import, specifier, export.Symbol, module, moduleModel));

        _diagnostics.Error(
            specifier,
            InternalCodes.TypeOnlyImportOfValue,
            $"'{name}' is a value, not a type.",
            "remove 'type' from the import"
        );

        return false;
    }

    /// <summary>
    ///     Binds the exporting module's own symbol instance into this scope under the local name. The instance
    ///     is reused rather than copied — the same thing <see cref="DeclareGlobalSymbols" /> does for globals —
    ///     so that an imported interface still resolves to an <see cref="InterfaceSymbol" />.
    /// </summary>
    private bool DeclareImportedSymbol(
        ImportDeclaration import,
        ImportSpecifier specifier,
        Symbol export,
        SourceFile module,
        SemanticModel moduleModel)
    {
        var localName = specifier.LocalName.Text;
        var duplicateKind = export.IsTypeSymbol ? "Type" : "Variable";
        if (HasDuplicateSymbol(specifier, localName, !export.IsTypeSymbol, $"{duplicateKind} '{localName}' is already declared in this scope."))
            return false;

        if (LuauFactory.Keywords.Contains(localName))
        {
            _diagnostics.Error(
                specifier,
                InternalCodes.ReservedLuauKeyword,
                $"'{localName}' is a reserved Luau keyword and cannot be used as a declaration name."
            );

            return false;
        }

        AddToLookup(localName, export);
        AddDeclaration(export);
        _semanticModel.AddImportBinding(new ImportBinding(import, specifier, export, module));
        _semanticModel.TypeSolver.SetType(export.Declaration, moduleModel.GetType(export.Declaration));
        return true;
    }

    /// <remarks>
    ///     One specifier can produce a binding per namespace, so an interface referenced only as a type still
    ///     counts as used. Runs once the whole tree is resolved, since a name may be used above its import.
    /// </remarks>
    private void ReportUnusedImports()
    {
        foreach (var bindings in _semanticModel.ImportBindings.GroupBy(binding => binding.Specifier))
        {
            if (bindings.Any(binding => binding.IsUsed))
                continue;

            var binding = bindings.First();
            _diagnostics.Warn(
                binding.Specifier,
                InternalCodes.UnusedImport,
                $"'{binding.LocalName}' is imported but never used.",
                "remove it from the import clause"
            );
        }

        foreach (var binding in _semanticModel.NamespaceImports.Where(binding => !binding.IsUsed))
            _diagnostics.Warn(
                binding.Import,
                InternalCodes.UnusedImport,
                $"'{binding.LocalName}' is imported but never used.",
                "remove the import"
            );
    }
}
