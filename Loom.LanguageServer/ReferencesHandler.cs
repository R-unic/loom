using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.LanguageServer;

public sealed class ReferencesHandler(DocumentStore documents) : ReferencesHandlerBase
{
    public override Task<LocationContainer?> Handle(ReferenceParams request, CancellationToken cancellationToken)
    {
        if (!documents.TryGetState(request.TextDocument.Uri, out var state))
            return Task.FromResult<LocationContainer?>(null);

        try
        {
            var offset = IncrementalText.ToOffset(state.File.SourceFile.SourceText, request.Position);
            if (SymbolReferences.At(state.File, offset) is not { } symbol)
                return Task.FromResult<LocationContainer?>(null);

            var references = SymbolReferences.Of(symbol, state.Unit, cancellationToken)
                .Where(reference => request.Context.IncludeDeclaration || !reference.IsDeclaration)
                .Select(ToLocation)
                .OfType<Location>()
                .ToArray();

            return Task.FromResult<LocationContainer?>(new LocationContainer(references));
        }
        // a cancelled request must not answer: the client asked for this one to stop, not to come back empty
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Task.FromResult<LocationContainer?>(null);
        }
    }

    private static Location? ToLocation(SymbolReference reference) => Conversion.ToLocation(reference.Name.GetLocation());

    protected override ReferenceRegistrationOptions CreateRegistrationOptions(ReferenceCapability capability, ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = TextDocumentSelector.ForPattern("**/*.loom") };
}
