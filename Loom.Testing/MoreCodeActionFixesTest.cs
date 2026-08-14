using Loom.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace Loom.Testing;

/// <summary>
///     The mechanical half of <c>CodeActionHandlerTest</c>: fixes that follow the compiler's own hint word for
///     word, added once reference search and the module graph cache made the rest of the handler cheap enough
///     to be worth extending.
/// </summary>
[Collection("Assembly")]
public class MoreCodeActionFixesTest
{
    private static readonly (string Path, string Source) Math = ("util/math.loom", "export fn double(n: number): number -> n * 2;");

    [Fact]
    public async Task Handle_ForATypeOnlyImportOfAValue_OffersToDropTheKeyword()
    {
        var actions = await ActionsAsync("import type { double } from \"./util/math\";\nlet x = 1;", 0, 15, Math);
        var action = Assert.Single(actions, entry => entry.Title == "Remove 'type' from the import");

        var edit = Assert.Single(Assert.Single(action.Edit!.Changes!).Value);
        Assert.Equal("", edit.NewText);
    }

    /// <remarks>The space either side of 'type' collapses to one, not zero or two.</remarks>
    [Fact]
    public async Task Handle_ForATypeOnlyImportOfAValue_LeavesOneSpaceBetweenTheKeywords()
    {
        var actions = await ActionsAsync("import type { double } from \"./util/math\";\nlet x = 1;", 0, 15, Math);
        var action = Assert.Single(actions, entry => entry.Title == "Remove 'type' from the import");
        var edit = Assert.Single(Assert.Single(action.Edit!.Changes!).Value);

        var applied = Apply("import type { double } from \"./util/math\";\nlet x = 1;", edit);
        Assert.StartsWith("import { double } from", applied);
    }

    [Fact]
    public async Task Handle_ForATypeOnlyExportOfAValue_OffersToDropTheKeyword()
    {
        var actions = await ActionsAsync("export type { double } from \"./util/math\";", 0, 15, Math);
        var action = Assert.Single(actions, entry => entry.Title == "Remove 'type' from the export");
        var edit = Assert.Single(Assert.Single(action.Edit!.Changes!).Value);

        Assert.Equal("export { double } from \"./util/math\";", Apply("export type { double } from \"./util/math\";", edit));
    }

    [Fact]
    public async Task Handle_ForALocalTypeOnlyExportOfAValue_OffersToDropTheKeyword()
    {
        var actions = await ActionsAsync("fn double(n: number): number -> n * 2;\nexport type { double };", 1, 15);
        var action = Assert.Single(actions, entry => entry.Title == "Remove 'type' from the export");
        var edit = Assert.Single(Assert.Single(action.Edit!.Changes!).Value);

        Assert.Equal(
            "fn double(n: number): number -> n * 2;\nexport { double };",
            Apply("fn double(n: number): number -> n * 2;\nexport type { double };", edit)
        );
    }

    [Fact]
    public async Task Handle_ForAnExportedMutableVariable_OffersToUseLet()
    {
        var actions = await ActionsAsync("export mut x = 1;", 0, 0);
        var action = Assert.Single(actions, entry => entry.Title == "Use 'let' instead of 'mut'");
        var edit = Assert.Single(Assert.Single(action.Edit!.Changes!).Value);

        Assert.Equal("let", edit.NewText);
        Assert.Equal("export let x = 1;", Apply("export mut x = 1;", edit));
    }

    [Fact]
    public async Task Handle_ForAsyncAndNoYieldTogether_OffersToDropEither()
    {
        var actions = await ActionsAsync("[no_yield]\nasync fn f(): number -> 1;", 1, 0);

        var dropAsync = Assert.Single(actions, entry => entry.Title == "Drop 'async'");
        var dropAttribute = Assert.Single(actions, entry => entry.Title == "Drop '[no_yield]'");

        Assert.Equal(
            "[no_yield]\nfn f(): number -> 1;",
            Apply("[no_yield]\nasync fn f(): number -> 1;", Assert.Single(Assert.Single(dropAsync.Edit!.Changes!).Value))
        );

        Assert.Equal(
            "async fn f(): number -> 1;",
            Apply("[no_yield]\nasync fn f(): number -> 1;", Assert.Single(Assert.Single(dropAttribute.Edit!.Changes!).Value))
        );
    }

    /// <remarks>Removing one attribute out of several has to keep the brackets and the rest of the list, not just delete the whole line.</remarks>
    [Fact]
    public async Task Handle_ForNoYieldAmongOtherAttributes_RemovesOnlyThatName()
    {
        var actions = await ActionsAsync("[fallible, no_yield]\nasync fn f(): number -> 1;", 1, 0);
        var dropAttribute = Assert.Single(actions, entry => entry.Title == "Drop '[no_yield]'");

        Assert.Equal(
            "[fallible]\nasync fn f(): number -> 1;",
            Apply("[fallible, no_yield]\nasync fn f(): number -> 1;", Assert.Single(Assert.Single(dropAttribute.Edit!.Changes!).Value))
        );
    }

    private static string Apply(string source, TextEdit edit)
    {
        var lines = source.Split('\n');
        var start = OffsetOf(lines, edit.Range.Start);
        var end = OffsetOf(lines, edit.Range.End);
        return source[..start] + edit.NewText + source[end..];
    }

    private static int OffsetOf(string[] lines, Position position)
    {
        var offset = 0;
        for (var i = 0; i < position.Line; i++)
            offset += lines[i].Length + 1;

        return offset + (int)position.Character;
    }

    private static async Task<CodeAction[]> ActionsAsync(string source, int line, int character, params (string Path, string Source)[] otherFiles)
    {
        var actions = Array.Empty<CodeAction>();
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var result = await new CodeActionHandler(store).Handle(
                    new CodeActionParams
                    {
                        TextDocument = new TextDocumentIdentifier(uri),
                        Range = new Range(new Position(line, character), new Position(line, character)),
                        Context = new CodeActionContext()
                    },
                    TestContext.Current.CancellationToken
                );

                actions = result?.Where(entry => entry.CodeAction != null).Select(entry => entry.CodeAction!).ToArray() ?? [];
            },
            source,
            otherFiles
        );

        return actions;
    }
}
