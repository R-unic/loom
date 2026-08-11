using System.Collections.Concurrent;
using System.Reflection;
using Loom.Config;
using Loom.Core.Pipeline;
using Loom.Core.Resolving;
using Loom.Core.Resolving.Symbols;
using Loom.Core.Text;
using Loom.Core.TypeChecking.Types;
using IWithAttributes = Loom.Core.Parsing.AST.IWithAttributes;
using PrimitiveType = Loom.Core.TypeChecking.Types.PrimitiveType;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.Core.TypeChecking;

public static class Intrinsics
{
    public static readonly TupleMarkerType TupleMarker = new();
    
    private const string CoreFileName = "loom.loom";
    private const string RuntimeFileName = "runtime.loom";
    private const string PluginSecurityFileName = "PluginSecurity.loom";
    private const string NonPluginRuntimeFileName = "None.loom";
    private const string IntrinsicResourcePrefix = "Intrinsic/";

    [ThreadStatic] private static bool _isBootstrapping;
    private static readonly ConcurrentDictionary<ProjectType, HashSet<(Symbol, Type)>> _cache = new();
    private static readonly Assembly _resourceAssembly = typeof(Intrinsics).Assembly;

    public static readonly InterfaceType Range = new(
        "Range",
        [],
        new ObjectType(
            null,
            [
                new ObjectProperty(false, "minimum", PrimitiveType.Number),
                new ObjectProperty(false, "maximum", PrimitiveType.Number),
                new ObjectProperty(false, "length", PrimitiveType.Number),
                new ObjectProperty(false, "clamp", new FunctionType([], [PrimitiveType.Number], PrimitiveType.Number))
            ]
        )
    );

    public static readonly InterfaceType StringMembers = new(
        "string",
        [],
        new ObjectType(
            null,
            [
                new ObjectProperty(false, "length", PrimitiveType.Number),
                new ObjectProperty(false, "upper", new FunctionType([], [], PrimitiveType.String)),
                new ObjectProperty(false, "lower", new FunctionType([], [], PrimitiveType.String)),
                new ObjectProperty(false, "trim", new FunctionType([], [], PrimitiveType.String)),
                new ObjectProperty(false, "replace", new FunctionType([], [PrimitiveType.String, PrimitiveType.String], PrimitiveType.String)),
                new ObjectProperty(false, "reverse", new FunctionType([], [], PrimitiveType.String)),
                new ObjectProperty(false, "repeat", new FunctionType([], [PrimitiveType.Number], PrimitiveType.String)),
                new ObjectProperty(false, "split", new FunctionType([], [new OptionalType(PrimitiveType.String)], new ArrayType(PrimitiveType.String, true))),
                new ObjectProperty(false, "has", new FunctionType([], [PrimitiveType.String], PrimitiveType.Bool)),
                new ObjectProperty(false, "starts_with", new FunctionType([], [PrimitiveType.String], PrimitiveType.Bool)),
                new ObjectProperty(false, "ends_with", new FunctionType([], [PrimitiveType.String], PrimitiveType.Bool)),
                new ObjectProperty(false, "byte", new FunctionType([], [new OptionalType(PrimitiveType.Number)], new OptionalType(PrimitiveType.Number)))
            ]
        )
    );

    /// <summary>
    ///     The <c>Set&lt;T&gt;</c> definition from <c>loom.loom</c>, published once the intrinsics have
    ///     compiled so <see cref="ArrayType" /> can name it in <c>to_set</c>'s return type.
    ///     <para>
    ///         An array's members are built in C# rather than declared, so unlike every other reference to an
    ///         intrinsic type there is no semantic model in reach to look this up through. It is null until
    ///         the intrinsics finish compiling - an array built during that bootstrap simply has no
    ///         <c>to_set</c>, which is fine because no intrinsic source calls it. First writer wins: every
    ///         project type includes <c>loom.loom</c>, so the definitions are interchangeable.
    ///     </para>
    /// </summary>
    internal static GenericType? SetDefinition;

    public static HashSet<(Symbol, Type)> Register(SemanticModel model, CompilationUnit injectInto)
    {
        var projectType = injectInto.Config.ProjectType;

        if (!_cache.TryGetValue(projectType, out var intrinsics))
        {
            intrinsics = CompileIntrinsics(projectType);
            if (intrinsics.Count > 0)
                _cache.TryAdd(projectType, intrinsics);
        }

        foreach (var (symbol, type) in intrinsics)
            model.TypeSolver.SetType(symbol.Declaration, type);

        return intrinsics;
    }

