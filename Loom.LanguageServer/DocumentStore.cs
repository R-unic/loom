using System.Collections.Concurrent;
using Loom.Config;
using Loom.Core.Modules;
using Loom.Core.Pipeline;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.LanguageServer;

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

    public void Close(DocumentUri uri)
    {
        lock (_compilationLock)
        {
            _documents.TryRemove(uri, out _);
            _state.TryRemove(uri, out _);
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

    private CompilationResult? Recompile(CompilationUnit unit, IReadOnlyList<OpenDocument> dirty)
    {
        try
        {
            var result = unit.Recompile(dirty.ToDictionary(document => document.Path, document => document.Text));
            _results[unit] = result;

            foreach (var document in dirty)
                document.IsDirty = false;

            var modules = new ModuleResolver(unit.SourceFiles, unit.Roots);
            foreach (var (openUri, open) in _documents)
            {
                if (open.Unit != unit)
                    continue;

                var file = result.Files.Find(compiled => FilePaths.Same(compiled.SourceFile.AbsolutePath, open.Path));
                if (file != null)
                    _state[openUri] = new DocumentState(file, unit, BuildCompletions(file, unit, modules)) { Modules = modules };
            }

            return result;
        }
        catch (Exception)
        {
            // the buffers stay dirty, so the next request tries again rather than answering from a compile
            // that never finished
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
