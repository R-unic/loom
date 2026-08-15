using Loom.Config;
using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Pipeline;
using Loom.Core.Text;

namespace Loom.Core.Modules;

/// <summary>
///     The import dependency graph of a compilation unit. Resolves every import specifier to a source file,
///     orders the files so that a module is analyzed before anything importing it, and reports imports that
///     cannot be resolved along with dependency cycles.
/// </summary>
public sealed class ModuleGraph
{
    private readonly ModuleDiagnostics _diagnostics;
    private readonly ModuleDiagnostics _cycleDiagnostics;
    private readonly Dictionary<NodeId, SourceFile> _resolvedModules;

    private enum VisitState
    {
        // ReSharper disable once UnusedMember.Local
        /// <summary>Must stay the default value: an absent file reads back as this from the state lookup.</summary>
        Unvisited,
        Visiting,
        Ordered
    }

    private sealed record ModuleEdge(Node ModuleReference, ParsedFile Target);

    /// <summary>
    ///     The module diagnostics of a build, one bag per file, each reporting with that file's own
    ///     <see cref="DiagnosticOptions" /> so they behave like the bags of every other stage.
    /// </summary>
    private sealed class ModuleDiagnostics(Func<SourceFile, DiagnosticOptions> optionsOf)
    {
        private readonly Dictionary<SourceFile, DiagnosticBag> _bags = [];

        public DiagnosticBag? Get(SourceFile file) => _bags.GetValueOrDefault(file);

        /// <summary>Adopts a bag built elsewhere, which is how a file's cached resolution diagnostics get back in.</summary>
        public void Put(SourceFile file, DiagnosticBag bag) => _bags[file] = bag;

        public DiagnosticBag Of(SourceFile file)
        {
            if (!_bags.TryGetValue(file, out var bag))
                _bags[file] = bag = new DiagnosticBag(options: optionsOf(file));

            return bag;
        }
    }

    /// <summary>
    ///     What one build worked out about each file's imports, kept so that editing one file does not cost a
    ///     re-resolution of every other file's. Resolving imports is most of what building the graph costs,
    ///     and on an incremental compile almost every file's answer is the one it gave last time.
    /// </summary>
    /// <remarks>
    ///     Keyed by the parse rather than by the file: reparsing produces a new <see cref="ParsedFile" />,
    ///     which misses the cache and is resolved again, while every file that was not reparsed hits it. What
    ///     an entry cannot survive is the set of files changing - an import that resolved to nothing may now
    ///     resolve, and one that resolved may now be gone - but that cannot happen behind the cache's back:
    ///     <see cref="CompilationUnit" /> answers a file appearing or vanishing with a full compile, which
    ///     reparses everything and so misses on every entry. An edge is stored as a path rather than as the
    ///     <see cref="ParsedFile" /> it pointed at, since that file may since have been reparsed into a new
    ///     one, and a stale target would order and invalidate the wrong instance.
    /// </remarks>
    public sealed class Cache
    {
        private Dictionary<ParsedFile, ResolvedImports> _entries = new(ReferenceEqualityComparer.Instance);

        internal ResolvedImports? Get(ParsedFile parsedFile) => _entries.GetValueOrDefault(parsedFile);

        /// <summary>Keeps exactly what this build used, which is what drops the entries of files it reparsed.</summary>
        internal void Keep(Dictionary<ParsedFile, ResolvedImports> used) => _entries = used;
    }

    /// <summary>One file's imports as resolved, and whatever resolving them had to report.</summary>
    internal sealed record ResolvedImports(List<CachedEdge> Edges, DiagnosticBag? Diagnostics);

    /// <summary>An import that resolved, named by where it landed rather than by what was parsed there at the time.</summary>
    internal sealed record CachedEdge(Node ModuleReference, string TargetPath);

    private ModuleGraph(
        List<ParsedFile> order,
        Dictionary<NodeId, SourceFile> resolvedModules,
        ModuleDiagnostics diagnostics,
        ModuleDiagnostics cycleDiagnostics,
        Dictionary<SourceFile, List<SourceFile>> dependents)
    {
        Order = order;
        _resolvedModules = resolvedModules;
        _diagnostics = diagnostics;
        _cycleDiagnostics = cycleDiagnostics;
        Dependents = dependents;
    }

    /// <summary>Every parsed file, dependencies before their importers.</summary>
    public List<ParsedFile> Order { get; }

    /// <summary>Every file that imports (or re-exports from) the given file, one level - the reverse of the import graph.</summary>
    public IReadOnlyDictionary<SourceFile, List<SourceFile>> Dependents { get; }

