using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;

namespace Loom.LanguageServer;

public sealed class WorkspaceSymbolsHandler(DocumentStore documents) : WorkspaceSymbolsHandlerBase
{
    public override Task<Container<WorkspaceSymbol>?> Handle(WorkspaceSymbolParams request, CancellationToken cancellationToken) =>
        Task.FromResult<Container<WorkspaceSymbol>?>(new Container<WorkspaceSymbol>(WorkspaceSymbols.Matching(documents.CompiledFiles(), request.Query ?? "")));

    protected override WorkspaceSymbolRegistrationOptions CreateRegistrationOptions(WorkspaceSymbolCapability capability, ClientCapabilities clientCapabilities) =>
        new() { ResolveProvider = false };
}
