using System.Collections.Concurrent;
using Loom.Config;
using Loom.Core.Pipeline;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.LanguageServer;

public sealed record DocumentState(CompiledFile File, IReadOnlyList<VisibleSymbol> VisibleSymbols);

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
            var file = result.Files.Find(f => f.SourceFile.AbsolutePath == path);
            if (file != null)
                _state[uri] = new DocumentState(file, SnapshotVisibleSymbols(file, unit));

            return result;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static IReadOnlyList<VisibleSymbol> SnapshotVisibleSymbols(CompiledFile file, CompilationUnit unit)
    {
        var semanticModel = file.SemanticModel;
        return semanticModel.Declarations.Values
            .SelectMany(symbols => symbols)
            .Concat(unit.Globals.Of(file.SourceFile).Keys)
            .GroupBy(symbol => symbol.Name)
            .Select(group => group.First())
            .Select(symbol => new VisibleSymbol(symbol.Name, symbol.Kind, semanticModel.GetType(symbol.Declaration).ToString()))
            .ToArray();
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
