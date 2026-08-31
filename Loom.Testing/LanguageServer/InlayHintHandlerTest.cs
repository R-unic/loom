using Loom.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace Loom.Testing.LanguageServer;

[Collection("Assembly")]
public class InlayHintHandlerTest
{
    [Fact]
    public async Task Handle_AnnotatesAnInferredBindingWithItsType()
    {
        var hint = Assert.Single(await HintsAsync("let count = 1;"));
        Assert.Equal(": 1", hint.Label.String);
        Assert.Equal(InlayHintKind.Type, hint.Kind);
        Assert.Equal(9, hint.Position.Character);
    }

    [Fact]
    public async Task Handle_LeavesAnAnnotatedBindingAlone() => Assert.Empty(await HintsAsync("let count: number = 1;"));

    [Fact]
    public async Task Handle_AnInferredBindingHintInsertsTheAnnotationItShows()
    {
        var edit = Assert.Single(Assert.Single(await HintsAsync("let count = 1;")).TextEdits!);
        Assert.Equal(": 1", edit.NewText);
        Assert.Equal(edit.Range.Start, edit.Range.End);
        Assert.Equal(9, edit.Range.Start.Character);
    }

    [Fact]
    public async Task Handle_AnnotatesAnInferredReturnType()
    {
        var hints = await HintsAsync("fn double(x: number) -> x * 2");
        var hint = Assert.Single(hints, entry => entry.Kind == InlayHintKind.Type);

        Assert.Equal(": number", hint.Label.String);
        Assert.Equal(20, hint.Position.Character);
    }

    [Fact]
    public async Task Handle_LeavesAnAnnotatedReturnTypeAlone() =>
        Assert.DoesNotContain(await HintsAsync("fn double(x: number): number { return x * 2; }"), hint => hint.Kind == InlayHintKind.Type);

    [Fact]
    public async Task Handle_NamesTheParameterEachArgumentIsPassedAs()
    {
        var hints = await HintsAsync(
            """
            fn move_to(x: number, y: number): void { }

            fn main(): void {
              move_to(1, 2);
            }
            """
        );

        var parameters = hints.Where(hint => hint.Kind == InlayHintKind.Parameter).ToArray();
        Assert.Equal(["x:", "y:"], parameters.Select(hint => hint.Label.String));
        Assert.Equal(10, parameters[0].Position.Character);
    }

    [Fact]
    public async Task Handle_SkipsAnArgumentAlreadyWrittenAsTheParametersName()
    {
        var hints = await HintsAsync(
            """
            fn move_to(x: number, y: number): void { }

            fn main(): void {
              let x = 1;
              move_to(x, 2);
            }
            """
        );

        Assert.Equal("y:", Assert.Single(hints, hint => hint.Kind == InlayHintKind.Parameter).Label.String);
    }

    [Fact]
    public async Task Handle_SkipsArgumentsSwallowedByARestParameter() =>
        Assert.DoesNotContain(await HintsAsync("fn main(): void {\n  print(1, 2, 3);\n}"), hint => hint.Kind == InlayHintKind.Parameter);

    [Fact]
    public async Task Handle_LeavesOutHintsOutsideTheRequestedRange()
    {
        var hints = await HintsAsync(
            "let first = 1;\nlet second = 2;",
            new Range(new Position(1, 0), new Position(1, 15))
        );

        Assert.Equal(": 2", Assert.Single(hints).Label.String);
    }

    [Fact]
    public async Task Handle_ForAnUnknownDocument_ReturnsNothing()
    {
        var handler = new InlayHintHandler(new DocumentStore());
        var uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.FromFileSystemPath(Path.Combine(Path.GetTempPath(), "does-not-exist.loom"));

        Assert.Null(
            await handler.Handle(
                new InlayHintParams { TextDocument = new TextDocumentIdentifier(uri), Range = new Range(new Position(0, 0), new Position(0, 0)) },
                TestContext.Current.CancellationToken
            )
        );
    }

    private static async Task<InlayHint[]> HintsAsync(string source, Range? range = null)
    {
        var hints = Array.Empty<InlayHint>();
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var lines = source.Replace("\r\n", "\n").Split('\n');
                var whole = new Range(new Position(0, 0), new Position(lines.Length - 1, lines[^1].Length));
                var result = await new InlayHintHandler(store).Handle(
                    new InlayHintParams { TextDocument = new TextDocumentIdentifier(uri), Range = range ?? whole },
                    TestContext.Current.CancellationToken
                );

                hints = result?.ToArray() ?? [];
            },
            source
        );

        return hints;
    }
}
