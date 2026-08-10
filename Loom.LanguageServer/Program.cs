using Loom.LanguageServer;
using Microsoft.Extensions.DependencyInjection;
using OmniSharp.Extensions.LanguageServer.Server;

var streams = ProtocolStreams.ClaimStandardStreams();
var server = await LanguageServer.From(options =>
    options
        .WithInput(streams.Input)
        .WithOutput(streams.Output)
        .WithServices(services =>
            services.AddSingleton<DocumentStore>()
                .AddSingleton<DiagnosticPublisher>()
                // long enough that a burst of keystrokes settles into one compile, short enough that a pause
                // to look at what you wrote is already long enough to have the diagnostics for it
                .AddSingleton(new Debouncer(TimeSpan.FromMilliseconds(300)))
        )
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
        .WithHandler<CodeActionHandler>()
);

await server.WaitForExit;
