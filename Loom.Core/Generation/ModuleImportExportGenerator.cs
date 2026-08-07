using Loom.Core.Diagnostics;
using Loom.Core.Modules;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Core.Resolving.Symbols;
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

/// <summary>
///     Generates the require()/type-import statements a file needs and the export table/type-alias
///     statements it produces, plus resolves which local name a given export's value expression should
///     read through. Only depends on the semantic model and diagnostics - unlike the rest of
///     <see cref="LuauGenerator" /> it never visits AST nodes or touches generation state, so it lives as
///     its own class instead of another <c>LuauGenerator.*.cs</c> partial.
/// </summary>
internal sealed class ModuleImportExportGenerator(SemanticModel semanticModel, DiagnosticBag diagnostics, ModuleRequirePathResolver? moduleRequirePaths)
{
    private readonly Dictionary<SourceFile, string> _moduleLocals = [];

    private HashSet<string> TakenModuleLocalNames =>
        field ??=
        [
            ..semanticModel.Declarations.Values.SelectMany(symbols => symbols).Select(symbol => symbol.Name),
            ..semanticModel.ImportBindings.Select(binding => binding.LocalName), // aliases live only in the lookup
            LuauFactory.RuntimeImportName
        ];

    public List<LuauStatement> GenerateImports()
    {
        var bindingsByModule = semanticModel.ImportBindings
            .GroupBy(binding => binding.Module)
            .ToDictionary(bindings => bindings.Key, bindings => bindings.ToList());

        var statements = new List<LuauStatement>();
        foreach (var (module, specifier, localName) in GetRequiredModules())
        {
            var moduleName = localName ?? ReserveModuleLocalName(specifier);
            _moduleLocals[module] = moduleName;
            statements.Add(new ConstVariable(moduleName, null, LuauFactory.RequireCall(GetRequirePath(module, specifier))));

            var bindings = bindingsByModule.GetValueOrDefault(module, []);
            statements.AddRange(bindings.Where(binding => binding.RequiresModuleAtRuntime).Select(binding => GenerateValueImport(binding, moduleName)));
            statements.AddRange(bindings.Where(binding => binding.Symbol.IsTypeSymbol).Select(binding => GenerateTypeImport(binding, moduleName)));
            statements.AddRange(bindings.Where(IsSerializableInterface).Select(binding => GenerateSerializerImport(binding, moduleName)));
        }

        return statements;
    }

    private List<(SourceFile Module, string Specifier, string? LocalName)> GetRequiredModules()
    {
        var seen = new HashSet<SourceFile>();

        var modules = semanticModel.NamespaceImports
            .Where(binding => seen.Add(binding.Module))
            .Select((SourceFile, string, string?) (binding) => (binding.Module, binding.ModulePath, binding.LocalName))
            .ToList();

        modules.AddRange(
            semanticModel.ImportBindings.Where(binding => seen.Add(binding.Module))
                .Select((SourceFile, string, string?) (binding) => (binding.Module, binding.Import.ModulePath!, null))
        );

        modules.AddRange(
            semanticModel.Exports.Where(export => export.Module != null && seen.Add(export.Module))
                .Select((SourceFile, string, string?) (export) => (export.Module!, export.ModulePath!, null))
        );

        return modules;
    }

    /// <remarks>
    ///     Within one project the fallback is a relative require, which resolves correctly on its own because
    ///     the output tree mirrors the source tree — worth a warning, since it only works where require-by-string
    ///     is available. A require into a package has no such standing: the two projects' output sit wherever
    ///     the consumer's Rojo project puts them, so a relative path between them is a guess, and one that fails
    ///     at runtime rather than at build time. That is an error.
    /// </remarks>
    private string GetRequirePath(SourceFile module, string specifier)
    {
        var requirePath = moduleRequirePaths?.Resolve(semanticModel.Tree.File, module, specifier)
            ?? ModuleRequirePath.Fallback(ModuleRequirePathStatus.RojoMissing, specifier);

        if (requirePath.Package is { } package)
            diagnostics.Error(
                semanticModel.Tree,
                InternalCodes.ModuleNotFoundInRojo,
                $"Could not locate package '{package}' through the Rojo project; its compiled output at '{requirePath.PackagesDirectory}' is not mapped.",
                $"add a $path mapping to your default.project.json covering '{requirePath.PackagesDirectory}'"
            );
        else if (requirePath.Status == ModuleRequirePathStatus.NotFoundInRojo)
            diagnostics.Warn(
                semanticModel.Tree,
                InternalCodes.ModuleNotFoundInRojo,
                $"Could not locate module '{specifier}' through the Rojo project; falling back to a relative require.",
                "add a $path mapping to your default.project.json that includes the output directory"
            );

        return requirePath.Path;
    }

