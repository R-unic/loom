using System.Diagnostics;
using Loom.Config;
using Loom.Core.Diagnostics;
using Loom.Core.Generation.Modules;
using Loom.Core.Modules;
using Loom.Core.Resolving;
using Loom.Core.Resolving.Symbols;
using Loom.Core.Text;

namespace Loom.Core.Pipeline;

public sealed class CompilationUnit(SourceRootSet roots, DiagnosticOptions? diagnosticOptions = null)
{
    /// <summary>A unit spanning one project, <paramref name="config" />'s source directory being its only root.</summary>
    public CompilationUnit(LoomConfig config, DiagnosticOptions? diagnosticOptions = null)
        : this(new SourceRootSet(new SourceRoot(config)), diagnosticOptions)
    {
    }

    /// <summary>
    ///     A unit spanning one project whose files <paramref name="sourceFiles" /> supplies rather than
    ///     <paramref name="config" />'s source directory. For a host compiling text it already holds — the
    ///     playground compiles a buffer, and has no directory to read. <see cref="SourceFiles" /> stays the
    ///     read-only view of what the unit ended up spanning.
    /// </summary>
    public CompilationUnit(LoomConfig config, IEnumerable<SourceFile> sourceFiles, DiagnosticOptions? diagnosticOptions = null)
        : this(new SourceRootSet(new SourceRoot(config, sourceFiles)), diagnosticOptions)
    {
    }

    /// <summary>Every project this unit compiles, the entry project first.</summary>
    public SourceRootSet Roots { get; } = roots;

    /// <summary>
    ///     The entry project's config, which governs what the unit decides once for every root: its project
    ///     type, and the Rojo project modules are named through. Anything decided per file — its output path,
    ///     whether it is written at all, the module boundary its imports may not cross — goes through
    ///     <see cref="Roots" /> instead, since a dependency root has a config of its own.
    /// </summary>
    public LoomConfig Config => Roots.Entry.Config;

    /// <summary>
    ///     Reporting behavior handed to every <see cref="DiagnosticBag" /> the unit's stages create. Defaults
    ///     to <see cref="DiagnosticOptions.Default" />, so a unit only fails fast when its creator asks for it.
    /// </summary>
    public DiagnosticOptions DiagnosticOptions { get; } = diagnosticOptions ?? DiagnosticOptions.Default;

    /// <summary>Every root's files, entry project first.</summary>
    public IReadOnlyList<SourceFile> SourceFiles => Roots.Files;

    /// <summary>
    ///     The package <paramref name="file" />'s diagnostics are the consumer's to read about rather than to
    ///     fix, or <see langword="null" /> when they are the entry project's own — the files whose diagnostics
    ///     are reported exactly as raised.
    /// </summary>
    /// <seealso cref="DiagnosticBag.AttributedTo" />
    public string? PackageAttributionOf(SourceFile file)
    {
        if (DiagnosticOptions.ReportDependencyDiagnostics || Roots.Count == 1)
            return null;

        var root = Roots.Of(file);
        return root == Roots.Entry ? null : root.ToString();
    }

    /// <summary>
    ///     Reporting behavior for <paramref name="file" />'s own stages. A dependency's files never fail fast,
    ///     however the unit was configured: the error worth stopping on is the one naming the package, and the
    ///     process has to survive long enough to raise it.
    /// </summary>
    public DiagnosticOptions DiagnosticOptionsFor(SourceFile file) =>
        PackageAttributionOf(file) == null ? DiagnosticOptions : DiagnosticOptions with { OnFatalError = null };

    /// <summary>Every root's ambient declarations, each visible only to the files of the root that declared them.</summary>
    public GlobalSymbols Globals { get; } = new(roots);

    /// <summary>
    ///     Where the runtime library lives in the instance tree. One per unit, resolved from the entry
    ///     project: the compiled output of every root, dependencies included, runs against that one runtime.
    /// </summary>
    public RuntimeImport RuntimeImport { get; } = RuntimeImport.Resolve(roots.Entry.Config);

