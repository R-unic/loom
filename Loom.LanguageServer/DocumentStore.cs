using System.Collections.Concurrent;
using Loom.Config;
using Loom.Core.Pipeline;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.LanguageServer;

/// <summary>
///     Everything a request about one open document is answered from. The unit comes along because a symbol's
///     origin - which package, which module, whether it is ambient at all - is a fact about the whole compile
///     rather than about the file the cursor is in.
/// </summary>
public sealed record DocumentState(CompiledFile File, CompilationUnit Unit, CompletionSnapshot Completions);

public sealed class DocumentStore
{
    private readonly ConcurrentDictionary<DocumentUri, string> _documents = [];
    private readonly ConcurrentDictionary<string, CompilationUnit> _unitsByProjectRoot = [];
    private readonly ConcurrentDictionary<DocumentUri, DocumentState> _state = [];
    private readonly Lock _compilationLock = new();

    public CompilationResult? Open(DocumentUri uri, string text)
    {
        lock (_compilationLock)
        {
            _documents[uri] = text;
            return Recompile(uri, text);
        }
    }

    public CompilationResult? Change(DocumentUri uri, IEnumerable<TextDocumentContentChangeEvent> changes)
    {
        lock (_compilationLock)
        {
            if (!_documents.TryGetValue(uri, out var text))
                return null;

            text = IncrementalText.ApplyChanges(text, changes);
            _documents[uri] = text;
            return Recompile(uri, text);
        }
    }

    public void Close(DocumentUri uri)
    {
        lock (_compilationLock)
        {
            _documents.TryRemove(uri, out _);
            _state.TryRemove(uri, out _);
        }
    }

    public bool TryGetState(DocumentUri uri, out DocumentState state) => _state.TryGetValue(uri, out state!);

    private CompilationResult? Recompile(DocumentUri uri, string text)
    {
        var rawPath = uri.GetFileSystemPath();
        if (string.IsNullOrEmpty(rawPath))
            return null;

        var path = Path.GetFullPath(rawPath);
        try
        {
            if (GetOrCreateUnit(path) is not { } unit)
                return null;

            var result = unit.Recompile(new Dictionary<string, string> { [path] = text });
            var file = result.Files.Find(compiled => FilePaths.Same(compiled.SourceFile.AbsolutePath, path));
            if (file != null)
                _state[uri] = new DocumentState(file, unit, BuildCompletions(file, unit));

            return result;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static CompletionSnapshot BuildCompletions(CompiledFile file, CompilationUnit unit)
    {
        try
        {
            return CompletionSnapshotBuilder.Build(file, unit);
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
        unit.Compile();
        _unitsByProjectRoot[config.ProjectDirectory] = unit;
        return unit;
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
