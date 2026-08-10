using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.LanguageServer;

public sealed class DefinitionHandler(DocumentStore documents) : DefinitionHandlerBase
{
    public override Task<LocationOrLocationLinks?> Handle(DefinitionParams request, CancellationToken cancellationToken)
    {
        if (!documents.TryGetState(request.TextDocument.Uri, out var state))
            return Task.FromResult<LocationOrLocationLinks?>(null);

        try
        {
            var offset = IncrementalText.ToOffset(state.File.SourceFile.SourceText, request.Position);

            // the same resolution the other navigation requests use, so a member, an import specifier and a
            // plain name all lead somewhere - a member name is a token of its access expression rather than a
            // node, so looking the node up alone finds nothing for it
            var symbol = SymbolReferences.At(state.File, offset);
            if (symbol == null || !File.Exists(symbol.Declaration.File.AbsolutePath))
                return Task.FromResult<LocationOrLocationLinks?>(null);

            var location = new Location
            {
                Uri = DocumentUri.FromFileSystemPath(symbol.Declaration.File.AbsolutePath),
                Range = Conversion.ToRange(symbol.Declaration.LocationSpan)
            };

            return Task.FromResult<LocationOrLocationLinks?>(new LocationOrLocationLinks(new LocationOrLocationLink(location)));
        }
        // a cancelled request must not answer: the client asked for this one to stop, not to come back empty
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Task.FromResult<LocationOrLocationLinks?>(null);
        }
    }

    protected override DefinitionRegistrationOptions CreateRegistrationOptions(DefinitionCapability capability, ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = TextDocumentSelector.ForPattern("**/*.loom") };
}