    /// <summary>
    ///     Semantic models of files already analyzed in this unit, keyed by source file. Because analysis
    ///     follows the module graph's order, every module a file imports is present here by the time that
    ///     file is resolved, which is how the resolver reads a dependency's exports.
    /// </summary>
    public Dictionary<SourceFile, SemanticModel> AnalyzedModules { get; } = [];

    /// <summary>Names modules for the requires the generator emits, through the unit's Rojo project.</summary>
    public ModuleRequirePathResolver ModuleRequirePaths { get; } = new(roots);

    /// <summary>
    ///     Import dependency graph of the unit, built between the two compilation phases. The resolver reads
    ///     it to find the module an import refers to, so it is null until <see cref="Compile()" /> runs.
    /// </summary>
    public ModuleGraph? ModuleGraph { get; private set; }

    /// <summary>
    ///     What the last build worked out about each file's imports. An incremental compile reparses only the
    ///     files that changed, so every other file's entry is still the answer - which is what keeps the graph
    ///     from being resolved from scratch on every keystroke. A file appearing or vanishing routes through
    ///     <see cref="Compile()" />, which reparses everything and so leaves nothing of the cache standing.
    /// </summary>
    private readonly ModuleGraph.Cache _moduleGraphCache = new();

    private readonly Dictionary<string, (Compiler Compiler, ParsedFile Parsed)> _parsedByPath = new(_pathComparer);
    private readonly Dictionary<string, (CompiledFile CompiledFile, TimeSpan AnalyzeDuration)> _compiledByPath = new(_pathComparer);

    private static readonly StringComparer _pathComparer = PathComparison.Comparer;

    public CompilationResult Compile()
    {
        var stopwatch = Stopwatch.StartNew();
        Globals.Clear();
        AnalyzedModules.Clear();

        var failures = new List<FailedFile>();
        var parsedFiles = ParseAll(failures);
        var compilers = new Dictionary<SourceFile, Compiler>();
        foreach (var (compiler, parsedFile) in parsedFiles)
        {
            compilers.TryAdd(parsedFile.File, compiler);
            _parsedByPath[parsedFile.File.AbsolutePath] = (compiler, parsedFile);
        }

        ModuleGraph = BuildModuleGraph(parsedFiles, failures);
        if (ModuleGraph == null)
            return new CompilationResult([], DiagnosticBag.Concat(failures.ConvertAll(failure => failure.Diagnostics), DiagnosticOptions))
            {
                Failures = failures, Elapsed = stopwatch.Elapsed
            };
        
        var compiledDeclarationFiles = analyzeAll(parsedFile => parsedFile.File.IsDeclaration);
        PopulateGlobals(compiledDeclarationFiles);

        var compiledConcreteFiles = analyzeAll(parsedFile => !parsedFile.File.IsDeclaration);
        var compiledFiles = compiledDeclarationFiles.Concat(compiledConcreteFiles).ToList();
        var diagnostics = DiagnosticBag.Concat(
            [..compiledFiles.ConvertAll(file => file.Diagnostics), ..failures.ConvertAll(failure => failure.Diagnostics)],
            DiagnosticOptions
        );

        if (!diagnostics.ContainsErrors())
            Emit(compiledFiles);

        return new CompilationResult(compiledFiles, diagnostics)
        {
            Failures = failures, Reanalyzed = compiledFiles.ConvertAll(file => file.SourceFile), Elapsed = stopwatch.Elapsed
        };

        List<CompiledFile> analyzeAll(Predicate<ParsedFile> predicate)
        {
            var files = new List<CompiledFile>();
            foreach (var parsedFile in ModuleGraph.Order.FindAll(predicate))
                if (AnalyzeAndCache(compilers[parsedFile.File], parsedFile, ModuleGraph.GetDiagnostics(parsedFile.File), failures) is { } compiledFile)
                    files.Add(compiledFile);

            return files;
        }
    }

