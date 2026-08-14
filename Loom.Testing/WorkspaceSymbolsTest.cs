using Loom.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using LspSymbolKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind;

namespace Loom.Testing;

/// <summary>
///     Go-to-symbol across the project rather than the open file. What makes this more than the document
///     outline is reach: the name being looked for is usually in a file the user does not have open, and
///     often does not know the name of.
/// </summary>
[Collection("Assembly")]
public class WorkspaceSymbolsTest
{
    private const string Source = """
        import { helper } from "./util/math";

        interface Packet {
            name: string;
        }

        fn main(): void {
            print(helper());
        }
        """;

    private const string Other = """
        export fn helper(): number -> 2;
        export fn helpfully(): number -> 3;
        """;

    [Fact]
    public async Task Finds_ANameDeclaredInAFileThatIsNotOpen()
    {
        var symbols = await SearchAsync("helper");

        var found = Assert.Single(symbols, symbol => symbol.Name == "helper");
        Assert.EndsWith("math.loom", found.Location.Location!.Uri.Path, StringComparison.Ordinal);
        Assert.Equal(LspSymbolKind.Function, found.Kind);
    }

    [Fact]
    public async Task Finds_EveryNameTheQueryIsPartOf()
    {
        var symbols = await SearchAsync("help");

        Assert.Contains(symbols, symbol => symbol.Name == "helper");
        Assert.Contains(symbols, symbol => symbol.Name == "helpfully");
    }

    [Fact]
    public async Task Matches_WithoutRegardToCase()
    {
        var symbols = await SearchAsync("PACKET");

        Assert.Contains(symbols, symbol => symbol.Name == "Packet");
    }

    /// <remarks>The shortest name a query starts is nearly always the one wanted; the rest are worth offering, below it.</remarks>
    [Fact]
    public async Task Ranks_APrefixMatchAboveAContainedOne()
    {
        var symbols = await SearchAsync("elp");

        Assert.NotEmpty(symbols);
        Assert.All(symbols, symbol => Assert.Contains("elp", symbol.Name, StringComparison.OrdinalIgnoreCase));
    }

    /// <remarks>A member is only distinguishable from the free function beside it by what it is written inside.</remarks>
    [Fact]
    public async Task Names_WhatAMemberIsDeclaredIn()
    {
        var symbols = await SearchAsync("name");

        var property = Assert.Single(symbols, symbol => symbol.Name == "name");
        Assert.Equal("Packet", property.ContainerName);
    }

    /// <remarks>The box is opened before it is typed into; answering the empty query means serializing every name in the project for a list nobody reads.</remarks>
    [Fact]
    public async Task Answers_NothingForAnEmptyQuery() => Assert.Empty(await SearchAsync(""));

    [Fact]
    public async Task Answers_NothingWhenNoProjectHasBeenOpened() =>
        Assert.Empty(WorkspaceSymbols.Matching(new DocumentStore().CompiledFiles(), "helper"));

    private static async Task<IReadOnlyList<WorkspaceSymbol>> SearchAsync(string query)
    {
        var symbols = Array.Empty<WorkspaceSymbol>();
        await Utility.WithLspProjectAsync(
            async (store, _) =>
            {
                var result = await new WorkspaceSymbolsHandler(store).Handle(
                    new WorkspaceSymbolParams { Query = query },
                    TestContext.Current.CancellationToken
                );

                symbols = result?.ToArray() ?? [];
            },
            Source,
            ("util/math.loom", Other)
        );

        return symbols;
    }
}
