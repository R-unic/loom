using Loom.Core.Pipeline;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;

namespace Loom.LanguageServer;

/// <summary>
///     Publishes what a compile found, for every file it found something in rather than only the one being
///     edited. A client keeps whatever it was last sent, so this also has to remember what it said: a file
///     whose errors are gone needs to be told so explicitly, and nothing else will say it.
/// </summary>
/// <remarks>
///     Deciding what to publish and reaching the client with it are separate: the first is a state machine
///     worth testing on its own, and the second is one call on a connection a test has no way to hold.
/// </remarks>
public sealed class DiagnosticPublisher(Action<PublishDiagnosticsParams> send)
{
    private readonly HashSet<DocumentUri> _reported = [];
    private readonly Lock _gate = new();

    public DiagnosticPublisher(ILanguageServerFacade server)
        : this(published => server.TextDocument.PublishDiagnostics(published)) { }

    /// <summary>Publishes the result's diagnostics, and clears every file that had some and no longer does.</summary>
    public void Publish(CompilationResult? result)
    {
        if (result == null) return;

        foreach (var (uri, diagnostics) in Next(result))
            Send(uri, diagnostics);
    }

    /// <summary>
    ///     What to send for this result: a set per file the compile found something in, plus an empty set per
    ///     file this compile covered that had diagnostics last time and does not now. Advances what the
    ///     publisher remembers, so it is the state machine rather than a query.
    /// </summary>
    /// <remarks>
    ///     Only files the compile covered are cleared. A workspace may hold more than one project, and each
    ///     compiles on its own - clearing every file not in this result would wipe the other project's
    ///     diagnostics every time this one is edited.
    /// </remarks>
    public IReadOnlyDictionary<DocumentUri, Diagnostic[]> Next(CompilationResult result)
    {
        var byFile = Conversion.DiagnosticsByFile(result);
        var covered = CoveredBy(result);

        lock (_gate)
        {
            var toSend = byFile.ToDictionary(entry => entry.Key, entry => entry.Value);
            foreach (var uri in _reported.Where(uri => covered.Contains(uri) && !byFile.ContainsKey(uri)))
                toSend[uri] = [];

            foreach (var uri in toSend.Keys)
                if (toSend[uri].Length == 0)
                    _reported.Remove(uri);
                else
                    _reported.Add(uri);

            return toSend;
        }
    }

    /// <summary>Every file this compile had an answer for, whether or not it found anything wrong with it.</summary>
    private static HashSet<DocumentUri> CoveredBy(CompilationResult result) =>
        result.Files.Select(file => file.SourceFile)
            .Concat(result.Failures.Select(failure => failure.File))
            .Where(file => Path.IsPathRooted(file.AbsolutePath))
            .Select(file => DocumentUri.FromFileSystemPath(file.AbsolutePath))
            .ToHashSet();

    /// <summary>
    ///     Clears one file, for a document whose diagnostics stop being the server's business - it closed, or
    ///     it turned out not to belong to any project.
    /// </summary>
    public void Clear(DocumentUri uri)
    {
        lock (_gate)
        {
            Send(uri, []);
            _reported.Remove(uri);
        }
    }

    private void Send(DocumentUri uri, Diagnostic[] diagnostics)
    {
        try
        {
            send(new PublishDiagnosticsParams { Uri = uri, Diagnostics = diagnostics });
        }
        catch (Exception)
        {
            // a dead connection is not this class's problem to solve
        }
    }
}
