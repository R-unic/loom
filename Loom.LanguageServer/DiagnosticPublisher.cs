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
public sealed class DiagnosticPublisher(ILanguageServerFacade server)
{
    private readonly HashSet<DocumentUri> _reported = [];
    private readonly Lock _gate = new();

    /// <summary>Publishes the result's diagnostics, and clears every file that had some and no longer does.</summary>
    public void Publish(CompilationResult? result)
    {
        if (result == null)
            return;

        foreach (var (uri, diagnostics) in Next(result))
            Send(uri, diagnostics);
    }

    /// <summary>
    ///     What to send for this result: a set per file the compile found something in, plus an empty set per
    ///     file that had diagnostics last time and does not now. Advances what the publisher remembers, so it
    ///     is the state machine rather than a query.
    /// </summary>
    public IReadOnlyDictionary<DocumentUri, Diagnostic[]> Next(CompilationResult result)
    {
        var byFile = Conversion.DiagnosticsByFile(result);
        lock (_gate)
        {
            var toSend = byFile.ToDictionary(entry => entry.Key, entry => entry.Value);
            foreach (var uri in _reported.Where(uri => !byFile.ContainsKey(uri)))
                toSend[uri] = [];

            _reported.Clear();
            foreach (var uri in byFile.Keys)
                _reported.Add(uri);

            return toSend;
        }
    }

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
            server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams { Uri = uri, Diagnostics = diagnostics });
        }
        catch (Exception)
        {
            // a dead connection is not this class's problem to solve
        }
    }
}
