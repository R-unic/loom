using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using Loom.Config;
using Loom.Core.Modules;
using Loom.Core.Pipeline;
using Loom.Core.Text;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;

namespace Loom.LanguageServer;

/// <summary>A file the editor reported as changed on disk, and whether it is still there.</summary>
public sealed record WatchedFile(string Path, bool Exists);

/// <summary>One project the server has compiled, and the files its last compile produced.</summary>
public sealed record CompiledProject(CompilationUnit Unit, IReadOnlyList<CompiledFile> Files);

/// <summary>
///     Everything a request about one open document is answered from. The unit comes along because a symbol's
///     origin - which package, which module, whether it is ambient at all - is a fact about the whole compile
///     rather than about the file the cursor is in.
/// </summary>
/// <param name="Modules">
///     The unit's modules, indexed once per compile and shared by everything answering from this state.
///     Building one walks every file of the project, and it holds the <see cref="SourceFile" /> instances
///     of the compile it was built for - which is exactly this state's lifetime, and no longer.
/// </param>
public sealed record DocumentState(CompiledFile File, CompilationUnit Unit, ModuleResolver Modules)
{
    /// <summary>
    ///     The store's own compile lock, threaded in so the deferred build below still runs under it. Every
    ///     mutation of <see cref="Unit" />'s shared state (<c>Globals</c>, <c>AnalyzedModules</c>) happens
    ///     under this same lock; without it, a request answered from an older state could read those
    ///     collections while a concurrent compile of another open document is clearing and repopulating them.
    ///     Defaults to a lock of its own rather than being required, since a record this public cannot carry a
    ///     required member the store's lock is not meant to be part of the public surface of.
    /// </summary>
    internal Lock CompilationLock { get; init; } = new();

    /// <summary>What may be written at each offset of this file.</summary>
    /// <remarks>
    ///     Built the first time something asks rather than with the compile, because completion is the only
    ///     request that reads it and every request moves the state. Building it costs about as much again as
    ///     the incremental compile that produced the state, and more as the project grows - the names other
    ///     modules export are collected across the whole unit - so a hover, a highlight or a diagnostic
    ///     publish was paying a completion's price to answer a question that looks at none of it.
    ///     <para>
    ///         Still one snapshot per state: a state describes one compile, so what it offers cannot change
    ///         under a request that already read it, and the next compile builds a state to replace this one.
    ///     </para>
    /// </remarks>
    public CompletionSnapshot Completions
    {
        get
        {
            if (field != null)
                return field;

            lock (CompilationLock)
                return field ??= Build(File, Unit, Modules);
        }
    }

    /// <remarks>
    ///     A snapshot that cannot be built is empty rather than fatal: the compiler bug behind it would
    ///     otherwise take down every request the document answers, not just the completions.
    /// </remarks>
    private static CompletionSnapshot Build(CompiledFile file, CompilationUnit unit, ModuleResolver modules)
    {
        try
        {
            return CompletionSnapshotBuilder.Build(file, unit, modules);
        }
        catch (Exception)
        {
            return CompletionSnapshot.Empty;
        }
    }
}

public sealed class DocumentStore
{
    /// <summary>One open buffer, and whether the compile has caught up with it.</summary>
    private sealed class OpenDocument(string path, string text)
    {
        public string Path { get; } = path;
        public string Text { get; set; } = text;
        public bool IsDirty { get; set; } = true;
        public CompilationUnit? Unit { get; set; }
    }

    /// <summary>
    ///     Told about an exception a compile threw - the compiler-bug path <c>Compiler.Compile</c> is supposed
    ///     to catch itself. Null for the parameterless constructor every direct-construction test site uses;
    ///     a caller that can reach the client wires <see cref="DocumentStore(ILanguageServerFacade)" /> instead,
    ///     so the failure is visible somewhere instead of only freezing the diagnostics that were last published.
    /// </summary>
    private readonly Action<Exception>? _onCompileFailed;

    public DocumentStore() { }

    public DocumentStore(Action<Exception> onCompileFailed) => _onCompileFailed = onCompileFailed;

    public DocumentStore(ILanguageServerFacade server)
        : this(exception => server.Window.LogError($"Loom: compile failed unexpectedly: {exception}")) { }

    private readonly ConcurrentDictionary<DocumentUri, OpenDocument> _documents = [];
    // keyed the way the compiler compares paths, not ordinally: a client's file: URI round-trips a Windows
    // drive letter in a different case than the one a directory walk up from another file's URI produces,
    // and an ordinal key would silently open a second unit for the same project
    private readonly ConcurrentDictionary<string, CompilationUnit> _unitsByProjectRoot = new(FilePaths.Comparer);
    private readonly ConcurrentDictionary<DocumentUri, DocumentState> _state = [];
    private readonly ConcurrentDictionary<CompilationUnit, CompilationResult> _results = [];
    private readonly Lock _compilationLock = new();