    public CompilationResult Recompile(IReadOnlySet<string> changedAbsolutePaths) => Recompile(NormalizePaths(changedAbsolutePaths), static _ => null);

    /// <summary>
    ///     Drops everything cached for a file, so that a later compile does not resurrect it. A file removed
    ///     from <see cref="Roots" /> is otherwise still parsed and analyzed: an incremental compile works from
    ///     what it parsed last time, not from what is on disk now.
    /// </summary>
    public void Forget(string absolutePath)
    {
        var path = NormalizePath(absolutePath);
        _parsedByPath.Remove(path);
        _compiledByPath.Remove(path);
    }

    public CompilationResult Recompile(IReadOnlyDictionary<string, string> changedContents)
    {
        var normalizedContents = new Dictionary<string, string>(_pathComparer);
        foreach (var (path, content) in changedContents)
            normalizedContents[NormalizePath(path)] = content;

        return Recompile(
            normalizedContents.Keys.ToHashSet(),
            path => normalizedContents[path]
        );
    }

    private CompilationResult Recompile(IReadOnlySet<string> changedAbsolutePaths, Func<string, string?> resolveContent)
    {
        changedAbsolutePaths = NormalizePaths(changedAbsolutePaths);
        foreach (var path in changedAbsolutePaths)
            if (resolveContent(path) is { } content)
                Roots.Replace(new SourceFile(path, content));

        if (_compiledByPath.Count == 0
            || changedAbsolutePaths.Any(path => !_parsedByPath.ContainsKey(path) || resolveContent(path) == null && !File.Exists(path)))
            return Compile();

        var stopwatch = Stopwatch.StartNew();
        Globals.Clear();
        AnalyzedModules.Clear();

        var failures = new List<FailedFile>();
        foreach (var path in changedAbsolutePaths)
        {
            var file = new SourceFile(path, resolveContent(path));
            Roots.Replace(file);

            var compiler = new Compiler(this, file);
            if (compiler.Parse() is { } parsedFile)
            {
                _parsedByPath[path] = (compiler, parsedFile);
            }
            else
            {
                failures.Add(new FailedFile(file, compiler.Diagnostics));
                _parsedByPath.Remove(path);
                _compiledByPath.Remove(path);
            }
        }

        var parsedFiles = _parsedByPath.Values.ToList();
        var compilers = new Dictionary<SourceFile, Compiler>();
        foreach (var (compiler, parsedFile) in parsedFiles)
            compilers.TryAdd(parsedFile.File, compiler);

        ModuleGraph = BuildModuleGraph(parsedFiles, failures);
        if (ModuleGraph == null)
            return new CompilationResult([], DiagnosticBag.Concat(failures.ConvertAll(failure => failure.Diagnostics), DiagnosticOptions))
            {
                Failures = failures, Elapsed = stopwatch.Elapsed
            };
        
        var dirty = new HashSet<string>(changedAbsolutePaths, _pathComparer);
        var reanalyzed = new List<SourceFile>();
        var timeSaved = TimeSpan.Zero;

        var compiledDeclarationFiles = analyzeAll(parsedFile => parsedFile.File.IsDeclaration);
        PopulateGlobals(compiledDeclarationFiles);

        var compiledConcreteFiles = analyzeAll(parsedFile => !parsedFile.File.IsDeclaration);
        var compiledFiles = compiledDeclarationFiles.Concat(compiledConcreteFiles).ToList();
        var diagnostics = DiagnosticBag.Concat(
            [..compiledFiles.ConvertAll(file => file.Diagnostics), ..failures.ConvertAll(failure => failure.Diagnostics)],
            DiagnosticOptions
        );

        if (!diagnostics.ContainsErrors())
            Emit(compiledFiles.Where(file => reanalyzed.Contains(file.SourceFile)));

        return new CompilationResult(compiledFiles, diagnostics)
        {
            Failures = failures, Reanalyzed = reanalyzed, Elapsed = stopwatch.Elapsed, EstimatedTimeSaved = timeSaved
        };

        List<CompiledFile> analyzeAll(Predicate<ParsedFile> predicate)
        {
            var files = new List<CompiledFile>();
            foreach (var parsedFile in ModuleGraph.Order.FindAll(predicate))
            {
                var path = parsedFile.File.AbsolutePath;
                if (!dirty.Contains(path) && _compiledByPath.TryGetValue(path, out var cached))
                {
                    AnalyzedModules[parsedFile.File] = cached.CompiledFile.SemanticModel;
                    files.Add(cached.CompiledFile);
                    timeSaved += cached.AnalyzeDuration;
                    continue;
                }

                var previous = _compiledByPath.TryGetValue(path, out var previousEntry) ? previousEntry.CompiledFile : null;
                var compiledFile = AnalyzeAndCache(compilers[parsedFile.File], parsedFile, ModuleGraph.GetDiagnostics(parsedFile.File), failures);
                var dependents = ModuleGraph.Dependents.GetValueOrDefault(parsedFile.File, []);
                if (compiledFile == null)
                {
                    foreach (var dependent in dependents)
                        dirty.Add(dependent.AbsolutePath);

                    continue;
                }

                files.Add(compiledFile);
                reanalyzed.Add(parsedFile.File);
                if (ExportsMatch(previous, compiledFile)) continue;

                foreach (var dependent in dependents)
                    dirty.Add(dependent.AbsolutePath);
            }

            return files;
        }
    }

