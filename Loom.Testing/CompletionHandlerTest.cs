using Loom.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.Testing;

[Collection("Assembly")]
public class CompletionHandlerTest
{
    [Fact]
    public async Task Handle_AfterADot_CompletesTheReceiversMembersOnly()
    {
        var completions = await CompleteAsync(
            """
            interface Player {
              name: string;
              score: number;
            }

            fn main(p: Player): void {
              p.
            }
            """
        );

        Assert.Equal(["name", "score"], completions.Select(item => item.Label).Order());
        Assert.Equal(CompletionItemKind.Property, Assert.Single(completions, item => item.Label == "name").Kind);
    }

    [Fact]
    public async Task Handle_AfterADot_FollowsAChainOfMembers()
    {
        var completions = await CompleteAsync(
            """
            interface Inner {
              deep: string;
            }

            interface Outer {
              inner: Inner;
            }

            fn main(o: Outer): void {
              o.inner.
            }
            """
        );

        Assert.Equal("deep", Assert.Single(completions).Label);
    }

    [Fact]
    public async Task Handle_InATypeAnnotation_CompletesTypesRatherThanValues()
    {
        var completions = await CompleteAsync(
            """
            interface Player {
              name: string;
            }

            fn main(): void {
              let count = 1;
              let chosen:
            }
            """
        );

        var labels = completions.Select(item => item.Label).ToArray();
        Assert.Contains("Player", labels);
        Assert.DoesNotContain("count", labels);
    }

    [Fact]
    public async Task Handle_InATypeAnnotation_OffersTheBuiltInTypeNames()
    {
        var labels = (await CompleteAsync("fn main(): void {\n  let a: n\n}", line: 1, character: 10)).Select(item => item.Label).ToArray();
        Assert.Contains("number", labels);
        Assert.Contains("never", labels);
        Assert.Contains("none", labels);
    }

    [Fact]
    public async Task Handle_InAHalfWrittenTypeArgument_StillCompletesTypes()
    {
        var labels = (await CompleteAsync("let a: Array<n", line: 0, character: 14)).Select(item => item.Label).ToArray();
        Assert.Contains("number", labels);
        Assert.DoesNotContain("number_range", labels);
    }

    [Fact]
    public async Task Handle_AfterAFinishedAnnotation_GoesBackToCompletingValues()
    {
        var labels = (await CompleteAsync("fn main(): void {\n  let a: number = pri\n}", line: 1, character: 21)).Select(item => item.Label).ToArray();
        Assert.Contains("print", labels);
    }

    [Fact]
    public async Task Handle_InANextParameterName_IsNotATypePosition()
    {
        var labels = (await CompleteAsync("fn main(a: number, b", line: 0, character: 20)).Select(item => item.Label).ToArray();
        Assert.Contains("break", labels);
        Assert.DoesNotContain("bool", labels);
    }

    [Fact]
    public async Task Handle_InAValuePosition_LeavesOutTypesAndOutOfScopeLocals()
    {
        var completions = await CompleteAsync(
            """
            interface Player {
              name: string;
            }

            fn other(): void {
              let hidden = 1;
            }

            fn main(): void {
              let visible = 2;
              let chosen =
            }
            """
        );

        var labels = completions.Select(item => item.Label).ToArray();
        Assert.Contains("visible", labels);
        Assert.Contains("other", labels);
        Assert.DoesNotContain("hidden", labels);
    }

    [Fact]
    public async Task Handle_InAStatementPosition_OffersKeywords()
    {
        var keyword = Assert.Single(await CompleteAsync("fn main(): void {\n  ret\n}", line: 1, character: 5));
        Assert.Equal("return", keyword.Label);
        Assert.Equal(CompletionItemKind.Keyword, keyword.Kind);
    }

    [Fact]
    public async Task Handle_AfterADot_OffersNoKeywords() =>
        Assert.Equal("floor", Assert.Single(await CompleteAsync("fn main(): void {\n  math.fl\n}", line: 1, character: 9)).Label);

    [Fact]
    public async Task Handle_InsideAnAttributeList_CompletesAttributes()
    {
        var completions = await CompleteAsync(
            """
            [se]
            interface Packet {
              id: u8;
            }
            """,
            line: 0,
            character: 3
        );

        var labels = completions.Select(item => item.Label).ToArray();
        Assert.Contains("serializable", labels);
        Assert.DoesNotContain("Packet", labels);
    }

    [Fact]
    public async Task Handle_InsideAnEnumIndex_CompletesEnumMembers()
    {
        var completions = await CompleteAsync(
            """
            enum Message {
              Hello,
              Goodbye
            }

            let picked = Message[""];
            """,
            line: 5,
            character: 21
        );

        Assert.Equal(["Goodbye", "Hello"], completions.Select(item => item.Label).Order());
    }

