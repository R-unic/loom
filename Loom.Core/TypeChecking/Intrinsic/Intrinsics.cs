using System.Collections.Concurrent;
using System.Reflection;
using Loom.Config;
using Loom.Core.Pipeline;
using Loom.Core.Resolving;
using Loom.Core.Resolving.Symbols;
using Loom.Core.Text;
using Loom.Core.TypeChecking.Types;
using IWithAttributes = Loom.Core.Parsing.AST.IWithAttributes;
using NodeId = Loom.Core.Parsing.AST.NodeId;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.Core.TypeChecking.Intrinsic;

public static class Intrinsics
{
    private const string CoreFileName = "loom.loom";
    private const string RuntimeFileName = "runtime.loom";
    private const string PluginSecurityFileName = "PluginSecurity.loom";
    private const string NonPluginRuntimeFileName = "None.loom";
    private const string IntrinsicResourcePrefix = "Intrinsic/";

    [ThreadStatic] private static bool _isBootstrapping;
    private static readonly ConcurrentDictionary<ProjectType, Lazy<IntrinsicRegistration>> _cache = new();
    private static readonly Assembly _resourceAssembly = typeof(Intrinsics).Assembly;

    /// <remarks>
    ///     Nothing is injected while the intrinsics are themselves being compiled. An intrinsic file reaches
    ///     the others through <see cref="CompilationUnit.Globals" /> during that bootstrap, and injecting
    ///     here as well would put a previous compilation's symbols into the scope of the file that declares
    ///     those very names - one name in one scope standing for two declarations, of which only the first
    ///     is ever reachable. The guard has to be here rather than only in <see cref="CompileIntrinsics" />:
    ///     a cached project type skips compiling but was still being injected.
    /// </remarks>
    /// <remarks>
    ///     <para>
    ///         Returns the symbols rather than putting them anywhere. Binding them into a file's scope and
    ///         type map is <see cref="AmbientIntrinsics" />'s job, and it does it once for the whole project.
    ///     </para>
    /// </remarks>
    internal static IntrinsicRegistration Register(CompilationUnit injectInto) =>
        _isBootstrapping ? IntrinsicRegistration.Empty : _cache.GetOrAdd(injectInto.Config.ProjectType, CompileLazily).Value;

    /// <remarks>
    ///     The <see cref="Lazy{T}" /> is what makes a project type compile exactly once, and it has to be
    ///     exactly once rather than merely eventually: two callers that both miss the cache would each get a
    ///     full set of symbols, only one of which is kept. The other caller's files would then be resolved
    ///     against symbols no later file shares, so one name would stand for two <see cref="Symbol" />
    ///     instances within a unit — which every identity comparison downstream, from import binding to
    ///     rename, assumes cannot happen. <see cref="ConcurrentDictionary{TKey,TValue}.GetOrAdd(TKey,Func{TKey,TValue})" />
    ///     alone does not promise the factory runs once; a lazy whose value is published under a lock does.
    ///     The bootstrap cannot deadlock on it, since <see cref="Register" /> returns above before ever
    ///     reaching the cache.
    /// </remarks>
    private static Lazy<IntrinsicRegistration> CompileLazily(ProjectType projectType) =>
        new(() => CompileIntrinsics(projectType), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <remarks>Only ever reached with <see cref="_isBootstrapping" /> clear - <see cref="Register" /> is the gate.</remarks>
    private static IntrinsicRegistration CompileIntrinsics(ProjectType projectType)
    {
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

            return new IntrinsicRegistration(CollectDeclaredSymbols(compiledFiles), CollectNodeBindings(compiledFiles));
        }
        finally
        {
            _isBootstrapping = false;
        }
    }

    /// <summary>
    ///     Every node's own symbol/type below every intrinsic file's own hand-written declarations (never
    ///     the ~1,600-declaration generated Roblox files, which are signature-only and so have no bodies to
    ///     bind) - a default trait method body is real code that can reference a name or use a type
    ///     anywhere inside it, and <see cref="AmbientIntrinsics.Types" />/<see cref="AmbientIntrinsics.References" />
    ///     otherwise only carry the type of each declaration's own top-level node. Without this, a defaulted
    ///     method compiled once here and reused by every 'implement' site that doesn't override it (see
    ///     <see cref="Generation.LuauGenerator.GetOrCreateTraitDefault" />) would resolve nothing below its
    ///     own signature when a consuming file's own <see cref="SemanticModel" /> - which never
    ///     walked this node - tries to read it back out.
    /// </summary>
    /// <remarks>
    ///     Merges each file's own <see cref="SemanticModel.References" />/<see cref="Solving.TypeSolver.BoundTypes" />
    ///     wholesale rather than re-deriving them node by node: a node can carry more than one recorded
    ///     symbol (an interface-invocation name resolves both a value and a type symbol on the same node),
    ///     which a single <c>GetSymbol(node)</c> call would collapse to the first; and re-querying
    ///     <c>GetType(node)</c> for a node the type checker never actually bound would mint and permanently
    ///     cache a fresh, unconstrained <see cref="TypeVariable" /> for it instead of simply omitting
    ///     it.
    /// </remarks>
    private static (SymbolTable References, Dictionary<NodeId, Type> Types) CollectNodeBindings(IEnumerable<CompiledFile> compiledFiles)
    {
        var references = new SymbolTable();
        var types = new Dictionary<NodeId, Type>();
        foreach (var compiledFile in compiledFiles)
        {
            if (compiledFile.SourceFile.Name is NonPluginRuntimeFileName or PluginSecurityFileName) continue;

            foreach (var (id, symbols) in compiledFile.SemanticModel.References)
                references[id] = symbols;

            foreach (var (id, type) in compiledFile.SemanticModel.TypeSolver.BoundTypes)
                types[id] = type;
        }

        return (references, types);
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
                    Interlocked.CompareExchange(ref IntrinsicTypes.SetDefinition, setDefinition, null);
            }

        return intrinsicSymbols;
    }

    private static int? ResolveAttributeUsageFlags(SemanticModel semanticModel, Symbol symbol)
    {
        if (symbol.Declaration is not IWithAttributes { Attributes: { } declaredAttributes })
            return null;

        var usageAttribute = declaredAttributes.AttributeList.Find(
            a => a.Name == "attribute_usage"
        );

        if (usageAttribute?.Arguments.ArgumentList is not [var flagsExpression] || semanticModel.GetConstantValue(flagsExpression) is not double flagsValue)
            return null;

        return (int)flagsValue;
    }
}

/// <summary>
///     What one project type's intrinsic bootstrap produces: <paramref name="Symbols" /> is the ambient
///     name set <see cref="AmbientIntrinsics" /> declares into every file's scope, unchanged from before;
///     <paramref name="NodeBindings" /> is every other node's own symbol/type below those declarations,
///     for a reference or type expression written inside one of them (a default trait method body,
///     chiefly) to still resolve when read back out by a consuming file's own <see cref="SemanticModel" />.
/// </summary>
internal readonly record struct IntrinsicRegistration(
    HashSet<(Symbol Symbol, Type Type)> Symbols,
    (SymbolTable References, Dictionary<NodeId, Type> Types) NodeBindings)
{
    public static readonly IntrinsicRegistration Empty = new([], (References: [], Types: []));
}