    public SourceFile? GetResolvedModule(Node moduleReference) => _resolvedModules.GetValueOrDefault(moduleReference.Id);

    /// <summary>Module diagnostics belonging to <paramref name="file" />, reported at its import sites.</summary>
    /// <remarks>
    ///     Two bags rather than one, because they are cached differently: what a file's own imports resolved to
    ///     is a fact about that file, and is kept between builds; a cycle is a fact about the whole graph, and
    ///     is worked out afresh every time, so that one going away takes its diagnostic with it.
    /// </remarks>
    public DiagnosticBag? GetDiagnostics(SourceFile file)
    {
        var resolution = _diagnostics.Get(file);
        var cycles = _cycleDiagnostics.Get(file);
        if (resolution == null || cycles == null)
            return resolution ?? cycles;

        return DiagnosticBag.Concat([resolution, cycles]);
    }

    /// <param name="parsedFiles"></param>
    /// <param name="roots"></param>
    /// <param name="diagnosticOptionsOf">Reporting behavior per file, the unit's for every file when unspecified.</param>
    /// <param name="cache">What the previous build worked out, reused for every file that has not been reparsed since.</param>
    public static ModuleGraph Build(
        List<ParsedFile> parsedFiles,
        SourceRootSet roots,
        Func<SourceFile, DiagnosticOptions>? diagnosticOptionsOf = null,
        Cache? cache = null)
    {
        var optionsOf = diagnosticOptionsOf ?? (_ => DiagnosticOptions.Default);
        var resolver = new ModuleResolver(parsedFiles.ConvertAll(parsedFile => parsedFile.File), roots);
        var parsedFilesByFile = new Dictionary<SourceFile, ParsedFile>();
        var parsedFilesByPath = new Dictionary<string, ParsedFile>(PathComparison.Comparer);
        foreach (var parsedFile in parsedFiles)
        {
            parsedFilesByFile.TryAdd(parsedFile.File, parsedFile);
            parsedFilesByPath.TryAdd(parsedFile.File.AbsolutePath, parsedFile);
        }

        var resolvedModules = new Dictionary<NodeId, SourceFile>();
        var diagnostics = new ModuleDiagnostics(optionsOf);
        var dependencies = new Dictionary<SourceFile, List<ModuleEdge>>();
        var used = new Dictionary<ParsedFile, ResolvedImports>(ReferenceEqualityComparer.Instance);

        foreach (var parsedFile in parsedFiles)
        {
            var imports = Reusable(cache?.Get(parsedFile), parsedFilesByPath)
                ?? ResolveImports(parsedFile, resolver, parsedFilesByFile, roots, optionsOf(parsedFile.File));

            used[parsedFile] = imports;
            if (imports.Diagnostics != null)
                diagnostics.Put(parsedFile.File, imports.Diagnostics);

            var edges = new List<ModuleEdge>(imports.Edges.Count);
            foreach (var edge in imports.Edges)
            {
                var target = parsedFilesByPath[edge.TargetPath];
                resolvedModules[edge.ModuleReference.Id] = target.File;
                edges.Add(new ModuleEdge(edge.ModuleReference, target));
            }

            dependencies[parsedFile.File] = edges;
        }

        cache?.Keep(used);

        var cycleDiagnostics = new ModuleDiagnostics(optionsOf);
        var order = Sort(parsedFiles, dependencies, roots, cycleDiagnostics);
        var dependents = new Dictionary<SourceFile, List<SourceFile>>();
        foreach (var (file, edges) in dependencies)
            foreach (var edge in edges)
            {
                if (!dependents.TryGetValue(edge.Target.File, out var importers))
                    dependents[edge.Target.File] = importers = [];

                importers.Add(file);
            }

        return new ModuleGraph(order, resolvedModules, diagnostics, cycleDiagnostics, dependents);
    }

    /// <summary>
    ///     The cached entry, if every file it points at is still one of this build's. A target that has gone
    ///     means the set of files changed without the cache being told, and the whole entry is then worth
    ///     nothing - resolving again is cheap next to answering with a module that is not there.
    /// </summary>
    private static ResolvedImports? Reusable(ResolvedImports? cached, Dictionary<string, ParsedFile> parsedFilesByPath)
    {
        if (cached == null)
            return null;

        foreach (var edge in cached.Edges)
            if (!parsedFilesByPath.ContainsKey(edge.TargetPath))
                return null;

        return cached;
    }

