using Loom.Core.Pipeline;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.LanguageServer;

/// <summary>
///     Every declaration in the workspace, filtered by what the user has typed into the go-to-symbol box.
///     Built from <see cref="DocumentOutline" /> so that a name is described here exactly as the outline
///     describes it - one place decides what kind each declaration is, and what its signature reads as.
/// </summary>
public static class WorkspaceSymbols
{
    /// <summary>
    ///     How many symbols are worth sending. The box shows a screenful and the user narrows it by typing;
    ///     a project's every name would be tens of thousands of entries, serialized on each keystroke.
    /// </summary>
    private const int MaximumResults = 256;

    public static IReadOnlyList<WorkspaceSymbol> Matching(IReadOnlyList<CompiledFile> files, string query)
    {
        // an empty query asks for the whole workspace, which is a request for a list nobody reads; the box
        // is opened and then typed into, and the first keystroke is what makes the question answerable
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var matches = new List<(int Rank, WorkspaceSymbol Symbol)>();
        foreach (var file in files)
        {
            var path = file.SourceFile.AbsolutePath;
            if (!Path.IsPathRooted(path))
                continue;

            var uri = DocumentUri.FromFileSystemPath(path);
            foreach (var (symbol, container) in Flatten(DocumentOutline.Of(file, describe: false), null))
            {
                if (RankOf(symbol.Name, query) is not { } rank)
                    continue;

                matches.Add((rank, ToWorkspaceSymbol(symbol, container, uri)));
            }
        }

        return matches
            .OrderBy(match => match.Rank)
            .ThenBy(match => match.Symbol.Name.Length)
            .ThenBy(match => match.Symbol.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumResults)
            .Select(match => match.Symbol)
            .ToArray();
    }

    /// <summary>
    ///     How well the name answers the query, lower being better, or null when it does not answer it at
    ///     all. A name the query starts is what the user is almost always after; one merely containing it is
    ///     worth offering, below.
    /// </summary>
    private static int? RankOf(string name, string query)
    {
        if (name.StartsWith(query, StringComparison.Ordinal))
            return 0;

        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            return 1;

        return name.Contains(query, StringComparison.OrdinalIgnoreCase) ? 2 : null;
    }

    /// <summary>
    ///     The outline's tree as a flat list, each entry remembering what it was nested under. The protocol's
    ///     workspace symbol is flat and carries the container as a name rather than a link, so a method is
    ///     only distinguishable from the free function beside it by the interface written next to it.
    /// </summary>
    private static IEnumerable<(DocumentSymbol Symbol, string? Container)> Flatten(IEnumerable<DocumentSymbol> symbols, string? container)
    {
        foreach (var symbol in symbols)
        {
            yield return (symbol, container);

            if (symbol.Children is not { } children)
                continue;

            foreach (var nested in Flatten(children, symbol.Name))
                yield return nested;
        }
    }

    private static WorkspaceSymbol ToWorkspaceSymbol(DocumentSymbol symbol, string? container, DocumentUri uri) =>
        new()
        {
            Name = symbol.Name,
            Kind = symbol.Kind,
            Tags = symbol.Tags,
            ContainerName = container,
            // the selection range rather than the whole declaration: opening a result should put the cursor
            // on the name, not at the top of a fifty-line function
            Location = new Location { Uri = uri, Range = symbol.SelectionRange }
        };
}
