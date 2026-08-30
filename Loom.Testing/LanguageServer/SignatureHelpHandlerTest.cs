using Loom.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Loom.Testing;

namespace Loom.Testing.LanguageServer;

[Collection("Assembly")]
public class SignatureHelpHandlerTest
{
    private const string Prelude = """
                                   declare interface Maker {
                                     make: fn(name: string): string;
                                     make: fn(name: string, count: number): string;
                                   }

                                   declare let maker: Maker;

                                   ### Adds two numbers together.
                                   ### @param x the first addend
                                   ### @param y the second addend
                                   ### @returns their sum
                                   fn add(x: number, y: number): number {
                                     return x + y;
                                   }

                                   fn main(): void {

                                   """;

    [Fact]
    public async Task Handle_AtAnOpenParen_IsOnTheFirstParameter()
    {
        var help = await HelpAsync("  add(");
        Assert.Equal("fn add(x: number, y: number): number", Assert.Single(help!.Signatures).Label);
        Assert.Equal(0, help.ActiveParameter);
    }

    [Fact]
    public async Task Handle_AfterAComma_MovesToTheNextParameter() => Assert.Equal(1, (await HelpAsync("  add(1, "))!.ActiveParameter);

    [Fact]
    public async Task Handle_AfterTheClosingParen_OffersNothing() => Assert.Null(await HelpAsync("  add(1, 2)"));

    [Fact]
    public async Task Handle_InsideANestedCall_DescribesTheInnerOne()
    {
        var help = await HelpAsync("  add(add(1, ");
        Assert.Equal(1, help!.ActiveParameter);
        Assert.Equal("fn add(x: number, y: number): number", Assert.Single(help.Signatures).Label);
    }

    [Fact]
    public async Task Handle_AfterAnInnerCallCloses_ReturnsToTheOuterOne() => Assert.Equal(1, (await HelpAsync("  add(add(1, 2), "))!.ActiveParameter);

    [Fact]
    public async Task Handle_IgnoresCommasInsideAStringArgument() => Assert.Equal(0, (await HelpAsync("  print(\"a, b\""))!.ActiveParameter);

    [Fact]
    public async Task Handle_LabelsParametersBySpanSoRepeatedTypesStillHighlight()
    {
        var help = await HelpAsync("  add(");
        var signature = Assert.Single(help!.Signatures);
        var label = signature.Label;

        var parameters = signature.Parameters!.ToArray();
        Assert.Equal("x: number", Slice(label, parameters[0]));
        Assert.Equal("y: number", Slice(label, parameters[1]));
    }

    [Fact]
    public async Task Handle_ShowsTheFunctionsDocumentationAndThatOfEachParameter()
    {
        var help = await HelpAsync("  add(");
        var signature = Assert.Single(help!.Signatures);
        Assert.Equal("Adds two numbers together.", signature.Documentation?.MarkupContent?.Value);
        Assert.Equal("the first addend", signature.Parameters!.First().Documentation?.MarkupContent?.Value);
    }

    [Fact]
    public async Task Handle_ForAnOverloadSet_OffersEveryShapeWithItsOwnParameterNames()
    {
        var help = await HelpAsync("  maker.make(");
        Assert.Equal(
            ["fn make(name: string): string", "fn make(name: string, count: number): string"],
            help!.Signatures.Select(signature => signature.Label)
        );
    }

    [Fact]
    public async Task Handle_ForAnOverloadSet_PicksTheShapeTheArgumentsSoFarFit() =>
        Assert.Equal(1, (await HelpAsync("  maker.make(\"a\", "))!.ActiveSignature);

    [Fact]
    public async Task Handle_OnAnIntrinsicMethod_NamesItsParameters() =>
        Assert.Equal("fn clamp(x: number, min: number, max: number): number", Assert.Single((await HelpAsync("  math.clamp(1, "))!.Signatures).Label);

    [Fact]
    public async Task Handle_OutsideAnyCall_OffersNothing() => Assert.Null(await HelpAsync("  let x = 1;"));

    private static string Slice(string label, ParameterInformation parameter)
    {
        var (start, end) = parameter.Label.Range;
        return label[start..end];
    }

    private static async Task<SignatureHelp?> HelpAsync(string lastLine)
    {
        var source = Prelude + lastLine;
        var lines = source.Replace("\r\n", "\n").Split('\n');
        var position = new Position(lines.Length - 1, lines[^1].Length);

        SignatureHelp? help = null;
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
                help = await new SignatureHelpHandler(store).Handle(
                    new SignatureHelpParams { TextDocument = new TextDocumentIdentifier(uri), Position = position },
                    TestContext.Current.CancellationToken
                ),
            source + "\n}"
        );

        return help;
    }
}
