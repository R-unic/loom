using Loom.Core.Modules;
using Loom.Core.Parsing.AST;
using Loom.Core.Pipeline;
using Loom.Core.Text;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.LanguageServer;

/// <summary>
///     Makes a module specifier the thing it names. Go-to-definition already reaches the declaration behind
///     an imported name, but the specifier itself - the only part of the line naming a file - answered
///     nothing, and a relative one is not a path the editor can follow on its own: where <c>"./util/math"</c>
///     lands depends on which root the importing file belongs to.
/// </summary>
public sealed class DocumentLinkHandler(DocumentStore documents) : DocumentLinkHandlerBase
{
    public override Task<DocumentLinkContainer?> Handle(DocumentLinkParams request, CancellationToken cancellationToken)
    {
        if (!documents.TryGetState(request.TextDocument.Uri, out var state))
            return Task.FromResult<DocumentLinkContainer?>(null);

        var links = new List<DocumentLink>();
        foreach (var statement in state.File.Tree.Statements)
        {
            var specifier = SpecifierOf(statement);
            if (specifier == null || Target(state, specifier.Value as string) is not { } target)
                continue;

            links.Add(new DocumentLink { Range = Conversion.ToRange(specifier.LocationSpan), Target = target });
        }

        return Task.FromResult<DocumentLinkContainer?>(new DocumentLinkContainer(links));
    }

    /// <summary>The quoted module name a statement imports from, or null for a statement that names none.</summary>
    private static Literal? SpecifierOf(Statement statement) =>
        statement switch
        {
            ImportDeclaration import => import.ModuleSpecifier,
            NamespaceImport import => import.ModuleSpecifier,
            IReExport { IsReExport: true } reExport => reExport.ModuleSpecifier,
            _ => null
        };

    /// <summary>
    ///     The file the specifier resolves to, or null when it resolves to nothing - a half-typed path, or a
    ///     package the project does not depend on. A link to a file that is not there would open an empty
    ///     editor rather than say so.
    /// </summary>
    private static DocumentUri? Target(DocumentState state, string? specifier)
    {
        if (string.IsNullOrEmpty(specifier))
            return null;

        var module = Resolve(state.Modules, state.File.SourceFile, specifier);
        return module != null && Path.IsPathRooted(module.AbsolutePath) ? DocumentUri.FromFileSystemPath(module.AbsolutePath) : null;
    }

    private static SourceFile? Resolve(ModuleResolver modules, SourceFile importingFile, string specifier)
    {
        try
        {
            return modules.Resolve(importingFile, specifier).File;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>A link is complete when it is made, so there is nothing left for a resolve request to fill in.</summary>
    public override Task<DocumentLink> Handle(DocumentLink request, CancellationToken cancellationToken) => Task.FromResult(request);

    protected override DocumentLinkRegistrationOptions CreateRegistrationOptions(DocumentLinkCapability capability, ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = TextDocumentSelector.ForPattern("**/*.loom"), ResolveProvider = false };
}
