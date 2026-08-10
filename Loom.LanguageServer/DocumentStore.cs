using System.Collections.Concurrent;
using Loom.Config;
using Loom.Core.Modules;
using Loom.Core.Pipeline;
using Loom.Core.Text;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.LanguageServer;

/// <summary>A file the editor reported as changed on disk, and whether it is still there.</summary>
public sealed record WatchedFile(string Path, bool Exists);

/// <summary>
///     Everything a request about one open document is answered from. The unit comes along because a symbol's
///     origin - which package, which module, whether it is ambient at all - is a fact about the whole compile
///     rather than about the file the cursor is in.
/// </summary>
public sealed record DocumentState(CompiledFile File, CompilationUnit Unit, CompletionSnapshot Completions)
{
    /// <summary>
    ///     The unit's modules, indexed once per compile and shared by everything answering from this state.
    ///     Building one walks every file of the project, and it holds the <see cref="SourceFile" /> instances
    ///     of the compile it was built for - which is exactly this state's lifetime, and no longer.
    /// </summary>
    public required ModuleResolver Modules { get; init; }
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

    private readonly ConcurrentDictionary<DocumentUri, OpenDocument> _documents = [];
    private readonly ConcurrentDictionary<string, CompilationUnit> _unitsByProjectRoot = [];
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
            // a manifest decides where a project's sources are and what it depends on, which is everything the
            // unit was built around - there is nothing to update in place, so the unit is rebuilt from scratch
            if (changes.Any(change => Path.GetFileName(change.Path) == ConfigReader.ConfigFileName))
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
            var result = changes.MembershipChanged ? unit.Compile() : unit.Recompile(changes.Paths);
            _results[unit] = result;
            RefreshStates(unit, result);
            return result;
        }
        catch (Exception)
        {
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
            var result = unit.Recompile(dirty.ToDictionary(document => document.Path, document => document.Text));
            _results[unit] = result;

            foreach (var document in dirty)
                document.IsDirty = false;

            RefreshStates(unit, result);
            return result;
        }
        catch (Exception)
        {
            // the buffers stay dirty, so the next request tries again rather than answering from a compile
            // that never finished
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
                _state[openUri] = new DocumentState(file, unit, BuildCompletions(file, unit, modules)) { Modules = modules };
        }
    }

    private void RevertToDisk(OpenDocument document)
    {
        if (document.Unit is not { } unit || ReadFromDisk(document.Path) is not { } disk || disk == document.Text)
            return;

        try
        {
            var result = unit.Recompile(new Dictionary<string, string> { [document.Path] = disk });
            _results[unit] = result;
            RefreshStates(unit, result);
        }
        catch (Exception)
        {
            // nothing to report against: the document that would have carried the failure just closed
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

    private static CompletionSnapshot BuildCompletions(CompiledFile file, CompilationUnit unit, ModuleResolver modules)
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

    private CompilationUnit? GetOrCreateUnit(string absolutePath)
    {
        var config = LocateProjectConfig(absolutePath);
        if (config == null)
            return null;

        if (_unitsByProjectRoot.TryGetValue(config.ProjectDirectory, out var unit))
            return unit;

        config.NoEmit = true;
        unit = new CompilationUnit(config);
        _results[unit] = unit.Compile();
        _unitsByProjectRoot[config.ProjectDirectory] = unit;
        return unit;
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