    /// <summary>
    ///     Writes every compiled file, dependencies included, when the entry project asked for output.
    ///     <see cref="LoomConfig.NoEmit" /> is read off that project alone: a dependency's compiled output is
    ///     part of the build consuming it — written into its output directory, and required from its instance
    ///     tree — so a library author's own choice not to emit cannot leave a consumer's build missing files.
    /// </summary>
    private void Emit(IEnumerable<CompiledFile> compiledFiles)
    {
        if (Config.NoEmit)
            return;

        // A declaration file has nothing to emit - 'declare' statements vanish entirely, so writing one
        // would only leave an empty .luau sitting in the output tree with no Rojo mapping asking for it.
        foreach (var file in compiledFiles)
            if (!file.SourceFile.IsDeclaration)
                FileManager.WriteCompiledFile(file);
    }

    private CompiledFile? AnalyzeAndCache(Compiler compiler, ParsedFile parsedFile, DiagnosticBag? moduleDiagnostics, List<FailedFile> failures)
    {
        var stopwatch = Stopwatch.StartNew();
        var compiledFile = compiler.Analyze(parsedFile, moduleDiagnostics);
        stopwatch.Stop();

        var path = parsedFile.File.AbsolutePath;
        if (compiledFile == null)
        {
            failures.Add(new FailedFile(parsedFile.File, compiler.Diagnostics));
            _compiledByPath.Remove(path);
            return null;
        }

        AnalyzedModules[parsedFile.File] = compiledFile.SemanticModel;
        _compiledByPath[path] = (compiledFile, stopwatch.Elapsed);
        return compiledFile;
    }

    /// <summary>Whether every export the file previously had is still there, under the same name, with the same type - the signal that nothing importing this file needs to be re-analyzed on its account.</summary>
    private static bool ExportsMatch(CompiledFile? previous, CompiledFile current)
    {
        if (previous == null || previous.SemanticModel.Exports.Count != current.SemanticModel.Exports.Count)
            return false;

        foreach (var currentExport in current.SemanticModel.Exports)
        {
            var previousExport = previous.SemanticModel.Exports.Find(export => export.Name == currentExport.Name);
            if (previousExport == null)
                return false;

            var previousType = previous.SemanticModel.GetType(previousExport.Symbol.Declaration);
            var currentType = current.SemanticModel.GetType(currentExport.Symbol.Declaration);
            if (!previousType.Equals(currentType))
                return false;
        }

        return true;
    }