    /// <summary>Takes the document and compiles it, since the first thing an editor wants on open is diagnostics.</summary>
    public CompilationResult? Open(DocumentUri uri, string text)
    {
        var path = PathOf(uri);
        if (path == null)
            return null;

        _documents[uri] = new OpenDocument(path, text);
        return Compile(uri);
    }

    /// <summary>
    ///     Records the edit without compiling. Typing is a burst, and compiling on each keystroke would spend
    ///     the whole burst analyzing text that is already gone; the next thing that needs an answer compiles.
    /// </summary>
    /// <returns>False for a document the store is not tracking, which is nothing to record an edit against.</returns>
    public bool Change(DocumentUri uri, IEnumerable<TextDocumentContentChangeEvent> changes)
    {
        if (!_documents.TryGetValue(uri, out var document))
            return false;

        lock (_compilationLock)
        {
            document.Text = IncrementalText.ApplyChanges(document.Text, changes);
            document.IsDirty = true;
        }

        return true;
    }

    /// <summary>
    ///     Drops the buffer and puts the file back to what is on disk. An editor discards unsaved edits when a
    ///     document closes, but the unit keeps whatever text it was last handed - so without this, every other
    ///     file in the project would go on being analyzed against a version of this one that no longer exists
    ///     anywhere.
    /// </summary>
    public void Close(DocumentUri uri)
    {
        lock (_compilationLock)
        {
            if (!_documents.TryRemove(uri, out var document))
                return;

            _state.TryRemove(uri, out _);
            RevertToDisk(document);
        }
    }

    /// <summary>
    ///     Brings the document's project up to date with every open buffer of it and returns what that compile
    ///     found, or the last result when nothing has changed since. Every open buffer goes in together: they
    ///     share one unit, so compiling one file's edits while another's sit unsent would analyze this file
    ///     against a version of its neighbour that only exists on disk.
    /// </summary>
    public CompilationResult? Compile(DocumentUri uri)
    {
        lock (_compilationLock)
        {
            if (!_documents.TryGetValue(uri, out var document))
                return null;

            if (UnitOf(document) is not { } unit)
                return null;

            var dirty = _documents.Values.Where(open => open.Unit == unit && open.IsDirty).ToArray();
            if (dirty.Length == 0)
                return _results.GetValueOrDefault(unit);

            return Recompile(unit, dirty);
        }
    }

    /// <summary>
    ///     The state a request should be answered from, compiled up to the latest edit. Reading is what forces
    ///     the compile, so an answer never describes text the user has already replaced.
    /// </summary>
    public bool TryGetState(DocumentUri uri, out DocumentState state)
    {
        Compile(uri);
        return _state.TryGetValue(uri, out state!);
    }

    /// <summary>Whether the document has edits the last compile did not see.</summary>
    public bool IsDirty(DocumentUri uri) => _documents.TryGetValue(uri, out var document) && document.IsDirty;

    /// <summary>
    ///     Every file of every project the server has compiled, for the questions asked about the workspace
    ///     rather than about one document.
    /// </summary>
    /// <remarks>
    ///     A project enters the store when a document is opened from it, so this covers what the user has
    ///     been in rather than everything on disk - a workspace may hold projects nothing has ever opened,
    ///     and compiling them to answer a search would compile the disk. Files that were already compiled
    ///     are not recompiled here: the answer describes the last compile of each project, which is the same
    ///     text every other answer describes.
    /// </remarks>
    public IReadOnlyList<CompiledFile> CompiledFiles() => Projects().SelectMany(project => project.Files).ToArray();

    /// <inheritdoc cref="CompiledFiles" />
    /// <remarks>Grouped by project for the questions that need the unit as well as the file - what a specifier resolves to is decided by the roots it is written in.</remarks>
    public IReadOnlyList<CompiledProject> Projects()
    {
        lock (_compilationLock)
            return _results.Select(entry => new CompiledProject(entry.Key, entry.Value.Files)).ToArray();
    }

    /// <summary>
    ///     Runs <paramref name="use" /> against every compiled project under the store's own lock, for a caller
    ///     that reads a project's <see cref="CompilationUnit" /> - its <c>SourceFiles</c> or <c>Roots</c> -
    ///     rather than just the files <see cref="Projects" /> already copied out. Those collections are mutated
    ///     in place by a recompile the same way <c>Globals</c> and <c>AnalyzedModules</c> are, so a read of them
    ///     has to be inside the same lock as that mutation, not just after <see cref="Projects" /> returns.
    /// </summary>
    public T WithProjects<T>(Func<IReadOnlyList<CompiledProject>, T> use)
    {
        lock (_compilationLock)
            return use(_results.Select(entry => new CompiledProject(entry.Key, entry.Value.Files)).ToArray());
    }

