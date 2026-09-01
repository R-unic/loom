using Loom.LanguageServer;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.Testing.LanguageServer;

/// <summary>
///     What an interface or trait sits above and below. Exercised through prepare, the same way a client
///     reaches it, and against one project throughout: the item carries a path that only means anything while
///     the project it was prepared from is still open.
/// </summary>
[Collection("Assembly")]
public class TypeHierarchyTest
{
    /// <remarks>A trait joins an interface through 'implement', never through the colon-list - that names base interfaces only.</remarks>
    private const string Source = """
        trait Named {
            fn name(): string;
        }

        interface Base {
            id: number;
        }

        implement Named for Base {
            fn name(): string -> "base";
        }

        interface Derived: Base {
            extra: string;
        }

        interface Unrelated { }
        """;

    [Fact]
    public async Task Prepare_OffersTheInterfaceUnderTheCursor() =>
        await WithHandlersAsync(
            async (prepare, _, _, uri) =>
            {
                var item = Assert.Single((await PrepareAsync(prepare, uri, "interface Base"))!);
                Assert.Equal("Base", item.Name);
            }
        );

    [Fact]
    public async Task Prepare_OffersTheTraitUnderTheCursor() =>
        await WithHandlersAsync(
            async (prepare, _, _, uri) =>
            {
                var item = Assert.Single((await PrepareAsync(prepare, uri, "trait Named"))!);
                Assert.Equal("Named", item.Name);
            }
        );

    /// <remarks>
    ///     On a declaration, the type half of the name is picked out from the ambiguity of the value the same
    ///     name also declares; on a use elsewhere - here, the base list - there is no such ambiguity, and the
    ///     resolved reference is trusted directly.
    /// </remarks>
    [Fact]
    public async Task Prepare_OffersTheInterfaceAtAUseSiteRatherThanItsDeclaration() =>
        await WithHandlersAsync(
            async (prepare, _, _, uri) =>
            {
                var item = Assert.Single((await PrepareAsync(prepare, uri, "Derived: Base"))!);
                Assert.Equal("Base", item.Name);
            }
        );

    [Fact]
    public async Task Prepare_OffersTheTraitAtAUseSiteRatherThanItsDeclaration() =>
        await WithHandlersAsync(
            async (prepare, _, _, uri) =>
            {
                var item = Assert.Single((await PrepareAsync(prepare, uri, "implement Named"))!);
                Assert.Equal("Named", item.Name);
            }
        );

    [Fact]
    public async Task Subtypes_OfANonInterfaceNonTraitType_AreEmpty() =>
        await Utility.WithLspProjectAsync(
            (store, uri) =>
            {
                Assert.True(store.TryGetState(uri, out var state));
                var enumSymbol = state.File.SemanticModel.DeclaredSymbols.OfType<Loom.Core.Resolving.Symbols.EnumTypeSymbol>().First();

                Assert.Empty(TypeHierarchy.Subtypes(enumSymbol, state.Unit));
                return Task.CompletedTask;
            },
            "enum Colour { Red }"
        );

    [Fact]
    public async Task Prepare_OffersNothingForANonTypeSymbol() =>
        await WithHandlersAsync(async (prepare, _, _, uri) => Assert.Null(await PrepareAsync(prepare, uri, "id")));

    [Fact]
    public async Task Supertypes_OfAnInterface_NameTheTraitItImplements() =>
        await WithHandlersAsync(
            async (prepare, super, _, uri) =>
            {
                var item = Assert.Single((await PrepareAsync(prepare, uri, "interface Base"))!);
                var supertypes = (await super.Handle(new TypeHierarchySupertypesParams { Item = item }, Cancel))!.ToArray();

                var only = Assert.Single(supertypes);
                Assert.Equal("Named", only.Name);
            }
        );

    [Fact]
    public async Task Supertypes_OfAnInterface_NameTheInterfaceItExtends() =>
        await WithHandlersAsync(
            async (prepare, super, _, uri) =>
            {
                var item = Assert.Single((await PrepareAsync(prepare, uri, "interface Derived"))!);
                var supertypes = (await super.Handle(new TypeHierarchySupertypesParams { Item = item }, Cancel))!.ToArray();

                var only = Assert.Single(supertypes);
                Assert.Equal("Base", only.Name);
            }
        );

    [Fact]
    public async Task Supertypes_OfATrait_AreEmpty() =>
        await WithHandlersAsync(
            async (prepare, super, _, uri) =>
            {
                var item = Assert.Single((await PrepareAsync(prepare, uri, "trait Named"))!);
                Assert.Empty((await super.Handle(new TypeHierarchySupertypesParams { Item = item }, Cancel))!);
            }
        );

