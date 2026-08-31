using Loom.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.Testing.LanguageServer;

[Collection("Assembly")]
public class DocumentSymbolHandlerTest
{
    [Fact]
    public async Task Handle_ListsEveryTopLevelDeclaration()
    {
        var symbols = await OutlineAsync(
            """
            interface Player { name: string; }
            enum Message { Hello }
            type Id = number;
            fn main(): void { }
            let count = 1;
            mut total = 2;
            """
        );

        Assert.Equal(["Player", "Message", "Id", "main", "count", "total"], symbols.Select(symbol => symbol.Name));
        Assert.Equal(
            [SymbolKind.Interface, SymbolKind.Enum, SymbolKind.Struct, SymbolKind.Function, SymbolKind.Constant, SymbolKind.Variable],
            symbols.Select(symbol => symbol.Kind)
        );
    }

    [Fact]
    public async Task Handle_NestsInterfaceMembersUnderTheInterface()
    {
        var symbols = await OutlineAsync(
            """
            interface Player {
              name: string;
              greet: fn(other: string): string;
            }
            """
        );

        var members = Assert.Single(symbols).Children!.ToArray();
        Assert.Equal(["name", "greet"], members.Select(member => member.Name));
        Assert.Equal(SymbolKind.Property, members[0].Kind);
        Assert.Equal(SymbolKind.Method, members[1].Kind);
    }

    /// <summary>An indexer has no name of its own, so the outline shows the syntax that declared it.</summary>
    [Fact]
    public async Task Handle_NestsAnIndexerUnderTheInterfaceAsAKeyEntry()
    {
        var member = Assert.Single(Assert.Single(await OutlineAsync("interface WithIndexer { [string]: number; }")).Children!);

        Assert.Equal("[string]", member.Name);
        Assert.Equal(": number", member.Detail);
        Assert.Equal(SymbolKind.Key, member.Kind);
    }

    [Fact]
    public async Task Handle_NestsEnumMembersUnderTheEnum()
    {
        var symbols = await OutlineAsync("enum Message {\n  Hello,\n  Goodbye\n}");
        var members = Assert.Single(symbols).Children!.ToArray();

        Assert.Equal(["Hello", "Goodbye"], members.Select(member => member.Name));
        Assert.All(members, member => Assert.Equal(SymbolKind.EnumMember, member.Kind));
    }

    [Fact]
    public async Task Handle_ShowsTheSignatureAsDetail() =>
        Assert.Equal("(x: number): number", Assert.Single(await OutlineAsync("fn double(x: number): number { return x * 2; }")).Detail);

    [Fact]
    public async Task Handle_SeesThroughExportAndDeclare()
    {
        var symbols = await OutlineAsync("export fn one(): void { }\ndeclare fn two(): void;");
        Assert.Equal(["one", "two"], symbols.Select(symbol => symbol.Name));
        Assert.All(symbols, symbol => Assert.Equal(SymbolKind.Function, symbol.Kind));
    }

    /// <summary>The entry has to cover the keyword too, or clicking it in the outline selects only part of the statement.</summary>
    [Fact]
    public async Task Handle_ForAnExport_CoversTheExportKeyword() =>
        Assert.Equal(0, Assert.Single(await OutlineAsync("export fn one(): void { }")).Range.Start.Character);

    [Fact]
    public async Task Handle_SelectionRangeIsTheNameAlone()
    {
        var symbol = Assert.Single(await OutlineAsync("fn double(x: number): number { return x * 2; }"));
        Assert.Equal(3, symbol.SelectionRange.Start.Character);
        Assert.Equal(9, symbol.SelectionRange.End.Character);
    }

    [Fact]
    public async Task Handle_TagsADeprecatedDeclaration()
    {
        var symbol = Assert.Single(await OutlineAsync("[deprecated(\"use 'add'\")]\nfn sum(x: number): number { return x; }"));
        Assert.Equal(SymbolTag.Deprecated, Assert.Single(symbol.Tags!));
    }