    private static ConstVariable GenerateValueImport(ImportBinding binding, string moduleName) =>
        new(binding.LocalName, null, new PropertyAccess(new Identifier(moduleName), [binding.ExportedName]));

    private bool IsSerializableInterface(ImportBinding binding) =>
        binding.Symbol is InterfaceSymbol interfaceSymbol && semanticModel.SerializationSchemas.ContainsKey(interfaceSymbol);

    /// <summary>
    ///     Binds an imported interface's codec under the same name the declaring module emitted it with,
    ///     so a serialization call in this file resolves to a local exactly as it would at home. An
    ///     interface is a type and carries no runtime binding of its own, so nothing else brings it in.
    /// </summary>
    private static ConstVariable GenerateSerializerImport(ImportBinding binding, string moduleName)
    {
        var name = SerializationEmitter.SerializerName(binding.ExportedName);
        return new ConstVariable(name, null, new PropertyAccess(new Identifier(moduleName), [name]));
    }

    private static TypeAlias GenerateTypeImport(ImportBinding binding, string moduleName) =>
        GenerateTypeAlias(binding.LocalName, binding.ExportedName, binding.Symbol, moduleName, false);

    public LuauExpression GenerateModuleMember(SourceFile module, string name) => new PropertyAccess(new Identifier(_moduleLocals[module]), [name]);

    public LuauExpression GenerateExportedValue(ExportBinding export) =>
        export.Module == null
            ? new Identifier(export.SourceName)
            : new PropertyAccess(new Identifier(_moduleLocals[export.Module]), [export.SourceName]);

    public void MarkListExportedTypes(List<LuauStatement> statements)
    {
        var typeAliasesByName = statements.OfType<TypeAlias>().ToDictionary(a => a.Name, a => a);
        var unaliasedTypeExports = semanticModel.Exports.Where(export => export is { IsReExport: false, Symbol.IsTypeSymbol: true }
            && export.Name == export.SourceName
        );

        foreach (var export in unaliasedTypeExports)
            if (typeAliasesByName.TryGetValue(export.Name, out var typeAlias))
                typeAlias.IsExported = true;
    }

    public IEnumerable<LuauStatement> GenerateExportedTypeAliases() =>
        semanticModel.Exports
            .Where(export => export.Symbol.IsTypeSymbol && (export.IsReExport || export.Name != export.SourceName))
            .Select(export => GenerateTypeAlias(
                    export.Name,
                    export.SourceName,
                    export.Symbol,
                    export.Module == null ? null : _moduleLocals[export.Module],
                    true
                )
            );

    private static TypeAlias GenerateTypeAlias(string name, string sourceName, Symbol symbol, string? moduleName, bool isExported)
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

    /// <remarks>
    ///     The last segment names the module, which is what a reader of the output is looking for. A package
    ///     specifier's earlier segments are tried before numbering when that name is taken, so an import of
    ///     <c>"math/vector"</c> beside a local <c>vector</c> reads as <c>math_vector</c> rather than
    ///     <c>vector_1</c> — the numbering is there for names no part of the specifier can tell apart.
    /// </remarks>
    private string ReserveModuleLocalName(string specifier)
    {
        var segments = specifier.Split('/', StringSplitOptions.RemoveEmptyEntries).Where(segment => segment is not (".." or ".")).ToList();
        var name = ToLocalName(segments.Count == 0 ? "module" : segments[^1]);
        if (TakenModuleLocalNames.Add(name))
            return name;

        for (var from = segments.Count - 2; from >= 0; from--)
        {
            var qualified = ToLocalName(string.Join('_', segments.Skip(from)));
            if (TakenModuleLocalNames.Add(qualified))
                return qualified;
        }

        var unique = name;
        for (var suffix = 1; !TakenModuleLocalNames.Add(unique); suffix++)
            unique = name + '_' + suffix;

        return unique;
    }

    /// <summary>Turns a specifier segment into something Luau will accept as a local name.</summary>
    private static string ToLocalName(string segment)
    {
        var sanitized = new string(segment.Select(character => char.IsLetterOrDigit(character) || character == '_' ? character : '_').ToArray());
        var name = sanitized.Length == 0 || char.IsDigit(sanitized[0]) ? '_' + sanitized : sanitized;

        return LuauFactory.Keywords.Contains(name) ? '_' + name : name;
    }
}