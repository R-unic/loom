using Loom.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.Testing.LanguageServer;

/// <summary>
///     What the client is told each token is. The interesting half is the half a grammar cannot reach - an
///     identifier is a type, a parameter or a method only because the compiler resolved it - so most of these
///     point at a name and ask what it came back as.
/// </summary>
[Collection("Assembly")]
public class SemanticTokenTest
{
    private const string Source = """
        ### A packet.
        interface Packet {
            name: string;
            mut size: number;
        }

        trait Describable {
            fn describe(): string;
        }

        enum Colour { Red, Green }

        event fired(value: number);

        fn label<T>(packet: Packet, extra: T): string {
            mut count = 1;
            count = count + 1;
            print(packet.name);
            return packet.name;
        }
        """;

    [Fact]
    public async Task Classifies_AnInterfaceAndItsUses()
    {
        var tokens = await ClassifyAsync(Source);

        Assert.All(Named(tokens, "Packet"), token => Assert.Equal(SemanticTokenType.Interface, token.Type));
        Assert.Equal(2, Named(tokens, "Packet").Length);
    }

    [Fact]
    public async Task Classifies_ATraitAsAnInterfaceAndItsFunctionAsAMethod()
    {
        var tokens = await ClassifyAsync(Source);

        Assert.Equal(SemanticTokenType.Interface, Single(tokens, "Describable").Type);
        Assert.Equal(SemanticTokenType.Method, Single(tokens, "describe").Type);
    }

    [Fact]
    public async Task Classifies_AFreeFunctionAsAFunctionRatherThanAMethod()
    {
        var tokens = await ClassifyAsync(Source);

        Assert.Equal(SemanticTokenType.Function, Single(tokens, "label").Type);
    }

    [Fact]
    public async Task Classifies_AParameterApartFromALocal()
    {
        var tokens = await ClassifyAsync(Source);

        Assert.All(Named(tokens, "packet"), token => Assert.Equal(SemanticTokenType.Parameter, token.Type));
        Assert.All(Named(tokens, "count"), token => Assert.Equal(SemanticTokenType.Variable, token.Type));
    }

    /// <remarks>A type parameter is declared as a type, but it stands for whatever a use site passes - which is what a reader wants told apart from the concrete types beside it.</remarks>
    [Fact]
    public async Task Classifies_ATypeParameterApartFromAType()
    {
        var tokens = await ClassifyAsync(Source);

        Assert.All(Named(tokens, "T"), token => Assert.Equal(SemanticTokenType.TypeParameter, token.Type));
    }

    [Fact]
    public async Task Classifies_AnEnumAndItsMembers()
    {
        var tokens = await ClassifyAsync(Source);

        Assert.Equal(SemanticTokenType.Enum, Single(tokens, "Colour").Type);
        Assert.Equal(SemanticTokenType.EnumMember, Single(tokens, "Red").Type);
    }

    [Fact]
    public async Task Classifies_AnEventDeclaration()
    {
        var tokens = await ClassifyAsync(Source);

        Assert.Equal(SemanticTokenType.Event, Single(tokens, "fired").Type);
    }

    [Fact]
    public async Task Classifies_APropertyReachedThroughItsReceiver()
    {
        var tokens = await ClassifyAsync(Source);

        Assert.All(Named(tokens, "name"), token => Assert.Equal(SemanticTokenType.Property, token.Type));
    }

    /// <remarks>Immutability is the default, so what the modifier picks out is every binding that is not the exception.</remarks>
    [Fact]
    public async Task Classifies_AnImmutableBindingAsReadonlyAndAMutableOneAsNot()
    {
        var tokens = await ClassifyAsync(Source);

        Assert.Contains(SemanticTokenModifier.Readonly, Named(tokens, "packet")[0].Modifiers);
        Assert.DoesNotContain(SemanticTokenModifier.Readonly, Named(tokens, "count")[0].Modifiers);
        Assert.DoesNotContain(SemanticTokenModifier.Readonly, Single(tokens, "size").Modifiers);
    }

