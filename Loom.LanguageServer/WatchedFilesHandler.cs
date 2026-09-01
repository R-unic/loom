using Loom.Config;
using Loom.Core.Pipeline;
using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using WatcherRegistration = OmniSharp.Extensions.LanguageServer.Protocol.Models.FileSystemWatcher;

namespace Loom.LanguageServer;

/// <summary>
///     Keeps the compile in step with files changed outside the editor - a branch switch, a code generator,
///     another tool. The client does the watching and reports what moved; a server watching the filesystem
///     itself would have to reimplement the editor's own exclusions and would hear its own writes back.
/// </summary>
public sealed class WatchedFilesHandler(DocumentStore documents, DiagnosticPublisher publisher, Debouncer debouncer) : DidChangeWatchedFilesHandlerBase
{
    /// <summary>The key the batched reload is scheduled under; a change on disk belongs to a project rather than to any one document.</summary>
    private static readonly DocumentUri _batch = DocumentUri.From("loom:watched-files");

    public override Task<Unit> Handle(DidChangeWatchedFilesParams request, CancellationToken cancellationToken)
    {
        var changes = request.Changes
            .Select(change => new WatchedFile(change.Uri.GetFileSystemPath(), change.Type != FileChangeType.Deleted))
            .Where(change => !string.IsNullOrEmpty(change.Path))
            .ToArray();

        if (changes.Length == 0)
            return Unit.Task;

        // a checkout rewrites hundreds of files, and the client reports them as they land - recompiling once
        // per notification would compile the project once per file it changed
        debouncer.Schedule(
            _batch,
            _ =>
            {
                foreach (var result in documents.ReloadFromDisk(changes))
                    publisher.Publish(result);

                return Task.CompletedTask;
            }
        );

        return Unit.Task;
    }

    protected override DidChangeWatchedFilesRegistrationOptions CreateRegistrationOptions(
        DidChangeWatchedFilesCapability capability,
        ClientCapabilities clientCapabilities) =>
        new()
        {
            Watchers = new Container<WatcherRegistration>(
                Watching($"**/*{FileManager.LoomExtension}"),
                // a manifest decides where a project's sources are and what it depends on, so a change to one
                // invalidates the whole unit rather than any single file in it
                Watching($"**/{ConfigReader.ConfigFileName}"),
                // installing a dependency changes which projects the unit spans just as much as the manifest does
                Watching($"**/{LockFile.FileName}")
            )
        };

    private static WatcherRegistration Watching(string glob) => new() { GlobPattern = new GlobPattern(glob) };
}
