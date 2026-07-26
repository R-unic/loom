using Loom.Core.Diagnostics;
using Loom.Core.Modules;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Core.Text;
using Loom.Luau;
using Loom.Luau.AST;
using Identifier = Loom.Luau.AST.Identifier;
using PropertyAccess = Loom.Luau.AST.PropertyAccess;
using TypeAlias = Loom.Luau.AST.TypeAlias;
using TypeName = Loom.Luau.AST.TypeName;
using TypeParameter = Loom.Luau.AST.TypeParameter;
using TypeParameters = Loom.Luau.AST.TypeParameters;

namespace Loom.Core.Generation;

public sealed partial class LuauGenerator
{
    private readonly Dictionary<SourceFile, string> _moduleLocals = [];

    private HashSet<string> TakenModuleLocalNames =>
        field ??=
        [
            .._semanticModel.Declarations.Values.SelectMany(symbols => symbols).Select(symbol => symbol.Name),
            .._semanticModel.ImportBindings.Select(binding => binding.LocalName), // aliases live only in the lookup
            LuauFactory.RuntimeImportName
        ];

    private List<LuauStatement> GenerateModuleImports()
    {
        var statements = new List<LuauStatement>();
        foreach (var (module, specifier, localName) in GetRequiredModules())
        {
            var moduleName = localName ?? ReserveModuleLocalName(specifier);
            _moduleLocals[module] = moduleName;
            statements.Add(new ConstVariable(moduleName, null, LuauFactory.RequireCall(GetRequirePath(module, specifier))));

            var bindings = _semanticModel.ImportBindings.FindAll(binding => binding.Module == module);
            statements.AddRange(bindings.FindAll(binding => binding.RequiresModuleAtRuntime).ConvertAll(binding => GenerateValueImport(binding, moduleName)));

            statements.AddRange(bindings.FindAll(binding => binding.Symbol.IsTypeSymbol).ConvertAll(binding => GenerateTypeImport(binding, moduleName)));
        }

        return statements;
    }

    private List<(SourceFile Module, string Specifier, string? LocalName)> GetRequiredModules()
    {
        var modules = new List<(SourceFile, string, string?)>();
        var seen = new HashSet<SourceFile>();

        foreach (var binding in _semanticModel.NamespaceImports.Where(binding => seen.Add(binding.Module)))
            modules.Add((binding.Module, binding.ModulePath, binding.LocalName));

        foreach (var binding in _semanticModel.ImportBindings.Where(binding => seen.Add(binding.Module)))
            modules.Add((binding.Module, binding.Import.ModulePath!, null));

        foreach (var export in _semanticModel.Exports.Where(export => export.Module != null && seen.Add(export.Module)))
            modules.Add((export.Module!, export.ModulePath!, null));

        return modules;
    }

    private string GetRequirePath(SourceFile module, string specifier)
    {
        var requirePath = _moduleRequirePaths?.Resolve(module, specifier)
            ?? ModuleRequirePath.Fallback(ModuleRequirePathStatus.RojoMissing, specifier);

        if (requirePath.Status == ModuleRequirePathStatus.NotFoundInRojo)
            _diagnostics.Warn(
                _semanticModel.Tree,
                InternalCodes.ModuleNotFoundInRojo,
                $"Could not locate module '{specifier}' through the Rojo project; falling back to a relative require.",
                "add a $path mapping to your default.project.json that includes the output directory"
            );

        return requirePath.Path;
    }

    private static LuauStatement GenerateValueImport(ImportBinding binding, string moduleName) =>
        new ConstVariable(binding.LocalName, null, new PropertyAccess(new Identifier(moduleName), [binding.ExportedName]));

    private static LuauStatement GenerateTypeImport(ImportBinding binding, string moduleName) =>
        GenerateTypeAlias(binding.LocalName, binding.ExportedName, binding.Symbol, moduleName, false);

    private LuauExpression GenerateExportedValue(ExportBinding export) =>
        export.Module == null
            ? new Identifier(export.SourceName)
            : new PropertyAccess(new Identifier(_moduleLocals[export.Module]), [export.SourceName]);

    private void MarkListExportedTypes(List<LuauStatement> statements)
    {
        foreach (var export in _semanticModel.Exports)
        {
            if (export.IsReExport || !export.Symbol.IsTypeSymbol || export.Name != export.SourceName)
                continue;

            if (statements.OfType<TypeAlias>().FirstOrDefault(alias => alias.Name == export.Name) is { } typeAlias)
                typeAlias.IsExported = true;
        }
    }

    private List<LuauStatement> GenerateExportedTypeAliases() =>
        _semanticModel.Exports
            .FindAll(export => export.Symbol.IsTypeSymbol && (export.IsReExport || export.Name != export.SourceName))
            .ConvertAll(export => GenerateTypeAlias(
                    export.Name,
                    export.SourceName,
                    export.Symbol,
                    export.Module == null ? null : _moduleLocals[export.Module],
                    true
                )
            );

    private static LuauStatement GenerateTypeAlias(string name, string sourceName, Symbol symbol, string? moduleName, bool isExported)
    {
        var parameterNames = symbol.Declaration is GenericNamedDeclaration { TypeParameters: { } typeParameters }
            ? typeParameters.ParameterList.ConvertAll(parameter => parameter.Name.Text)
            : [];

        var source = new TypeName(sourceName, parameterNames.ConvertAll(LuauType (parameter) => new TypeName(parameter)));
        return new TypeAlias(
            name,
            new TypeParameters(parameterNames.ConvertAll(parameter => new TypeParameter(parameter))),
            moduleName == null ? source : new QualifiedTypeName([moduleName], source)
        ) { IsExported = isExported };
    }

    private string ReserveModuleLocalName(string specifier)
    {
        var segment = specifier.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault(segment => segment != "..");
        var sanitized = new string((segment ?? "module").Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
        var name = sanitized.Length == 0 || char.IsDigit(sanitized[0]) ? '_' + sanitized : sanitized;
        if (LuauFactory.Keywords.Contains(name))
            name = '_' + name;

        var unique = name;
        for (var suffix = 1; !TakenModuleLocalNames.Add(unique); suffix++)
            unique = name + '_' + suffix;

        return unique;
    }
}