using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.LanguageServer;

/// <summary>
///     Answers whether the name under the cursor can be renamed, before the editor asks the user for a new
///     one. Refusing here is what turns "rename symbol" into an error the moment it is invoked, rather than
///     into a prompt whose result is silently dropped.
/// </summary>
public sealed class PrepareRenameHandler(DocumentStore documents) : PrepareRenameHandlerBase
{
    public override Task<RangeOrPlaceholderRange?> Handle(PrepareRenameParams request, CancellationToken cancellationToken)
    {
        if (!documents.TryGetState(request.TextDocument.Uri, out var state))
            return Task.FromResult<RangeOrPlaceholderRange?>(null);

        try
        {
            var offset = IncrementalText.ToOffset(state.File.SourceFile.SourceText, request.Position);
            if (SymbolReferences.At(state.File, offset) is not { } symbol)
                return Task.FromResult<RangeOrPlaceholderRange?>(null);

            // CanRename reads state.Unit.Roots, which a concurrent recompile can rebuild mid-check
            bool canRename;
            lock (state.CompilationLock)
                canRename = RenameHandler.CanRename(symbol, state.Unit);

            if (!canRename)
                return Task.FromResult<RangeOrPlaceholderRange?>(null);

            var here = SymbolReferences.In(symbol, state.File).FirstOrDefault(reference => reference.Name.Span.Contains(offset));
            return Task.FromResult(here == null ? null : new RangeOrPlaceholderRange(Conversion.ToRange(here.Name.GetLocation())));
        }
        // a cancelled request must not answer: the client asked for this one to stop, not to come back empty
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Task.FromResult<RangeOrPlaceholderRange?>(null);
        }
    }

    protected override RenameRegistrationOptions CreateRegistrationOptions(RenameCapability capability, ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = TextDocumentSelector.ForPattern("**/*.loom"), PrepareProvider = true };
}