    /// <summary>
    ///     Resolves one file's imports, reporting whatever it finds into a bag of that file's own. Nothing
    ///     here reads the rest of the graph, which is what makes the answer worth keeping between builds.
    /// </summary>
    private static ResolvedImports ResolveImports(
        ParsedFile parsedFile,
        ModuleResolver resolver,
        Dictionary<SourceFile, ParsedFile> parsedFilesByFile,
        SourceRootSet roots,
        DiagnosticOptions options)
    {
        DiagnosticBag? bag = null;
        var edges = new List<CachedEdge>();
        foreach (var (node, specifier, path) in ModuleReferencesOf(parsedFile))
        {
            var target = ResolveModuleReference(resolver, parsedFile, node, specifier, path, parsedFilesByFile, ref bag, options);
            if (target == null)
                continue;

            // The edge is still recorded: the import resolved, and dropping it here would bury the realm
            // error under "cannot find name" for everything the module publishes.
            ReportRealmViolation(parsedFile.File, target.File, specifier, roots, ref bag, options);

            edges.Add(new CachedEdge(node, target.File.AbsolutePath));
        }

        return new ResolvedImports(edges, bag);
    }

    /// <summary>
    ///     Reports an import reaching across a realm boundary. Replication is what makes this an error rather
    ///     than a convention: a server module is never delivered to the client, so a client importing one
    ///     names something that is not there at runtime, and a server importing a client module ships code it
    ///     should not have. Shared is importable from anywhere, which is what makes it shared.
    /// </summary>
    private static void ReportRealmViolation(
        SourceFile importing,
        SourceFile imported,
        Node moduleReference,
        SourceRootSet roots,
        ref DiagnosticBag? bag,
        DiagnosticOptions options)
    {
        var from = roots.RealmOf(importing);
        var to = roots.RealmOf(imported);
        if (to == Realm.Shared || to == from)
            return;

        Report(
            ref bag,
            options,
            moduleReference,
            InternalCodes.RealmBoundaryCrossed,
            $"A {Describe(from)} module cannot import a {Describe(to)} one.",
            $"move what both realms need into a shared directory, or declare this module's directory as '{Describe(to)}'"
        );
    }

    private static string Describe(Realm realm) => realm.ToString().ToLowerInvariant();

    /// <remarks>
    ///     Specifiers are matched case-sensitively because Roblox requires are, so a module that differs only
    ///     in case is the likeliest thing the author meant and is worth naming — the bare "could not find it"
    ///     reads like a typo hunt on a file system that does not care about case.
    /// </remarks>
    private static string? NotFoundHint(ModuleResolver resolver, SourceFile importingFile, string specifier, SourceFile? caseInsensitiveMatch)
    {
        if (caseInsensitiveMatch != null)
            return $"did you mean '{resolver.SpecifierOf(importingFile, caseInsensitiveMatch)}'? module paths are case-sensitive";

        return FileManager.IsLoomFile(specifier) ? $"drop the '{FileManager.LoomExtension}' extension from the path" : null;
    }

    /// <summary>Every statement in the file that names another module: imports and re-exports alike.</summary>
    private static IEnumerable<(Node Node, Literal Specifier, string? Path)> ModuleReferencesOf(ParsedFile parsedFile)
    {
        foreach (var import in parsedFile.Imports)
            yield return (import, import.ModuleSpecifier, import.ModulePath);

        foreach (var import in parsedFile.NamespaceImports)
            yield return (import, import.ModuleSpecifier, import.ModulePath);

        foreach (var export in parsedFile.ReExports)
            yield return ((Node)export, export.ModuleSpecifier!, export.ModulePath);
    }