    [Fact]
    public async Task Subtypes_OfAnInterface_NameEveryInterfaceExtendingIt() =>
        await WithHandlersAsync(
            async (prepare, _, sub, uri) =>
            {
                var item = Assert.Single((await PrepareAsync(prepare, uri, "interface Base"))!);
                var subtypes = (await sub.Handle(new TypeHierarchySubtypesParams { Item = item }, Cancel))!.ToArray();

                var only = Assert.Single(subtypes);
                Assert.Equal("Derived", only.Name);
            }
        );

    [Fact]
    public async Task Subtypes_OfATrait_NameEveryInterfaceImplementingIt() =>
        await WithHandlersAsync(
            async (prepare, _, sub, uri) =>
            {
                var item = Assert.Single((await PrepareAsync(prepare, uri, "trait Named"))!);
                var subtypes = (await sub.Handle(new TypeHierarchySubtypesParams { Item = item }, Cancel))!.ToArray();

                var only = Assert.Single(subtypes);
                Assert.Equal("Base", only.Name);
            }
        );

    [Fact]
    public async Task Subtypes_OfAnUnrelatedInterface_AreEmpty() =>
        await WithHandlersAsync(
            async (prepare, _, sub, uri) =>
            {
                var item = Assert.Single((await PrepareAsync(prepare, uri, "interface Unrelated"))!);
                Assert.Empty((await sub.Handle(new TypeHierarchySubtypesParams { Item = item }, Cancel))!);
            }
        );

    /// <summary>An item whose data no longer resolves to a symbol - here because the file it names is not open - answers with nothing rather than throwing.</summary>
    [Fact]
    public async Task Supertypes_RefusesAnItemThatNoLongerResolves() =>
        await WithHandlersAsync(
            async (_, super, _, uri) =>
            {
                var closedUri = DocumentUri.FromFileSystemPath(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid() + ".loom"));
                var stale = new TypeHierarchyItem
                {
                    Name = "Base",
                    Kind = SymbolKind.Interface,
                    Uri = uri,
                    Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(new Position(0, 0), new Position(0, 1)),
                    SelectionRange = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(new Position(0, 0), new Position(0, 1)),
                    Data = new JObject { ["loomUri"] = closedUri.ToString(), ["loomOffset"] = 0, ["loomName"] = "Base" }
                };

                Assert.Null(await super.Handle(new TypeHierarchySupertypesParams { Item = stale }, Cancel));
            }
        );

    /// <inheritdoc cref="Supertypes_RefusesAnItemThatNoLongerResolves" />
    [Fact]
    public async Task Subtypes_RefusesAnItemThatNoLongerResolves() =>
        await WithHandlersAsync(
            async (_, _, sub, uri) =>
            {
                var closedUri = DocumentUri.FromFileSystemPath(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid() + ".loom"));
                var stale = new TypeHierarchyItem
                {
                    Name = "Base",
                    Kind = SymbolKind.Interface,
                    Uri = uri,
                    Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(new Position(0, 0), new Position(0, 1)),
                    SelectionRange = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(new Position(0, 0), new Position(0, 1)),
                    Data = new JObject { ["loomUri"] = closedUri.ToString(), ["loomOffset"] = 0, ["loomName"] = "Base" }
                };

                Assert.Null(await sub.Handle(new TypeHierarchySubtypesParams { Item = stale }, Cancel));
            }
        );

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    /// <summary>Positions by finding a unique substring rather than counting lines and columns by hand, which a multi-line raw string makes easy to get wrong.</summary>
    private static Task<Container<TypeHierarchyItem>?> PrepareAsync(TypeHierarchyPrepareHandler handler, DocumentUri uri, string needle) =>
        handler.Handle(new TypeHierarchyPrepareParams { TextDocument = new TextDocumentIdentifier(uri), Position = PositionOf(needle) }, Cancel);

    /// <summary>The start of the needle's last word - "interface Base" points at "Base", not at "interface".</summary>
    private static Position PositionOf(string needle)
    {
        var phraseStart = Source.IndexOf(needle, StringComparison.Ordinal);
        Assert.True(phraseStart >= 0, $"'{needle}' does not appear in the fixture");

        var index = phraseStart + needle.LastIndexOf(' ') + 1;
        var line = 0;
        var lineStart = 0;
        for (var i = 0; i < index; i++)
            if (Source[i] == '\n')
            {
                line++;
                lineStart = i + 1;
            }

        return new Position(line, index - lineStart);
    }

    private static async Task WithHandlersAsync(
        Func<TypeHierarchyPrepareHandler, TypeHierarchySupertypesHandler, TypeHierarchySubtypesHandler, DocumentUri, Task> act) =>
        await Utility.WithLspProjectAsync(
            (store, uri) => act(new TypeHierarchyPrepareHandler(store), new TypeHierarchySupertypesHandler(store), new TypeHierarchySubtypesHandler(store), uri),
            Source
        );
}
