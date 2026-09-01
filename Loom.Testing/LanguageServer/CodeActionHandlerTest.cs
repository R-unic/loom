using Loom.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace Loom.Testing.LanguageServer;

[Collection("Assembly")]
public class CodeActionHandlerTest
{
    private static readonly (string Path, string Source) _math = ("util/math.loom", "export fn double(n: number): number { return n * 2; }");

    [Fact]
    public async Task Handle_ForAnUnknownName_OffersTheImportThatWouldResolveIt()
    {
        var actions = await ActionsAsync("let four = double(2);", 0, 11, _math);
        var action = Assert.Single(actions, entry => entry.Kind == CodeActionKind.QuickFix);

        Assert.Equal("Import 'double' from \"./util/math\"", action.Title);
        Assert.Equal(CodeActionKind.QuickFix, action.Kind);
        Assert.Equal("import { double } from \"./util/math\";\n", Assert.Single(Assert.Single(action.Edit!.Changes!).Value).NewText);
    }

    [Fact]
    public async Task Handle_ForAnUnknownNameNothingExports_OffersNothing() =>
        Assert.Empty(await ActionsAsync("let four = nowhere(2);", 0, 11, _math));

    [Fact]
    public async Task Handle_ForAnUnusedImport_OffersToRemoveTheWholeStatement()
    {
        var actions = await ActionsAsync("import { double } from \"./util/math\";\nlet x = 1;", 0, 9, _math);
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

    /// <remarks>
    ///     One code covers four different redundancies, so the fix has to be chosen by the syntax under the
    ///     diagnostic. These three are the ones that can be rewritten mechanically.
    /// </remarks>
    [Fact]
    public async Task Handle_ForABodyThatOnlyReturns_OffersAnExpressionBody()
    {
        var actions = await ActionsAsync("fn double(n: number): number {\n  return n * 2;\n}", 0, 0);
        var action = Assert.Single(actions, entry => entry.Title == "Use an expression body");

        var edit = Assert.Single(Assert.Single(action.Edit!.Changes!).Value);
        Assert.Equal("-> n * 2;", edit.NewText);
        Assert.Equal(29, edit.Range.Start.Character);
        Assert.Equal(2, edit.Range.End.Line);
    }

    /// <remarks>A void body has nothing to be the expression, so there is no rewrite to offer.</remarks>
    [Fact]
    public async Task Handle_ForABodyThatReturnsNothing_OffersNoExpressionBody() =>
        Assert.DoesNotContain(await ActionsAsync("fn stop(): void {\n  return;\n}", 0, 0), entry => entry.Title == "Use an expression body");

    [Fact]
    public async Task Handle_ForARedundantNullForgiving_OffersToRemoveIt()
    {
        var actions = await ActionsAsync("let x = 1;\nlet y = x!;", 1, 8);
        var action = Assert.Single(actions, entry => entry.Title == "Remove the redundant '!'");

        var edit = Assert.Single(Assert.Single(action.Edit!.Changes!).Value);
        Assert.Equal("", edit.NewText);
        Assert.Equal(9, edit.Range.Start.Character);
        Assert.Equal(10, edit.Range.End.Character);
    }

    [Fact]
    public async Task Handle_ForARedundantNullCoalesce_OffersToRemoveTheRightHandSide()
    {
        var actions = await ActionsAsync("let x = 1;\nlet y = x ?? 2;", 1, 8);
        var action = Assert.Single(actions, entry => entry.Title == "Remove the redundant '??'");

        var edit = Assert.Single(Assert.Single(action.Edit!.Changes!).Value);
        Assert.Equal("", edit.NewText);
        Assert.Equal(9, edit.Range.Start.Character);
        Assert.Equal(14, edit.Range.End.Character);
    }

    [Fact]
    public async Task Handle_ForUnreachableCode_OffersToRemoveTheLine()
    {
        var actions = await ActionsAsync("fn f(): number {\n  return 1;\n  print(2);\n}", 2, 2);
        var action = Assert.Single(actions, entry => entry.Title == "Remove unreachable code");

        var edit = Assert.Single(Assert.Single(action.Edit!.Changes!).Value);
        Assert.Equal("", edit.NewText);
        Assert.Equal(2, edit.Range.Start.Line);
        Assert.Equal(3, edit.Range.End.Line);
    }

    /// <remarks>Organize Imports is a command the user runs wherever the cursor is, not a fix on a warning, so it is offered away from one.</remarks>
    [Fact]
    public async Task OrganizeImports_RemovesEveryUnusedImportAtOnce()
    {
        var actions = await ActionsAsync(
            "import { double } from \"./util/math\";\nimport { triple } from \"./util/more\";\nlet x = 1;",
            2,
            0,
            _math,
            ("util/more.loom", "export fn triple(n: number): number { return n * 3; }")
        );

        var action = Assert.Single(actions, entry => entry.Kind == CodeActionKind.SourceOrganizeImports);
        Assert.Equal("Remove unused imports", action.Title);
        Assert.Equal(2, Assert.Single(action.Edit!.Changes!).Value.Count());
    }

    [Fact]
    public async Task OrganizeImports_IsNotOfferedWhenEveryImportIsUsed() =>
        Assert.DoesNotContain(
            await ActionsAsync("import { double } from \"./util/math\";\nlet four = double(2);", 1, 0, _math),
            entry => entry.Kind == CodeActionKind.SourceOrganizeImports
        );

    [Fact]
    public async Task FixAll_AppliesEveryUnambiguousFixInOneEdit()
    {
        var actions = await ActionsAsync("let x = 1;\nlet y = x!;\nlet z = x ?? 2;", 0, 0);

        var action = Assert.Single(actions, entry => entry.Kind == CodeActionKind.SourceFixAll);
        Assert.Equal("Fix all auto-fixable problems", action.Title);
        Assert.Equal(2, Assert.Single(action.Edit!.Changes!).Value.Count());
    }

    /// <remarks>A name two modules export has no one right fix, and choosing one for the user is not fixing it.</remarks>
    [Fact]
    public async Task FixAll_LeavesOutADiagnosticThatOffersAChoice() =>
        Assert.DoesNotContain(
            await ActionsAsync(
                "let four = double(2);",
                0,
                11,
                _math,
                ("util/more.loom", "export fn double(n: number): number { return n * 2; }")
            ),
            entry => entry.Kind == CodeActionKind.SourceFixAll
        );

    /// <remarks>The editor asks for one kind when the user runs a command, and for nothing in particular when the lightbulb opens.</remarks>
    [Fact]
    public async Task Handle_WhenTheClientAsksForOneKind_AnswersWithOnlyThatKind()
    {
        var actions = await ActionsAsync(
            "import { double } from \"./util/math\";\nlet y = 1;\nlet z = y!;",
            2,
            8,
            new Container<CodeActionKind>(CodeActionKind.SourceOrganizeImports),
            _math
        );

        Assert.All(actions, action => Assert.Equal(CodeActionKind.SourceOrganizeImports, action.Kind));
        Assert.NotEmpty(actions);
    }

    private static Task<CodeAction[]> ActionsAsync(string source, int line, int character, params (string Path, string Source)[] otherFiles) =>
        ActionsAsync(source, line, character, null, otherFiles);

    private static async Task<CodeAction[]> ActionsAsync(
        string source,
        int line,
        int character,
        Container<CodeActionKind>? only,
        params (string Path, string Source)[] otherFiles)
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
                        Context = new CodeActionContext { Only = only }
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