    private static ParsedFile? ResolveModuleReference(
        ModuleResolver resolver,
        ParsedFile parsedFile,
        Node moduleReference,
        Literal moduleSpecifier,
        string? specifier,
        Dictionary<SourceFile, ParsedFile> parsedFilesByFile,
        ref DiagnosticBag? bag,
        DiagnosticOptions options)
    {
        if (parsedFile.File.IsDeclaration)
        {
            Report(
                ref bag,
                options,
                moduleReference,
                InternalCodes.ImportInDeclarationFile,
                "Declaration files cannot import modules.",
                "declare the symbol ambiently instead"
            );

            return null;
        }

        if (specifier == null)
            return null; // the parser already reported the malformed specifier

        var resolution = resolver.Resolve(parsedFile.File, specifier);
        switch (resolution.Status)
        {
            case ModuleResolutionStatus.Resolved when resolution.File != null:
                return parsedFilesByFile.GetValueOrDefault(resolution.File);

            case ModuleResolutionStatus.UnsupportedSpecifier:
                Report(
                    ref bag,
                options,
                    moduleSpecifier,
                    InternalCodes.UnsupportedModuleSpecifier,
                    $"Module '{specifier}' is neither a relative path nor a package name.",
                    FileManager.IsLoomFile(specifier)
                        ? $"drop the '{FileManager.LoomExtension}' extension from the path"
                        : "start the path with './' or '../', or name a package you depend on"
                );

                return null;

            case ModuleResolutionStatus.PackageNotFound:
                Report(
                    ref bag,
                options,
                    moduleSpecifier,
                    InternalCodes.PackageNotFound,
                    $"Cannot find package '{resolution.Package}'.",
                    $"add '{resolution.Package}' to [dependencies] and install it before importing from it"
                );

                return null;

            case ModuleResolutionStatus.UndeclaredDependency:
                Report(
                    ref bag,
                options,
                    moduleSpecifier,
                    InternalCodes.UndeclaredDependency,
                    $"Package '{resolution.Package}' is not a dependency of this project.",
                    $"it is only in this build because something else depends on it; add '{resolution.Package}' to [dependencies] to import it yourself"
                );

                return null;

            case ModuleResolutionStatus.SelfImport:
                Report(
                    ref bag,
                options,
                    moduleSpecifier,
                    InternalCodes.SelfImport,
                    "A module cannot import itself."
                );

                return null;

            case ModuleResolutionStatus.OutsideSourceDirectory:
                Report(
                    ref bag,
                options,
                    moduleSpecifier,
                    InternalCodes.ModuleOutsideSourceDirectory,
                    $"Module '{specifier}' is outside the source directory."
                );

                return null;

            case ModuleResolutionStatus.NotFound:
            default:
                Report(
                    ref bag,
                options,
                    moduleSpecifier,
                    InternalCodes.ModuleNotFound,
                    $"Could not find module '{specifier}'.",
                    NotFoundHint(resolver, parsedFile.File, specifier, resolution.CaseInsensitiveMatch)
                );

                return null;
        }
    }

    /// <summary>
    ///     Depth-first post-order traversal: a file is appended once every module it imports has been
    ///     appended. An edge back into a file still being visited closes a cycle, which is reported at the
    ///     import that closed it and then ignored so the remaining files can still be ordered.
    /// </summary>
    private static List<ParsedFile> Sort(
        List<ParsedFile> parsedFiles,
        Dictionary<SourceFile, List<ModuleEdge>> dependencies,
        SourceRootSet roots,
        ModuleDiagnostics diagnostics)
    {
        var order = new List<ParsedFile>(parsedFiles.Count);
        var states = new Dictionary<SourceFile, VisitState>();
        var path = new List<SourceFile>();
        foreach (var parsedFile in parsedFiles)
            visit(parsedFile);

        return order;

        void visit(ParsedFile parsedFile)
        {
            if (states.GetValueOrDefault(parsedFile.File) is VisitState.Ordered)
                return;

            states[parsedFile.File] = VisitState.Visiting;
            path.Add(parsedFile.File);

            foreach (var edge in dependencies.GetValueOrDefault(parsedFile.File, []))
            {
                if (states.GetValueOrDefault(edge.Target.File) is VisitState.Visiting)
                {
                    Report(
                        diagnostics,
                        parsedFile.File,
                        edge.ModuleReference,
                        InternalCodes.CircularModuleDependency,
                        $"Circular module dependency: {DescribeCycle(path, edge.Target.File, roots)}.",
                        "Luau requires cannot be cyclic; move the shared code into a third module"
                    );

                    continue;
                }

                visit(edge.Target);
            }

            path.RemoveAt(path.Count - 1);
            states[parsedFile.File] = VisitState.Ordered;
            order.Add(parsedFile);
        }
    }

    /// <summary>Each file named by its own root, so a cycle running through a dependency reads as one.</summary>
    private static string DescribeCycle(List<SourceFile> path, SourceFile target, SourceRootSet roots)
    {
        var start = path.IndexOf(target);
        var cycle = path.Skip(start < 0 ? 0 : start).Append(target);
        return string.Join(" → ", cycle.Select(file => roots.Of(file).Describe(file)));
    }

    private static void Report(
        ModuleDiagnostics diagnostics,
        SourceFile file,
        Node node,
        string code,
        string message,
        string? hint = null) =>
        diagnostics.Of(file).Error(node, code, message, hint);

    /// <summary>
    ///     Reports into the bag of the file being resolved, making one only if there turns out to be something
    ///     to put in it - most files import nothing that is wrong with them, and a bag each would be an
    ///     allocation per file per build to hold nothing.
    /// </summary>
    private static void Report(
        ref DiagnosticBag? bag,
        DiagnosticOptions options,
        Node node,
        string code,
        string message,
        string? hint = null) =>
        (bag ??= new DiagnosticBag(options: options)).Error(node, code, message, hint);
}