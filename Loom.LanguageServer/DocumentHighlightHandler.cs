using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.LanguageServer;

/// <summary>
///     Highlights the other places the name under the cursor appears. Scoped to the open document rather than
///     the unit: this is the decoration that follows the caret, so it has to answer from what is already on
///     screen without walking every module.
/// </summary>
public sealed class DocumentHighlightHandler(DocumentStore documents) : DocumentHighlightHandlerBase
{
    public override Task<DocumentHighlightContainer?> Handle(DocumentHighlightParams request, CancellationToken cancellationToken)
    {
        if (!documents.TryGetState(request.TextDocument.Uri, out var state))
            return Task.FromResult<DocumentHighlightContainer?>(null);

        try
        {
            var offset = IncrementalText.ToOffset(state.File.SourceFile.SourceText, request.Position);
            if (SymbolReferences.At(state.File, offset) is not { } symbol)
                return Task.FromResult<DocumentHighlightContainer?>(null);

            var highlights = SymbolReferences.In(symbol, state.File)
                .Select(reference => new DocumentHighlight
                    {
                        Range = Conversion.ToRange(reference.Name.GetLocation()),
                        Kind = reference.IsWrite ? DocumentHighlightKind.Write : DocumentHighlightKind.Read
                    }
                );

            return Task.FromResult<DocumentHighlightContainer?>(new DocumentHighlightContainer(highlights));
        }
        // a cancelled request must not answer: the client asked for this one to stop, not to come back empty
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Task.FromResult<DocumentHighlightContainer?>(null);
        }
    }

    protected override DocumentHighlightRegistrationOptions CreateRegistrationOptions(
        DocumentHighlightCapability capability,
        ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = TextDocumentSelector.ForPattern("**/*.loom") };
}