    [Fact]
    public async Task Classifies_ADeclarationApartFromAUse()
    {
        var tokens = await ClassifyAsync(Source);
        var uses = Named(tokens, "packet");

        Assert.Contains(SemanticTokenModifier.Declaration, uses[0].Modifiers);
        Assert.All(uses.Skip(1), token => Assert.DoesNotContain(SemanticTokenModifier.Declaration, token.Modifiers));
    }

    [Fact]
    public async Task Classifies_AnIntrinsicAsComingFromTheDefaultLibrary()
    {
        var tokens = await ClassifyAsync(Source);

        Assert.Contains(SemanticTokenModifier.DefaultLibrary, Single(tokens, "print").Modifiers);
    }

    [Fact]
    public async Task Classifies_KeywordsCommentsAndLiterals()
    {
        var tokens = await ClassifyAsync(Source);

        Assert.Equal(SemanticTokenType.Keyword, Named(tokens, "interface")[0].Type);
        Assert.Contains(tokens, token => token.Type == SemanticTokenType.Number);
        var doc = Assert.Single(tokens, token => token.Token.Text.StartsWith("###", StringComparison.Ordinal));
        Assert.Equal(SemanticTokenType.Comment, doc.Type);
        Assert.Contains(SemanticTokenModifier.Documentation, doc.Modifiers);
    }

    /// <remarks>Brackets and separators are left out: the editor already reads them, and every one sent is five more numbers on every keystroke.</remarks>
    [Fact]
    public async Task Classifies_NothingForDelimiters()
    {
        var tokens = await ClassifyAsync(Source);

        Assert.DoesNotContain(tokens, token => token.Token.Text is "{" or "}" or "(" or ")" or ";" or "," or ".");
    }

    [Fact]
    public async Task Handle_EncodesEveryTokenAsFiveNumbersInSourceOrder() =>
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var tokens = await new SemanticTokensHandler(store).Handle(
                    new SemanticTokensParams { TextDocument = new TextDocumentIdentifier(uri) },
                    TestContext.Current.CancellationToken
                );

                var data = tokens!.Data.ToArray();
                Assert.NotEmpty(data);
                Assert.Equal(0, data.Length % 5);
                // every entry after the first is a delta from the one before, so a negative line would put a
                // token before the token it follows
                for (var i = 5; i < data.Length; i += 5)
                    Assert.True(data[i] >= 0, "tokens have to be pushed in source order");
            },
            Source
        );

    /// <remarks>
    ///     The protocol describes a token as a line, a column and a length, so one that spans lines has to be
    ///     sent as one entry per line. A block comment is the case that reaches this.
    /// </remarks>
    [Fact]
    public async Task Handle_SplitsATokenThatSpansLines() =>
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var tokens = await new SemanticTokensHandler(store).Handle(
                    new SemanticTokensParams { TextDocument = new TextDocumentIdentifier(uri) },
                    TestContext.Current.CancellationToken
                );

                var data = tokens!.Data.ToArray();
                var comment = SemanticTokenClassifier.Legend.GetTokenTypeIdentity((SemanticTokenType?)SemanticTokenType.Comment);
                var commentLines = 0;
                for (var i = 0; i < data.Length; i += 5)
                    if (data[i + 3] == comment)
                        commentLines++;

                Assert.Equal(3, commentLines);
            },
            "#: one\ntwo\nthree :#\nlet x = 1;"
        );

    private static async Task<IReadOnlyList<ClassifiedToken>> ClassifyAsync(string source)
    {
        IReadOnlyList<ClassifiedToken> tokens = [];
        await Utility.WithLspProjectAsync(
            (store, uri) =>
            {
                Assert.True(store.TryGetState(uri, out var state));
                tokens = SemanticTokenClassifier.Of(state.File);
                return Task.CompletedTask;
            },
            source
        );

        return tokens;
    }

    private static ClassifiedToken[] Named(IReadOnlyList<ClassifiedToken> tokens, string text) =>
        tokens.Where(token => token.Token.Text == text).ToArray();

    private static ClassifiedToken Single(IReadOnlyList<ClassifiedToken> tokens, string text) => Assert.Single(Named(tokens, text));
}
