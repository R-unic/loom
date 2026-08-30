using Loom.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.Testing.LanguageServer;

[Collection("Assembly")]
public class FoldingRangeHandlerTest
{
    [Fact]
    public async Task Handle_FoldsAFunctionBodyUpToTheLineBeforeItsBrace()
    {
        var range = Assert.Single(await FoldsAsync("fn main(): void {\n  print(1);\n  print(2);\n}"));
        Assert.Equal(0, range.StartLine);
        Assert.Equal(2, range.EndLine);
        Assert.Null(range.Kind);
    }

    [Fact]
    public async Task Handle_LeavesASingleLineBodyAlone() => Assert.Empty(await FoldsAsync("fn main(): void { print(1); }"));

    [Fact]
    public async Task Handle_FoldsAnInterfaceBody()
    {
        var range = Assert.Single(await FoldsAsync("interface Player {\n  name: string;\n  score: number;\n}"));
        Assert.Equal(0, range.StartLine);
        Assert.Equal(2, range.EndLine);
    }

    [Fact]
    public async Task Handle_FoldsAnEnumBody() => Assert.Single(await FoldsAsync("enum Message {\n  Hello,\n  Goodbye\n}"));

    [Fact]
    public async Task Handle_FoldsNestedBlocksSeparately()
    {
        var folds = await FoldsAsync(
            """
            fn main(): void {
              if (true) {
                print(1);
              }
            }
            """
        );

        Assert.Equal(2, folds.Length);
        Assert.Contains(folds, fold => fold is { StartLine: 0, EndLine: 3 });
        Assert.Contains(folds, fold => fold is { StartLine: 1, EndLine: 2 });
    }

    [Fact]
    public async Task Handle_FoldsARunOfLineComments()
    {
        var folds = await FoldsAsync("## one\n## two\n## three\nlet x = 1;");
        var comment = Assert.Single(folds, fold => fold.Kind == FoldingRangeKind.Comment);

        Assert.Equal(0, comment.StartLine);
        Assert.Equal(2, comment.EndLine);
    }

    [Fact]
    public async Task Handle_DoesNotFoldALoneLineComment() => Assert.Empty(await FoldsAsync("## just one\nlet x = 1;"));

    [Fact]
    public async Task Handle_TreatsCodeBetweenCommentsAsBreakingTheRun() =>
        Assert.Empty(await FoldsAsync("## one\nlet x = 1;\n## two\nlet y = 2;"));

    [Fact]
    public async Task Handle_FoldsAMultiLineBlockComment()
    {
        var comment = Assert.Single(await FoldsAsync("#: one\n   two :#\nlet x = 1;"), fold => fold.Kind == FoldingRangeKind.Comment);
        Assert.Equal(0, comment.StartLine);
        Assert.Equal(1, comment.EndLine);
    }

    [Fact]
    public async Task Handle_FoldsTheLeadingImportBlock() =>
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var folds = await new FoldingRangeHandler(store).Handle(
                    new FoldingRangeRequestParam { TextDocument = new TextDocumentIdentifier(uri) },
                    TestContext.Current.CancellationToken
                );

                var imports = Assert.Single(folds!, fold => fold.Kind == FoldingRangeKind.Imports);
                Assert.Equal(0, imports.StartLine);
                Assert.Equal(1, imports.EndLine);
            },
            "import { double } from \"./util/math\";\nimport { triple } from \"./util/more\";\nlet a = double(2) + triple(3);",
            ("util/math.loom", "export fn double(n: number): number { return n * 2; }"),
            ("util/more.loom", "export fn triple(n: number): number { return n * 3; }")
        );

    [Fact]
    public async Task Handle_ForAnUnknownDocument_ReturnsNothing()
    {
        var handler = new FoldingRangeHandler(new DocumentStore());
        var uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.FromFileSystemPath(Path.Combine(Path.GetTempPath(), "does-not-exist.loom"));

        Assert.Null(
            await handler.Handle(new FoldingRangeRequestParam { TextDocument = new TextDocumentIdentifier(uri) }, TestContext.Current.CancellationToken)
        );
    }

    private static async Task<FoldingRange[]> FoldsAsync(string source)
    {
        var folds = Array.Empty<FoldingRange>();
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var result = await new FoldingRangeHandler(store).Handle(
                    new FoldingRangeRequestParam { TextDocument = new TextDocumentIdentifier(uri) },
                    TestContext.Current.CancellationToken
                );

                folds = result?.ToArray() ?? [];
            },
            source
        );

        return folds;
    }
}