    /// <summary>
    ///     Compiles one file on its own. Its imports resolve against a graph holding only that file, so an
    ///     import of anything else is reported as unresolvable — the file is the whole unit here, and saying
    ///     nothing would leave the import silently bound to nothing.
    /// </summary>
    /// <returns>Null when the compiler gave up on <paramref name="file" />; see <see cref="Compiler.Diagnostics" />.</returns>
    public CompiledFile? Compile(SourceFile file)
    {
        var compiler = new Compiler(this, file);
        var parsedFile = compiler.Parse();
        if (parsedFile == null)
            return null;

        ModuleGraph = BuildModuleGraph([(compiler, parsedFile)], []);

        return compiler.Analyze(parsedFile, ModuleGraph?.GetDiagnostics(file));
    }

    /// <summary>
    ///     The graph orders phase two, so a compiler bug while building it leaves no file analyzable. Every
    ///     parsed file is failed with the one error describing why, on top of whatever its own parse reported.
    /// </summary>
    private ModuleGraph? BuildModuleGraph(List<(Compiler Compiler, ParsedFile ParsedFile)> parsedFiles, List<FailedFile> failures)
    {
        try
        {
            return ModuleGraph.Build(parsedFiles.ConvertAll(parsed => parsed.ParsedFile), Roots, DiagnosticOptionsFor, _moduleGraphCache);
        }
        catch (Exception e)
        {
            var diagnostics = new DiagnosticBag(options: DiagnosticOptions);
            diagnostics.CompilerError(SourceFile.Empty, $"The compiler threw an exception building the module graph!\n{e.Message}\n{e.StackTrace}");
            failures.AddRange(
                parsedFiles.ConvertAll(parsed => new FailedFile(
                        parsed.ParsedFile.File,
                        DiagnosticBag.Concat([parsed.Compiler.Diagnostics, diagnostics], DiagnosticOptions)
                    )
                )
            );

            return null;
        }
    }

    /// <summary>
    ///     The compiler for a file is kept alongside its parsed form so that phase two reports the
    ///     lexer and parser diagnostics phase one collected.
    /// </summary>
    private List<(Compiler Compiler, ParsedFile ParsedFile)> ParseAll(List<FailedFile> failures)
    {
        var parsedFiles = new List<(Compiler, ParsedFile)>();
        foreach (var compiler in SourceFiles.Select(file => new Compiler(this, file)))
            if (compiler.Parse() is { } parsedFile)
                parsedFiles.Add((compiler, parsedFile));
            else
                failures.Add(new FailedFile(compiler.SourceFile, compiler.Diagnostics));

        return parsedFiles;
    }

    /// <summary>
    ///     Hoists every declaration file's top-level symbols into the ambient scope of the root that declared
    ///     them. Two files of one root declaring the same ambient name is an error rather than a silent
    ///     first-one-wins, since nothing at the use site would say which of the two a name resolved to.
    /// </summary>
    private void PopulateGlobals(List<CompiledFile> compiledDeclarationFiles)
    {
        foreach (var compiledFile in compiledDeclarationFiles)
        {
            foreach (var symbol in compiledFile.Tree.Statements.Select(statement => compiledFile.SemanticModel.GetDeclarationSymbol(statement)).OfType<Symbol>())
            {
                var type = compiledFile.SemanticModel.GetType(symbol.Declaration);
                if (Globals.Declare(compiledFile.Root, symbol, type) is not { } existing)
                    continue;

                compiledFile.Diagnostics.Error(
                    symbol.Declaration,
                    InternalCodes.DuplicateGlobal,
                    $"'{symbol.Name}' is already declared by '{compiledFile.Root.Describe(existing.File)}'.",
                    "a project's declaration files share one ambient scope, so each name may only be declared once across them"
                );
            }
        }
    }

    private static string NormalizePath(string path) => Path.GetFullPath(path);

    private static HashSet<string> NormalizePaths(IEnumerable<string> paths)
    {
        var normalized = new HashSet<string>(_pathComparer);
        foreach (var path in paths) normalized.Add(NormalizePath(path));
        return normalized;
    }
}