    /// <summary>
    ///     Takes in changes made to files outside the editor - a branch switch, a generator, another tool - and
    ///     recompiles whatever they touched.
    /// </summary>
    /// <remarks>
    ///     A file open in the editor is skipped: its buffer is the version the user is looking at, and the one
    ///     every request is answered against, so letting the file on disk overwrite it would answer about text
    ///     that is not on screen. A saved buffer arrives here identical to what the store already has anyway.
    /// </remarks>
    /// <returns>The compiles that ran, one per project the changes reached.</returns>
    public IReadOnlyList<CompilationResult> ReloadFromDisk(IReadOnlyList<WatchedFile> changes)
    {
        lock (_compilationLock)
        {
            // a manifest decides where a project's sources are and what it depends on, and a lock decides which
            // versions were installed for them - either changing is everything the unit was built around, so
            // there is nothing to update in place and the unit is rebuilt from scratch
            if (changes.Any(change => Path.GetFileName(change.Path) is ConfigReader.ConfigFileName or LockFile.FileName))
            {
                DiscardUnits();
                return [];
            }

            var affected = new Dictionary<CompilationUnit, UnitChanges>();
            foreach (var change in changes)
            {
                if (!FileManager.IsLoomFile(change.Path) || IsOpen(change.Path))
                    continue;

                if (UnitContaining(change.Path) is not { } unit)
                    continue;

                if (!affected.TryGetValue(unit, out var pending))
                    affected[unit] = pending = new UnitChanges();

                Apply(unit, change, pending);
            }

            return affected.Select(entry => Recompile(entry.Key, entry.Value)).OfType<CompilationResult>().ToArray();
        }
    }

    /// <summary>What a batch of on-disk changes did to one project: which files changed, and whether the set of files itself did.</summary>
    private sealed class UnitChanges
    {
        public HashSet<string> Paths { get; } = [];

        /// <summary>Whether a file appeared or vanished, which changes the module graph and so cannot be compiled incrementally.</summary>
        public bool MembershipChanged { get; set; }
    }

    private static void Apply(CompilationUnit unit, WatchedFile change, UnitChanges pending)
    {
        // the roots decide how the path is spelled; a client's URI and a directory listing disagree about the
        // case of a Windows drive letter, and module specifiers resolve case-sensitively
        var path = unit.Roots.CanonicalPath(change.Path);
        if (change.Exists)
        {
            if (ReadFromDisk(path) is not { } text)
                return;

            var file = new SourceFile(path, text);

            // a file the roots already hold is an edit; one they do not is a new module, and the graph has to
            // be rebuilt around it
            if (!unit.Roots.Replace(file))
                pending.MembershipChanged |= unit.Roots.Add(file);

            pending.Paths.Add(path);
            return;
        }

        if (!unit.Roots.Remove(path))
            return;

        unit.Forget(path);
        pending.MembershipChanged = true;
    }

    private CompilationResult? Recompile(CompilationUnit unit, UnitChanges changes)
    {
        try
        {
            // a file appearing or vanishing rewires the module graph, and an incremental compile works from the
            // graph it built last time
            var result = RunOnLargeStack(() => changes.MembershipChanged ? unit.Compile() : unit.Recompile(changes.Paths));
            _results[unit] = result;
            RefreshStates(unit, result);
            return result;
        }
        catch (Exception exception)
        {
            _onCompileFailed?.Invoke(exception);
            return null;
        }
    }

    /// <summary>Throws away every unit, so the next request builds one from the configuration as it now reads.</summary>
    private void DiscardUnits()
    {
        _unitsByProjectRoot.Clear();
        _results.Clear();
        _state.Clear();

        foreach (var document in _documents.Values)
        {
            document.Unit = null;
            document.IsDirty = true;
        }
    }

    private bool IsOpen(string path) => _documents.Values.Any(document => FilePaths.Same(document.Path, path));

    private CompilationUnit? UnitContaining(string path) =>
        _unitsByProjectRoot.Values.FirstOrDefault(unit => unit.Roots.Any(root => root.Contains(Path.GetFullPath(path))));

    private CompilationResult? Recompile(CompilationUnit unit, IReadOnlyList<OpenDocument> dirty)
    {
        try
        {
            var contents = dirty.ToDictionary(document => document.Path, document => document.Text);
            var result = RunOnLargeStack(() => unit.Recompile(contents));
            _results[unit] = result;

            foreach (var document in dirty)
                document.IsDirty = false;

            RefreshStates(unit, result);
            return result;
        }
        catch (Exception exception)
        {
            // the buffers stay dirty, so the next request tries again rather than answering from a compile
            // that never finished
            _onCompileFailed?.Invoke(exception);
            return null;
        }
    }

