using Loom.LanguageServer;
using Microsoft.Extensions.Configuration;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.Testing.LanguageServer;

/// <summary>
///     The counts written above each declaration, and the settings that decide whether they are written at
///     all. Counting is the expensive half and is deferred to the resolve request, so most of these ask for a
///     lens and then resolve it, which is the round trip an editor actually makes.
/// </summary>
[Collection("Assembly")]
public class CodeLensTest
{
    private const string Source = """
        trait Describable {
            fn describe(): string;
        }

        interface Packet {
            name: string;
        }

        implement Describable for Packet {
            fn describe(): string -> name;
        }

        fn label(p: Packet): string -> p.describe();

        fn unused(): number -> 1;
        """;

    [Fact]
    public async Task Lenses_AreOfferedForEveryTopLevelDeclaration() =>
        await WithLensesAsync(
            (_, lenses) =>
            {
                Assert.Contains(lenses, lens => lens.Range.Start.Line == 0);
                Assert.Contains(lenses, lens => lens.Range.Start.Line == 4);
                Assert.Contains(lenses, lens => lens.Range.Start.Line == 12);
            }
        );

    /// <remarks>A member's own line above it is where its documentation goes; a lens there annotates more lines than it leaves alone.</remarks>
    [Fact]
    public async Task Lenses_AreNotOfferedForInterfaceMembers() =>
        await WithLensesAsync((_, lenses) => Assert.DoesNotContain(lenses, lens => lens.Range.Start.Line == 5));

    [Fact]
    public async Task Resolve_CountsThePlacesThatReferToTheDeclaration() =>
        await WithLensesAsync(
            async (handler, lenses) =>
            {
                var packet = await ResolveAsync(handler, lenses, line: 4, "reference");

                // the parameter's type, and the implement block's subject
                Assert.Equal("2 references", packet.Command!.Title);
            }
        );

    [Fact]
    public async Task Resolve_CountsATraitsImplementations() =>
        await WithLensesAsync(
            async (handler, lenses) =>
            {
                var trait = await ResolveAsync(handler, lenses, line: 0, "implementation");
                Assert.Equal("1 implementation", trait.Command!.Title);
            }
        );

    /// <remarks>A lens is read at a glance, and "1 references" snags.</remarks>
    [Fact]
    public async Task Resolve_WritesACountOfOneInTheSingular() =>
        await WithLensesAsync(
            async (handler, lenses) =>
            {
                var unused = await ResolveAsync(handler, lenses, line: 14, "reference");
                Assert.Equal("0 references", unused.Command!.Title);
            }
        );

    /// <remarks>A trait carries both counts, and one lens can only say one thing, so it gets one of each.</remarks>
    [Fact]
    public async Task Lenses_GiveATraitBothACountOfReferencesAndOneOfImplementations() =>
        await WithLensesAsync((_, lenses) => Assert.Equal(2, lenses.Count(lens => lens.Range.Start.Line == 0)));

    [Fact]
    public async Task Lenses_AreNotOfferedWhenBothSettingsAreOff() =>
        await WithLensesAsync(
            (_, lenses) => Assert.Empty(lenses),
            Settings(("loom:codeLens:references", "false"), ("loom:codeLens:implementations", "false"))
        );

    [Fact]
    public async Task Lenses_LeaveOutImplementationsWhenOnlyThatSettingIsOff() =>
        await WithLensesAsync(
            (_, lenses) =>
            {
                Assert.NotEmpty(lenses);
                Assert.Single(lenses, lens => lens.Range.Start.Line == 0);
            },
            Settings(("loom:codeLens:implementations", "false"))
        );

    [Fact]
    public void Settings_DefaultToOnWhenTheClientSendsNothing()
    {
        var settings = new ServerSettings();

        Assert.True(settings.CodeLensReferences);
        Assert.True(settings.CodeLensImplementations);
        Assert.True(settings.CodeLensEnabled);
    }

    /// <remarks>A misspelt preference must not stop the server answering; the default is what it falls back to.</remarks>
    [Fact]
    public void Settings_FallBackToTheDefaultForAValueThatIsNotABoolean() =>
        Assert.True(Settings(("loom:codeLens:references", "yes please")).CodeLensReferences);

    private static ServerSettings Settings(params (string Key, string Value)[] values) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(values.Select(entry => new KeyValuePair<string, string?>(entry.Key, entry.Value))).Build());

    private static async Task<CodeLens> ResolveAsync(CodeLensHandler handler, IReadOnlyList<CodeLens> lenses, int line, string noun)
    {
        foreach (var lens in lenses.Where(lens => lens.Range.Start.Line == line))
        {
            var resolved = await handler.Handle(lens, TestContext.Current.CancellationToken);
            if (resolved.Command?.Title.Contains(noun, StringComparison.Ordinal) == true)
                return resolved;
        }

        Assert.Fail($"no '{noun}' lens on line {line}");
        return null!;
    }

    private static Task WithLensesAsync(Action<CodeLensHandler, IReadOnlyList<CodeLens>> act, ServerSettings? settings = null) =>
        WithLensesAsync((handler, lenses) =>
            {
                act(handler, lenses);
                return Task.CompletedTask;
            },
            settings
        );

    private static async Task WithLensesAsync(Func<CodeLensHandler, IReadOnlyList<CodeLens>, Task> act, ServerSettings? settings = null) =>
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var handler = new CodeLensHandler(store, settings ?? new ServerSettings());
                var lenses = await handler.Handle(
                    new CodeLensParams { TextDocument = new TextDocumentIdentifier(uri) },
                    TestContext.Current.CancellationToken
                );

                await act(handler, lenses?.ToArray() ?? []);
            },
            Source
        );
}
