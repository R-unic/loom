using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;

namespace Loom.LanguageServer;

/// <summary>
///     Warns before a delete breaks an import. Unlike a move, a delete has nowhere left to point an import
///     at, and the protocol gives <c>willDeleteFiles</c> no way to refuse the operation - a warning shown
///     before the file is gone is the whole of what the server can do about it.
/// </summary>
public sealed class WillDeleteFilesHandler : WillDeleteFileHandlerBase
{
    private readonly DocumentStore _documents;
    private readonly Action<string> _warn;

    public WillDeleteFilesHandler(DocumentStore documents, ILanguageServerFacade server)
        : this(documents, message => server.Window.ShowWarning(message)) { }

    /// <summary>For tests: nothing here reaches a real connection.</summary>
    public WillDeleteFilesHandler(DocumentStore documents, Action<string> warn)
    {
        _documents = documents;
        _warn = warn;
    }

    public override Task<WorkspaceEdit?> Handle(WillDeleteFileParams request, CancellationToken cancellationToken)
    {
        var deletedPaths = request.Files
            .Where(file => file.Uri.IsFile)
            .Select(file => file.Uri.LocalPath)
            .Where(path => !string.IsNullOrEmpty(path))
            .Select(Path.GetFullPath)
            .ToArray();

        // Broken reads each project's Unit.SourceFiles/Unit.Roots, which a concurrent recompile mutates in
        // place - Projects() alone only protects the moment it copies the file list out, not that read
        var broken = _documents.WithProjects(projects => ModuleDeletions.Broken(projects, deletedPaths));
        if (broken.Count > 0)
            Warn(broken);

        // no edit: there is nothing left to point the broken imports at once the delete goes through
        return Task.FromResult<WorkspaceEdit?>(null);
    }

    private void Warn(IReadOnlyList<BrokenImport> broken)
    {
        try
        {
            _warn(ModuleDeletions.Describe(broken));
        }
        catch (Exception)
        {
            // a dead connection is not this handler's problem to solve
        }
    }

    protected override WillDeleteFileRegistrationOptions CreateRegistrationOptions(
        FileOperationsWorkspaceClientCapabilities capability,
        ClientCapabilities clientCapabilities) =>
        new()
        {
            Filters = new Container<FileOperationFilter>(
                new FileOperationFilter { Pattern = new FileOperationPattern { Glob = "**/*.loom", Matches = FileOperationPatternKind.File } },
                new FileOperationFilter { Pattern = new FileOperationPattern { Glob = "**", Matches = FileOperationPatternKind.Folder } }
            )
        };
}
