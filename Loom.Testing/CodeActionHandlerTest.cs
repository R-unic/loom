using Loom.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace Loom.Testing;

[Collection("Assembly")]
public class CodeActionHandlerTest
{
    private static readonly (string Path, string Source) Math = ("util/math.loom", "export fn double(n: number): number { return n * 2; }");

    [Fact]
    public async Task Handle_ForAnUnknownName_OffersTheImportThatWouldResolveIt()
    {
        var actions = await ActionsAsync("let four = double(2);", 0, 11, Math);
        var action = Assert.Single(actions);

        Assert.Equal("Import 'double' from \"./util/math\"", action.Title);
        Assert.Equal(CodeActionKind.QuickFix, action.Kind);
        Assert.Equal("import { double } from \"./util/math\";\n", Assert.Single(Assert.Single(action.Edit!.Changes!).Value).NewText);
    }

    [Fact]
    public async Task Handle_ForAnUnknownNameNothingExports_OffersNothing() =>
        Assert.Empty(await ActionsAsync("let four = nowhere(2);", 0, 11, Math));

    [Fact]
    public async Task Handle_ForAnUnusedImport_OffersToRemoveTheWholeStatement()
    {
        var actions = await ActionsAsync("import { double } from \"./util/math\";\nlet x = 1;", 0, 9, Math);
        var action = Assert.Single(actions, entry => entry.Title == "Remove unused import");

        var edit = Assert.Single(Assert.Single(action.Edit!.Changes!).Value);
        Assert.Equal("", edit.NewText);
        Assert.Equal(0, edit.Range.Start.Line);
        Assert.Equal(1, edit.Range.End.Line);
    }

    [Fact]
    public async Task Handle_ForOneUnusedNameAmongSeveral_RemovesOnlyThatName()
    {
        var actions = await ActionsAsync(
            "import { double, triple } from \"./util/math\";\nlet six = triple(2);",
            0,
            9,
            ("util/math.loom", "export fn double(n: number): number { return n * 2; }\nexport fn triple(n: number): number { return n * 3; }")
        );

        var action = Assert.Single(actions, entry => entry.Title == "Remove 'double' from the import");
        var edit = Assert.Single(Assert.Single(action.Edit!.Changes!).Value);

        Assert.Equal(9, edit.Range.Start.Character);
        Assert.Equal(17, edit.Range.End.Character);
    }

    [Fact]
    public async Task Handle_ForAPanicOutsideAFallibleFunction_OffersToMarkIt()
    {
        var actions = await ActionsAsync(
            """
            fn read(): number {
              let result: Result<number, Error> = ok(1);
              return result.unwrap();
            }
            """,
            2,
            17
        );

        var action = Assert.Single(actions, entry => entry.Title == "Mark 'read' as '[fallible]'");
        var edit = Assert.Single(Assert.Single(action.Edit!.Changes!).Value);

        Assert.Equal("[fallible]\n", edit.NewText);
        Assert.Equal(0, edit.Range.Start.Line);
        Assert.Equal(0, edit.Range.Start.Character);
    }

    [Fact]
    public async Task Handle_OffersNothingWhereThereIsNoDiagnostic() => Assert.Empty(await ActionsAsync("let x = 1;", 0, 4));

    [Fact]
    public async Task Handle_ForAnUnknownDocument_ReturnsNothing()
    {
        var handler = new CodeActionHandler(new DocumentStore());
        var uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.FromFileSystemPath(Path.Combine(Path.GetTempPath(), "does-not-exist.loom"));

        Assert.Null(
            await handler.Handle(
                new CodeActionParams
                {
                    TextDocument = new TextDocumentIdentifier(uri),
                    Range = new Range(new Position(0, 0), new Position(0, 0)),
                    Context = new CodeActionContext()
                },
                TestContext.Current.CancellationToken
            )
        );
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

                actions = result?.Select(entry => entry.CodeAction!).OfType<CodeAction>().ToArray() ?? [];
            },
            source,
            otherFiles
        );

        return actions;
    }
}
