using Loom.LanguageServer;
using Microsoft.Extensions.DependencyInjection;
using OmniSharp.Extensions.LanguageServer.Server;

var streams = ProtocolStreams.ClaimStandardStreams();
var server = await LanguageServer.From(options =>
    options
        .WithInput(streams.Input)
        .WithOutput(streams.Output)
        .WithServices(services => services.AddSingleton<DocumentStore>())
        .WithHandler<TextDocumentSyncHandler>()
        .WithHandler<HoverHandler>()
        .WithHandler<DefinitionHandler>()
        .WithHandler<CompletionHandler>()
        .WithHandler<SignatureHelpHandler>()
        .WithHandler<DocumentSymbolHandler>()
        .WithHandler<InlayHintHandler>()
        .WithHandler<ReferencesHandler>()
        .WithHandler<DocumentHighlightHandler>()
        .WithHandler<PrepareRenameHandler>()
        .WithHandler<RenameHandler>()
        .WithHandler<FoldingRangeHandler>()
        .WithHandler<TypeDefinitionHandler>()
        .WithHandler<ImplementationHandler>()
);

await server.WaitForExit;
