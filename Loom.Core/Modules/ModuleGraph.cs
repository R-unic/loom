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

        public DiagnosticBag Of(SourceFile file)
        {
            if (!_bags.TryGetValue(file, out var bag))
                _bags[file] = bag = new DiagnosticBag(options: optionsOf(file));

            return bag;
        }
    }

    private ModuleGraph(
        List<ParsedFile> order,
        Dictionary<NodeId, SourceFile> resolvedModules,
        ModuleDiagnostics diagnostics,
        Dictionary<SourceFile, List<SourceFile>> dependents)
    {
        Order = order;
        _resolvedModules = resolvedModules;
        _diagnostics = diagnostics;
        Dependents = dependents;
    }

    /// <summary>Every parsed file, dependencies before their importers.</summary>
    public List<ParsedFile> Order { get; }

    /// <summary>Every file that imports (or re-exports from) the given file, one level - the reverse of the import graph.</summary>
    public IReadOnlyDictionary<SourceFile, List<SourceFile>> Dependents { get; }

    public SourceFile? GetResolvedModule(Node moduleReference) => _resolvedModules.GetValueOrDefault(moduleReference.Id);

    /// <summary>Module diagnostics belonging to <paramref name="file" />, reported at its import sites.</summary>
    public DiagnosticBag? GetDiagnostics(SourceFile file) => _diagnostics.Get(file);

    /// <param name="diagnosticOptionsOf">Reporting behavior per file, the unit's for every file when unspecified.</param>
    public static ModuleGraph Build(List<ParsedFile> parsedFiles, SourceRootSet roots, Func<SourceFile, DiagnosticOptions>? diagnosticOptionsOf = null)
    {
        var resolver = new ModuleResolver(parsedFiles.ConvertAll(parsedFile => parsedFile.File), roots);
        var parsedFilesByFile = new Dictionary<SourceFile, ParsedFile>();
        foreach (var parsedFile in parsedFiles)
            parsedFilesByFile.TryAdd(parsedFile.File, parsedFile);

        var resolvedModules = new Dictionary<NodeId, SourceFile>();
        var diagnostics = new ModuleDiagnostics(diagnosticOptionsOf ?? (_ => DiagnosticOptions.Default));
        var dependencies = new Dictionary<SourceFile, List<ModuleEdge>>();
        foreach (var parsedFile in parsedFiles)
        {
            var edges = new List<ModuleEdge>();
            foreach (var (node, specifier, path) in ModuleReferencesOf(parsedFile))
            {
                var target = ResolveModuleReference(
                    resolver,
                    parsedFile,
                    node,
                    specifier,
                    path,
                    parsedFilesByFile,
                    diagnostics
                );

                if (target == null)
                    continue;

                resolvedModules[node.Id] = target.File;
                edges.Add(new ModuleEdge(node, target));
            }

            dependencies[parsedFile.File] = edges;
        }

        var order = Sort(parsedFiles, dependencies, roots, diagnostics);
        var dependents = new Dictionary<SourceFile, List<SourceFile>>();
        foreach (var (file, edges) in dependencies)
            foreach (var edge in edges)
            {
                if (!dependents.TryGetValue(edge.Target.File, out var importers))
                    dependents[edge.Target.File] = importers = [];

                importers.Add(file);
            }

        return new ModuleGraph(order, resolvedModules, diagnostics, dependents);
    }

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
            yield return (export, export.ModuleSpecifier!, export.ModulePath);
    }

    private static ParsedFile? ResolveModuleReference(
        ModuleResolver resolver,
        ParsedFile parsedFile,
        Node moduleReference,
        Literal moduleSpecifier,
        string? specifier,
        Dictionary<SourceFile, ParsedFile> parsedFilesByFile,
        ModuleDiagnostics diagnostics)
    {
        if (parsedFile.File.IsDeclaration)
        {
            Report(
                diagnostics,
                parsedFile.File,
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
                    diagnostics,
                    parsedFile.File,
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
                    diagnostics,
                    parsedFile.File,
                    moduleSpecifier,
                    InternalCodes.PackageNotFound,
                    $"Cannot find package '{resolution.Package}'.",
                    $"add '{resolution.Package}' to [dependencies] and install it before importing from it"
                );

                return null;

            case ModuleResolutionStatus.UndeclaredDependency:
                Report(
                    diagnostics,
                    parsedFile.File,
                    moduleSpecifier,
                    InternalCodes.UndeclaredDependency,
                    $"Package '{resolution.Package}' is not a dependency of this project.",
                    $"it is only in this build because something else depends on it; add '{resolution.Package}' to [dependencies] to import it yourself"
                );

                return null;

            case ModuleResolutionStatus.SelfImport:
                Report(
                    diagnostics,
                    parsedFile.File,
                    moduleSpecifier,
                    InternalCodes.SelfImport,
                    "A module cannot import itself."
                );

                return null;

            case ModuleResolutionStatus.OutsideSourceDirectory:
                Report(
                    diagnostics,
                    parsedFile.File,
                    moduleSpecifier,
                    InternalCodes.ModuleOutsideSourceDirectory,
                    $"Module '{specifier}' is outside the source directory."
                );

                return null;

            case ModuleResolutionStatus.NotFound:
            default:
                Report(
                    diagnostics,
                    parsedFile.File,
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
}