using Loom.LanguageServer;
using Microsoft.Extensions.DependencyInjection;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using OmniSharp.Extensions.LanguageServer.Server;

var streams = ProtocolStreams.ClaimStandardStreams();
var server = await LanguageServer.From(options =>
    options
        .WithInput(streams.Input)
        .WithOutput(streams.Output)
        // asks the client for its "loom" settings on initialize, and keeps them up to date as the user
        // changes them - which is what ServerSettings reads
        .WithConfigurationSection(ServerSettings.Section)
        .WithServices(services =>
            services.AddSingleton<DocumentStore>()
                .AddSingleton<DiagnosticPublisher>()
                .AddSingleton(provider => new ServerSettings(provider.GetService<ILanguageServerConfiguration>()))
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
        .WithHandler<SemanticTokensHandler>()
        .WithHandler<WorkspaceSymbolsHandler>()
        .WithHandler<SelectionRangeHandler>()
        .WithHandler<CodeLensHandler>()
        .WithHandler<CallHierarchyPrepareHandler>()
        .WithHandler<CallHierarchyIncomingCallsHandler>()
        .WithHandler<CallHierarchyOutgoingCallsHandler>()
        .WithHandler<TypeHierarchyPrepareHandler>()
        .WithHandler<TypeHierarchySupertypesHandler>()
        .WithHandler<TypeHierarchySubtypesHandler>()
        .WithHandler<DocumentLinkHandler>()
        .WithHandler<WatchedFilesHandler>()
        .WithHandler<WillRenameFilesHandler>()
        .WithHandler<WillDeleteFilesHandler>()
        // the protocol library derives its capabilities from the handlers it knows, and it does not know this
        // one - its own model of a rename cannot carry where the file came from, so the request is answered by
        // a handler of ours and has to be advertised by hand or the client will never send it
        .OnInitialized((_, _, response, _) =>
            {
                // set onto the object the library already populated (WillDeleteFilesHandler's own
                // CreateRegistrationOptions runs before OnInitialized and fills in WillDelete on this same
                // object) rather than replacing it - a fresh FileOperationsWorkspaceServerCapabilities here
                // would silently drop that capability from the response
                response.Capabilities.Workspace ??= new WorkspaceServerCapabilities();
                response.Capabilities.Workspace.FileOperations ??= new FileOperationsWorkspaceServerCapabilities();
                response.Capabilities.Workspace.FileOperations.WillRename = new WillRenameFileRegistrationOptions.StaticOptions
                {
                    Filters = new Container<FileOperationFilter>(
                        new FileOperationFilter { Pattern = new FileOperationPattern { Glob = "**/*.loom", Matches = FileOperationPatternKind.File } },
                        new FileOperationFilter { Pattern = new FileOperationPattern { Glob = "**", Matches = FileOperationPatternKind.Folder } }
                    )
                };

                return Task.CompletedTask;
            }
        )
);

await server.WaitForExit;