    [Fact]
    public async Task Handle_FiltersByThePrefixAlreadyTyped()
    {
        var completions = await CompleteAsync(
            """
            interface Player {
              name: string;
              nickname: string;
              score: number;
            }

            fn main(p: Player): void {
              p.n
            }
            """
        );

        Assert.Equal(["name", "nickname"], completions.Select(item => item.Label).Order());
    }

    [Fact]
    public async Task Handle_SortsNamesThisFileDeclaresAboveTheOnesAlwaysInScope()
    {
        var completions = await CompleteAsync(
            """
            fn printer(): void { }

            fn main(): void {
              print
            }
            """,
            line: 3,
            character: 7
        );

        Assert.StartsWith("0", Assert.Single(completions, item => item.Label == "printer").SortText);
        Assert.StartsWith("1", Assert.Single(completions, item => item.Label == "print").SortText);
    }

    [Fact]
    public async Task Handle_ResolvesTheSignatureAndDocumentationLazily() =>
        await Utility.WithLspProject(
            async (store, uri) =>
            {
                var handler = new CompletionHandler(store);
                var completions = await handler.Handle(
                    new CompletionParams { TextDocument = new TextDocumentIdentifier(uri), Position = new Position(5, 13) },
                    TestContext.Current.CancellationToken
                );

                var greet = Assert.Single(completions, item => item.Label == "greet");
                Assert.Null(greet.LabelDetails);
                Assert.Null(greet.Documentation);

                var resolved = await handler.Handle(greet, TestContext.Current.CancellationToken);
                Assert.Equal("(name: string): string", resolved.LabelDetails?.Detail);

                var documentation = resolved.Documentation?.MarkupContent?.Value ?? "";
                Assert.Contains("fn greet(name: string): string", documentation);
                Assert.Contains("Greets somebody.", documentation);
            },
            """
            ### Greets somebody.
            fn greet(name: string): string {
              return name;
            }

            let chosen = gre
            """
        );

    [Fact]
    public async Task Handle_InsideAnImportSpecifier_CompletesSiblingModules() =>
        await Utility.WithLspProject(
            async (store, uri) =>
            {
                var completions = await new CompletionHandler(store).Handle(
                    new CompletionParams { TextDocument = new TextDocumentIdentifier(uri), Position = new Position(0, 24) },
                    TestContext.Current.CancellationToken
                );

                var specifier = Assert.Single(completions);
                Assert.Equal("./util/math", specifier.Label);
                Assert.Equal(CompletionItemKind.Module, specifier.Kind);
            },
            "import { double } from \"\";",
            ("util/math.loom", "export fn double(n: number): number { return n * 2; }")
        );

    [Fact]
    public async Task Handle_InsideAnImportList_CompletesTheModulesExportsAndNothingElse() =>
        await Utility.WithLspProject(
            async (store, uri) =>
            {
                var completions = await new CompletionHandler(store).Handle(
                    new CompletionParams { TextDocument = new TextDocumentIdentifier(uri), Position = new Position(0, 9) },
                    TestContext.Current.CancellationToken
                );

                Assert.Equal(["Vec", "double", "triple"], completions.Select(item => item.Label).Order(StringComparer.Ordinal));
            },
            "import {  } from \"./util/math\";",
            (
                "util/math.loom",
                """
                export fn double(n: number): number { return n * 2; }
                export fn triple(n: number): number { return n * 3; }
                export interface Vec { x: number; }
                """
            )
        );

    [Fact]
    public async Task Handle_ForAnUnknownDocument_ReturnsNoCompletions()
    {
        var handler = new CompletionHandler(new DocumentStore());
        var uri = DocumentUri.FromFileSystemPath(Path.Combine(Path.GetTempPath(), "does-not-exist.loom"));

        var completions = await handler.Handle(
            new CompletionParams { TextDocument = new TextDocumentIdentifier(uri), Position = new Position(0, 0) },
            TestContext.Current.CancellationToken
        );

        Assert.Empty(completions);
    }

    private static async Task<CompletionItem[]> CompleteAsync(string source, int? line = null, int? character = null)
    {
        var completions = Array.Empty<CompletionItem>();
        await Utility.WithLspProject(
            async (store, uri) =>
            {
                var position = line != null && character != null ? new Position(line.Value, character.Value) : EndOfPenultimateLine(source);
                var result = await new CompletionHandler(store).Handle(
                    new CompletionParams { TextDocument = new TextDocumentIdentifier(uri), Position = position },
                    TestContext.Current.CancellationToken
                );

                completions = result.ToArray();
            },
            source
        );

        return completions;
    }

    private static Position EndOfPenultimateLine(string source)
    {
        var lines = source.Replace("\r\n", "\n").Split('\n');
        var index = lines.Length - 1;
        while (index > 0 && lines[index].Trim().Length == 0)
            index--;

        if (lines[index].Trim() == "}")
            index--;

        return new Position(index, lines[index].TrimEnd().Length);
    }
}
