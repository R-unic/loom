using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;

namespace Loom.LanguageServer;

public sealed class TextDocumentSyncHandler(ILanguageServerFacade server, DocumentStore documents) : TextDocumentSyncHandlerBase
{
    private const string LanguageId = "loom";

    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri) => new(uri, LanguageId);

    public override Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken cancellationToken)
    {
        Publish(request.TextDocument.Uri, documents.Open(request.TextDocument.Uri, request.TextDocument.Text));
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken cancellationToken)
    {
        Publish(request.TextDocument.Uri, documents.Change(request.TextDocument.Uri, request.ContentChanges));
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
        documents.Close(request.TextDocument.Uri);
        Clear(request.TextDocument.Uri);
        return Unit.Task;
    }

    protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(TextSynchronizationCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = TextDocumentSelector.ForPattern("**/*.loom"),
            Change = TextDocumentSyncKind.Incremental
        };

    private void Publish(DocumentUri uri, Core.Pipeline.CompilationResult? result)
    {
        try
        {
            server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams { Uri = uri, Diagnostics = Conversion.DiagnosticsFor(result, uri) });
        }
        catch (Exception)
        {
            // ignored
        }
    }

    private void Clear(DocumentUri uri) => Publish(uri, null);
}