    /// <summary>Rebuilds what every open document of the unit is answered from, since one compile moves all of them.</summary>
    private void RefreshStates(CompilationUnit unit, CompilationResult result)
    {
        var modules = new ModuleResolver(unit.SourceFiles, unit.Roots);
        foreach (var (openUri, open) in _documents)
        {
            if (open.Unit != unit)
                continue;

            var file = result.Files.Find(compiled => FilePaths.Same(compiled.SourceFile.AbsolutePath, open.Path));
            if (file != null)
                _state[openUri] = new DocumentState(file, unit, modules) { CompilationLock = _compilationLock };
        }
    }

    private void RevertToDisk(OpenDocument document)
    {
        if (document.Unit is not { } unit || ReadFromDisk(document.Path) is not { } disk || disk == document.Text)
            return;

        try
        {
            var result = RunOnLargeStack(() => unit.Recompile(new Dictionary<string, string> { [document.Path] = disk }));
            _results[unit] = result;
            RefreshStates(unit, result);
        }
        catch (Exception exception)
        {
            // nothing to report against: the document that would have carried the failure just closed
            _onCompileFailed?.Invoke(exception);
        }
    }

    /// <summary>The file's saved text, or null when it has none - a document may close without ever having been saved.</summary>
    private static string? ReadFromDisk(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private CompilationUnit? UnitOf(OpenDocument document) => document.Unit ??= GetOrCreateUnit(document.Path);

    private CompilationUnit? GetOrCreateUnit(string absolutePath)
    {
        var config = LocateProjectConfig(absolutePath);
        if (config == null)
            return null;

        if (_unitsByProjectRoot.TryGetValue(config.ProjectDirectory, out var unit))
            return unit;

        config.NoEmit = true;
        unit = CreateUnit(config);

        CompilationResult result;
        try
        {
            result = RunOnLargeStack(unit.Compile);
        }
        catch (Exception exception)
        {
            // a compile throwing here is the compiler-bug path Compiler.Compile is supposed to catch itself;
            // degrade the same way a later recompile would rather than crashing the request that opened the file
            _onCompileFailed?.Invoke(exception);
            return null;
        }

        _results[unit] = result;
        _unitsByProjectRoot[config.ProjectDirectory] = unit;
        return unit;
    }

    /// <summary>
    ///     Runs a compile on a thread with a much larger stack than the default. Every stage after parsing is a
    ///     recursive visitor that descends once per level of nesting a file's syntax has, and a plausible file -
    ///     a few thousand terms into one chained expression, which generated or pasted code reaches easily -
    ///     overflows the default ~1MB stack. That throws <see cref="StackOverflowException" />, which nothing
    ///     can catch: it takes the whole server down rather than just failing this one compile, and reopening
    ///     the same file crashes it again. A larger stack does not remove the limit, it just moves it well past
    ///     what a real file reaches - the visitors themselves would need to become iterative to remove it.
    /// </summary>
    private static T RunOnLargeStack<T>(Func<T> work)
    {
        var result = default(T);
        ExceptionDispatchInfo? error = null;

        var thread = new Thread(
            () =>
            {
                try
                {
                    result = work();
                }
                catch (Exception exception)
                {
                    error = ExceptionDispatchInfo.Capture(exception);
                }
            },
            maxStackSize: 64 * 1024 * 1024
        ) { IsBackground = true };

        thread.Start();
        thread.Join();

        error?.Throw();
        return result!;
    }

    /// <summary>
    ///     The unit for a project the editor opened a file in, spanning the packages its lock file pins so a symbol
    ///     imported from one resolves the way it does in a build.
    /// </summary>
    /// <remarks>
    ///     A project whose dependencies cannot be loaded — no lock file yet, a dependency not installed — still gets
    ///     a unit over its own files. An editor is used while a project is being put together, and answering nothing
    ///     about the file on screen is worse than answering it without its packages; the unresolved imports are
    ///     reported as the diagnostics they already are.
    /// </remarks>
    private static CompilationUnit CreateUnit(LoomConfig config)
    {
        var roots = ProjectLoader.Load(config, out _);
        return roots == null ? new CompilationUnit(config) : new CompilationUnit(roots);
    }

    private static string? PathOf(DocumentUri uri)
    {
        var raw = uri.GetFileSystemPath();
        return string.IsNullOrEmpty(raw) ? null : Path.GetFullPath(raw);
    }

    private static LoomConfig? LocateProjectConfig(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        while (directory != null)
        {
            var config = ConfigReader.LocateFromDirectory(directory, out _);
            if (config != null)
                return config;

            directory = Path.GetDirectoryName(directory);
        }

        return null;
    }
}
