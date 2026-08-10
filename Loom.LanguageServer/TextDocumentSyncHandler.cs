using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;

namespace Loom.LanguageServer;

public sealed class TextDocumentSyncHandler(DocumentStore documents, DiagnosticPublisher publisher, Debouncer debouncer) : TextDocumentSyncHandlerBase
{
    private const string LanguageId = "loom";

    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri) => new(uri, LanguageId);

    /// <summary>Compiles at once: the first thing an editor wants when a file opens is its diagnostics.</summary>
    public override Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken cancellationToken)
    {
        publisher.Publish(documents.Open(request.TextDocument.Uri, request.TextDocument.Text));
        return Unit.Task;
    }

    /// <summary>
    ///     Records the edit and schedules the compile for once typing stops. Diagnostics are the one thing the
    ///     editor asks for that nobody is waiting on, so they are the one thing worth doing late - every other
    ///     request compiles what it needs when it is asked.
    /// </summary>
    public override Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri;
        if (!documents.Change(uri, request.ContentChanges))
            return Unit.Task;

        debouncer.Schedule(
            uri,
            _ =>
            {
                publisher.Publish(documents.Compile(uri));
                return Task.CompletedTask;
            }
        );

        return Unit.Task;
    }

    public override Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken cancellationToken) => Unit.Task;

    /// <summary>
    ///     Clears the file's diagnostics as it closes. A client keeps whatever the server last published until
    ///     the server says otherwise, so a closed file's errors would otherwise sit in the Problems panel for
    ///     the rest of the session, against a document nothing is analyzing any more.
    /// </summary>
    public override Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken cancellationToken)
    {
        debouncer.Cancel(request.TextDocument.Uri);
        documents.Close(request.TextDocument.Uri);
        publisher.Clear(request.TextDocument.Uri);
        return Unit.Task;
    }

    protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(TextSynchronizationCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = TextDocumentSelector.ForPattern("**/*.loom"),
            Change = TextDocumentSyncKind.Incremental
        };
}
