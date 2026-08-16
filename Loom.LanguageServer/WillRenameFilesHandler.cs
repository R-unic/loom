using MediatR;
using OmniSharp.Extensions.JsonRpc;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.LanguageServer;

/// <summary>One file the editor is about to move, as the protocol describes it.</summary>
/// <remarks>
///     Declared here rather than taken from the protocol library, whose <c>FileRename</c> carries a single
///     <c>uri</c> - the shape of a file <em>creation</em>. A rename has two ends, and without the one it is
///     moving from there is nothing to look for in the imports. The request is otherwise ordinary, so the
///     method attribute is all it takes to have it routed here.
/// </remarks>
public sealed record FileRenaming
{
    public DocumentUri OldUri { get; init; } = DocumentUri.From("file:///");
    public DocumentUri NewUri { get; init; } = DocumentUri.From("file:///");
}

[Method("workspace/willRenameFiles", Direction.ClientToServer)]
public sealed record WillRenameFilesParameters : IRequest<WorkspaceEdit?>
{
    public Container<FileRenaming> Files { get; init; } = new();
}

/// <summary>
///     Rewrites the imports a move would otherwise break, before it happens. The editor applies what comes
///     back and then moves the files, so one undo takes both the move and the edits with it.
/// </summary>
public sealed class WillRenameFilesHandler(DocumentStore documents) : IJsonRpcRequestHandler<WillRenameFilesParameters, WorkspaceEdit?>
{
    public Task<WorkspaceEdit?> Handle(WillRenameFilesParameters request, CancellationToken cancellationToken)
    {
        var renames = request.Files
            .Select(rename => (Old: rename.OldUri.GetFileSystemPath(), New: rename.NewUri.GetFileSystemPath()))
            .Where(rename => !string.IsNullOrEmpty(rename.Old) && !string.IsNullOrEmpty(rename.New))
            .Select(rename => new ModuleRename(Path.GetFullPath(rename.Old), Path.GetFullPath(rename.New)))
            .ToArray();

        // EditsFor reads each project's Unit.SourceFiles/Unit.Roots, which a concurrent recompile mutates in
        // place - Projects() alone only protects the moment it copies the file list out, not that read
        var edits = documents.WithProjects(projects => ModuleRenames.EditsFor(projects, renames));
        if (edits.Count == 0)
            return Task.FromResult<WorkspaceEdit?>(null);

        return Task.FromResult<WorkspaceEdit?>(
            new WorkspaceEdit { Changes = edits.ToDictionary(entry => entry.Key, entry => (IEnumerable<TextEdit>)entry.Value) }
        );
    }
}
