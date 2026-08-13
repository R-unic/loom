using Loom.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.Testing;

/// <summary>
///     Hover and go-to over one of each kind of name. Both render a symbol by what it is, so a kind nothing
///     hovers is a rendering that has never run - and the failure mode is an empty tooltip, which reads as
///     the editor not knowing the language rather than as a bug.
/// </summary>
[Collection("Assembly")]
public class LanguageServerSymbolKindTest
{
    private const string Other = "export fn helper(): number -> 2;\nexport let shared = 7;";

    private const string Source = """
        import { helper, shared } from "./other";

        ### A colour.
        enum Colour { Red, Green }

        ### Something describable.
        trait Describable {
            fn describe(): string;
        }

        ### A packet.
        interface Packet {
            ### The name it carries.
            name: string;
            size: number;
        }

        implement Describable for Packet {
            fn describe(): string -> name;
        }

        event fired(value: number);

        type Alias = number;

        fn main(p: Packet): void {
            let colour = Colour.Red;
            let described = p.describe();
            let aliased: Alias = 1;
            fired += fn(v) { print(v); };
            print(helper(), shared, colour, described, aliased, p.name);
        }
        """;

    private static async Task<string> HoverAsync(int line, int character)
    {
        var hover = "";
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var result = await new HoverHandler(store).Handle(
                    new HoverParams { TextDocument = new TextDocumentIdentifier(uri), Position = new Position(line, character) },
                    TestContext.Current.CancellationToken
                );

                hover = result?.Contents.MarkupContent?.Value ?? "";
            },
            Source,
            ("other.loom", Other)
        );

        return hover;
    }

    [Theory]
    [InlineData(3, 5, "Colour")]
    [InlineData(6, 6, "Describable")]
    [InlineData(11, 10, "Packet")]
    [InlineData(13, 4, "name")]
    [InlineData(21, 6, "fired")]
    [InlineData(23, 5, "Alias")]
    public async Task Hover_DescribesEachDeclarationKind(int line, int character, string expected) =>
        Assert.Contains(expected, await HoverAsync(line, character));

    /// <remarks>
    ///     A name that arrived through an import is described by where it came from, not just by its type -
    ///     which module publishes it is the thing you cannot see from the use site.
    /// </remarks>
    [Fact]
    public async Task Hover_OnAnImportedName_SaysWhereItCameFrom()
    {
        var hover = await HoverAsync(30, 10);

        Assert.Contains("helper", hover);
        Assert.Contains("other", hover);
    }

    [Fact]
    public async Task Hover_OnAnIntrinsic_SaysSo() => Assert.Contains("intrinsic", await HoverAsync(30, 4));

    [Fact]
    public async Task Hover_OnADocumentedMember_ShowsItsDocComment() => Assert.Contains("The name it carries.", await HoverAsync(13, 4));

    [Fact]
    public async Task Definition_ReachesADeclarationInAnotherFile() =>
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var target = await new DefinitionHandler(store).Handle(
                    new DefinitionParams { TextDocument = new TextDocumentIdentifier(uri), Position = new Position(30, 10) },
                    TestContext.Current.CancellationToken
                );

                Assert.NotNull(target);
                Assert.Contains(target!, location => location.Location!.Uri.GetFileSystemPath().EndsWith("other.loom", StringComparison.Ordinal));
            },
            Source,
            ("other.loom", Other)
        );

    [Fact]
    public async Task References_FindEveryUseOfADeclaration() =>
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var references = await new ReferencesHandler(store).Handle(
                    new ReferenceParams
                    {
                        TextDocument = new TextDocumentIdentifier(uri),
                        Position = new Position(11, 10),
                        Context = new ReferenceContext { IncludeDeclaration = true }
                    },
                    TestContext.Current.CancellationToken
                );

                Assert.NotNull(references);
                Assert.NotEmpty(references!);
                Assert.All(references!, location => Assert.Equal(uri, location.Uri));
            },
            Source,
            ("other.loom", Other)
        );

    [Fact]
    public async Task Implementation_ReachesATraitsImplementation() =>
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var target = await new ImplementationHandler(store).Handle(
                    new ImplementationParams { TextDocument = new TextDocumentIdentifier(uri), Position = new Position(6, 6) },
                    TestContext.Current.CancellationToken
                );

                Assert.NotNull(target);
            },
            Source,
            ("other.loom", Other)
        );

    [Fact]
    public async Task DocumentHighlight_MarksEveryUseInTheFile() =>
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var highlights = await new DocumentHighlightHandler(store).Handle(
                    new DocumentHighlightParams { TextDocument = new TextDocumentIdentifier(uri), Position = new Position(11, 10) },
                    TestContext.Current.CancellationToken
                );

                Assert.NotNull(highlights);
                Assert.NotEmpty(highlights!);
            },
            Source,
            ("other.loom", Other)
        );
}