    private static HashSet<(Symbol, Type)> CompileIntrinsics(ProjectType projectType)
    {
        if (_isBootstrapping)
            return [];

        _isBootstrapping = true;
        try
        {
            var compilationUnit = CreateCompilationUnit();
            var files = compilationUnit.SourceFiles.Where(file => IsIncludedFor(file, projectType)).ToList();
            foreach (var file in files)
                file.IsIntrinsic = true;

            // loom.loom first, then runtime.loom: both publish into Globals, which is the only
            // channel intrinsic files have for reaching each other while ambient injection is off.
            // The generated Roblox definitions name Result and RobloxError - every fallible API
            // method returns one - and those live in runtime.loom, so it has to be globalised
            // before the generated files compile, or their return types resolve to 'never'.
            var coreFiles = new[] { CoreFileName, RuntimeFileName }
                .Select(name => files.Find(file => file.Name == name))
                .OfType<SourceFile>()
                .ToList();

            var compiledFiles = new List<CompiledFile>();
            foreach (var coreFile in coreFiles)
                if (CompileCoreFile(compilationUnit, coreFile) is { } compiledCoreFile)
                    compiledFiles.Add(compiledCoreFile);

            foreach (var file in files)
                if (!coreFiles.Contains(file) && compilationUnit.Compile(file) is { } compiledFile)
                    compiledFiles.Add(compiledFile);

            return CollectDeclaredSymbols(compiledFiles);
        }
        finally
        {
            _isBootstrapping = false;
        }
    }

    /// <summary>
    ///     Intrinsic sources are embedded resources rather than a disk directory: reading them via
    ///     <see cref="FileManager.LoadDirectory" /> and a path relative to <see cref="AppContext.BaseDirectory" />
    ///     silently returns nothing in hosts with no real filesystem, such as the Blazor WebAssembly
    ///     playground, which left every intrinsic unavailable there.
    /// </summary>
    private static CompilationUnit CreateCompilationUnit()
    {
        var config = new LoomConfig
        {
            ProjectType = ProjectType.Library, NoEmit = true, Files = new FilesConfig { SourceDirectory = "Intrinsic" }
        };

        var compilationUnit = new CompilationUnit(config);
        compilationUnit.Roots.Entry.Files.AddRange(LoadEmbeddedIntrinsicFiles());
        return compilationUnit;
    }

    private static IEnumerable<SourceFile> LoadEmbeddedIntrinsicFiles()
    {
        foreach (var resourceName in _resourceAssembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(IntrinsicResourcePrefix, StringComparison.Ordinal)) continue;

            using var stream = _resourceAssembly.GetManifestResourceStream(resourceName)!;
            using var reader = new StreamReader(stream);
            yield return new SourceFile(resourceName, reader.ReadToEnd());
        }
    }

    private static bool IsIncludedFor(SourceFile file, ProjectType projectType) =>
        file.Name switch
        {
            PluginSecurityFileName => projectType == ProjectType.Plugin,
            NonPluginRuntimeFileName => projectType != ProjectType.Plugin,
            _ => true
        };

    /// <summary>
    ///     loom.loom declares luau_name, luau_method, and override, which every other intrinsic file's
    ///     attributes depend on, so it must compile - and have its declarations copied into
    ///     <see cref="CompilationUnit.Globals" /> - before any other intrinsic file does. Globals is the
    ///     same channel a regular project's own .d.loom files use to reach every other file in the unit;
    ///     it is the only channel intrinsic files have for referencing each other, since ambient intrinsic
    ///     injection stays off for this whole bootstrap (see <see cref="_isBootstrapping" />).
    /// </summary>
    private static CompiledFile? CompileCoreFile(CompilationUnit compilationUnit, SourceFile coreFile)
    {
        var compiled = compilationUnit.Compile(coreFile);
        if (compiled == null)
            return null;

        foreach (var symbol in compiled.Tree.Statements.SelectMany(statement => compiled.SemanticModel.GetDeclarationSymbols(statement)))
            compilationUnit.Globals.Declare(compiled.Root, symbol, compiled.SemanticModel.GetType(symbol.Declaration));

        return compiled;
    }

    private static HashSet<(Symbol, Type)> CollectDeclaredSymbols(IEnumerable<CompiledFile> compiledFiles)
    {
        var intrinsicSymbols = new HashSet<(Symbol, Type)>();
        foreach (var compiledFile in compiledFiles)
            foreach (var symbol in compiledFile.Tree.Statements.SelectMany(statement => compiledFile.SemanticModel.GetDeclarationSymbols(statement)))
            {
                symbol.IsIntrinsic = true;
                symbol.IsGlobal = true;
                symbol.AttributeUsageFlags = ResolveAttributeUsageFlags(compiledFile.SemanticModel, symbol);
                var type = compiledFile.SemanticModel.GetType(symbol.Declaration);
                intrinsicSymbols.Add((symbol, type));

                if (symbol is { Name: "Set", IsTypeSymbol: true } && type is GenericType setDefinition)
                    Interlocked.CompareExchange(ref SetDefinition, setDefinition, null);
            }

        return intrinsicSymbols;
    }

    private static int? ResolveAttributeUsageFlags(SemanticModel semanticModel, Symbol symbol)
    {
        if (symbol.Declaration is not IWithAttributes { Attributes: { } declaredAttributes })
            return null;

        var usageAttribute = declaredAttributes.AttributeList.Find(
            a => a.Expression.Tokens.LastOrDefault(t => t.Kind == SyntaxKind.Identifier)?.Text == "attribute_usage"
        );

        if (usageAttribute?.Arguments.ArgumentList is not [var flagsExpression] || semanticModel.GetConstantValue(flagsExpression) is not double flagsValue)
            return null;

        return (int)flagsValue;
    }
}