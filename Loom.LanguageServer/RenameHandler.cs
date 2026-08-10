using Loom.Core.Pipeline;
using Loom.Core.Resolving.Symbols;
using Loom.Core.Text;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.LanguageServer;

/// <summary>
///     Rewrites every place a symbol is written. What may be renamed is narrower than what may be found: a
///     name belonging to the language or to a package is not this project's to change, and an edit renaming it
///     would be applied to files the user does not own or cannot save.
/// </summary>
public sealed class RenameHandler(DocumentStore documents) : IRenameHandler
{
    public Task<WorkspaceEdit?> Handle(RenameParams request, CancellationToken cancellationToken)
    {
        if (!documents.TryGetState(request.TextDocument.Uri, out var state))
            return Task.FromResult<WorkspaceEdit?>(null);

        try
        {
            var offset = IncrementalText.ToOffset(state.File.SourceFile.SourceText, request.Position);
            if (SymbolReferences.At(state.File, offset) is not { } symbol || !CanRename(symbol, state.Unit) || !IsWritableName(request.NewName))
                return Task.FromResult<WorkspaceEdit?>(null);

            var changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>();
            foreach (var group in SymbolReferences.Of(symbol, state.Unit).GroupBy(reference => reference.File.AbsolutePath))
            {
                if (!Path.IsPathRooted(group.Key))
                    continue;

                changes[DocumentUri.FromFileSystemPath(group.Key)] = group
                    .Select(reference => new TextEdit { Range = Conversion.ToRange(reference.Name.GetLocation()), NewText = request.NewName })
                    .ToArray();
            }

            return Task.FromResult<WorkspaceEdit?>(changes.Count == 0 ? null : new WorkspaceEdit { Changes = changes });
        }
        catch (Exception)
        {
            return Task.FromResult<WorkspaceEdit?>(null);
        }
    }

    public RenameRegistrationOptions GetRegistrationOptions(RenameCapability capability, ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = TextDocumentSelector.ForPattern("**/*.loom"), PrepareProvider = true };

    /// <summary>
    ///     Whether the symbol belongs to this project. An intrinsic is part of the language and lives in an
    ///     embedded resource; a dependency's declaration lives in sources a package manager owns, and renaming
    ///     it there would be undone by the next install and would not change the package it came from.
    /// </summary>
    internal static bool CanRename(Symbol symbol, CompilationUnit unit)
    {
        if (symbol.IsIntrinsic || !Path.IsPathRooted(symbol.File.AbsolutePath))
            return false;

        try
        {
            return unit.Roots.Of(symbol.File) == unit.Roots.Entry;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Whether the new name is one the compiler would accept: an identifier, and not a word the language or Luau already uses.</summary>
    internal static bool IsWritableName(string name) =>
        name.Length > 0
        && (char.IsLetter(name[0]) || name[0] == '_')
        && name.All(character => char.IsLetterOrDigit(character) || character == '_')
        && !SyntaxFacts.KeywordMap.ContainsKey(name)
        && !SyntaxFacts.IsPrimitiveType(name)
        && !Luau.LuauFactory.Keywords.Contains(name);
}
