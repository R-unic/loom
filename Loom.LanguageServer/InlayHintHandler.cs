using Loom.Core.Text;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.LanguageServer;

public sealed class InlayHintHandler(DocumentStore documents) : InlayHintsHandlerBase
{
    public override Task<InlayHintContainer?> Handle(InlayHintParams request, CancellationToken cancellationToken)
    {
        if (!documents.TryGetState(request.TextDocument.Uri, out var state))
            return Task.FromResult<InlayHintContainer?>(null);

        try
        {
            var text = state.File.SourceFile.SourceText;
            var range = TextSpan.FromStartEnd(IncrementalText.ToOffset(text, request.Range.Start), IncrementalText.ToOffset(text, request.Range.End));

            return Task.FromResult<InlayHintContainer?>(new InlayHintContainer(InlayHints.Of(state.File, range)));
        }
        catch (Exception)
        {
            return Task.FromResult<InlayHintContainer?>(null);
        }
    }

    /// <summary>Every hint arrives complete, so resolving one has nothing to add.</summary>
    public override Task<InlayHint> Handle(InlayHint request, CancellationToken cancellationToken) => Task.FromResult(request);

    protected override InlayHintRegistrationOptions CreateRegistrationOptions(InlayHintClientCapabilities capability, ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = TextDocumentSelector.ForPattern("**/*.loom"), ResolveProvider = false };
}