    [Fact]
    public async Task Handle_NestsImplementationsUnderTheTraitTheyImplement()
    {
        var symbols = await OutlineAsync(
            """
            trait ToString {
                fn to_string: string;
            }

            interface User {
                name: string;
            }

            implement ToString for User {
                fn to_string -> name
            }
            """
        );

        Assert.Equal("to_string", Assert.Single(symbols[0].Children!).Name);

        var implementation = symbols[2];
        Assert.Equal("ToString", implementation.Name);
        Assert.Equal("for User", implementation.Detail);
        Assert.Equal("to_string", Assert.Single(implementation.Children!).Name);
    }

    [Fact]
    public async Task Handle_LeavesOutStatementsThatDeclareNothing() => Assert.Empty(await OutlineAsync("print(\"hi\");"));

    /// <remarks>
    ///     Typing just 'let' - the ordinary state of the line while it is still being written - leaves the
    ///     parser with no identifier to give the declaration; it synthesizes an empty one rather than
    ///     failing outright. LSP requires DocumentSymbol.name to be non-empty, so surfacing that empty name
    ///     used to fail the whole outline request in VS Code rather than just omitting the one entry.
    /// </remarks>
    [Fact]
    public async Task Handle_OmitsADeclarationWhoseNameIsNotYetTyped()
    {
        var symbols = await OutlineAsync("fn before(): void { }\nlet");

        Assert.Equal(["before"], symbols.Select(symbol => symbol.Name));
        Assert.All(symbols, symbol => Assert.False(string.IsNullOrEmpty(symbol.Name)));
    }

    /// <inheritdoc cref="Handle_OmitsADeclarationWhoseNameIsNotYetTyped" />
    [Fact]
    public async Task Handle_OmitsAnIncompleteImplementBlock()
    {
        var symbols = await OutlineAsync("interface User { name: string; }\nimplement");

        Assert.Equal(["User"], symbols.Select(symbol => symbol.Name));
    }

    [Fact]
    public async Task Handle_ForAnUnknownDocument_ReturnsNothing()
    {
        var handler = new DocumentSymbolHandler(new DocumentStore());
        var uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.FromFileSystemPath(Path.Combine(Path.GetTempPath(), "does-not-exist.loom"));

        Assert.Null(
            await handler.Handle(
                new DocumentSymbolParams { TextDocument = new TextDocumentIdentifier(uri) },
                TestContext.Current.CancellationToken
            )
        );
    }

    /// <remarks>
    ///     Rendering a declaration resolves and formats its type, which is most of what an outline costs. The
    ///     outline view shows the result beside each name; the workspace symbol search, which asks for every
    ///     file's outline on every keystroke, has nowhere to put it.
    /// </remarks>
    [Fact]
    public async Task Of_WithoutDescriptions_NamesTheSameDeclarationsWithoutRenderingTheirSignatures() =>
        await Utility.WithLspProjectAsync(
            (store, uri) =>
            {
                Assert.True(store.TryGetState(uri, out var state));
                var described = DocumentOutline.Of(state.File);
                var bare = DocumentOutline.Of(state.File, describe: false);

                Assert.Equal(described.Select(symbol => symbol.Name), bare.Select(symbol => symbol.Name));
                Assert.Equal(described.Select(symbol => symbol.Kind), bare.Select(symbol => symbol.Kind));
                Assert.Contains(described, symbol => !string.IsNullOrEmpty(symbol.Detail));
                Assert.All(bare, symbol => Assert.True(string.IsNullOrEmpty(symbol.Detail)));
                return Task.CompletedTask;
            },
            "interface Player { name: string; }\nfn main(): void { }\nlet count = 1;"
        );

    private static async Task<DocumentSymbol[]> OutlineAsync(string source)
    {
        var symbols = Array.Empty<DocumentSymbol>();
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var result = await new DocumentSymbolHandler(store).Handle(
                    new DocumentSymbolParams { TextDocument = new TextDocumentIdentifier(uri) },
                    TestContext.Current.CancellationToken
                );

                symbols = result?.Select(entry => entry.DocumentSymbol!).ToArray() ?? [];
            },
            source
        );

        return symbols;
    }
}
