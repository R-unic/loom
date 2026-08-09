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
    public async Task Handle_ResolvesTheTypeDetailLazily()
    {
        var completions = await CompleteAsync(
            """
            fn greet(name: string): string {
              return name;
            }

            let chosen =
            """
        );

        var greet = Assert.Single(completions, item => item.Label == "greet");
        Assert.Null(greet.LabelDetails);

        var handler = new CompletionHandler(new DocumentStore());
        var resolved = await handler.Handle(greet, TestContext.Current.CancellationToken);
        Assert.Equal(" fn(string): string", resolved.LabelDetails?.Detail);
    }

    [Fact]
    public async Task Handle_InsideAnImportSpecifier_CompletesSiblingModules()
    {
        var directory = Path.Combine(Path.GetTempPath(), "loom-lsp-test-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(directory, "src", "util"));
        try
        {
            File.WriteAllText(Path.Combine(directory, "loom-config.toml"), "[files]\nsource_directory = \"src\"\noutput_directory = \"dist\"\n");
            File.WriteAllText(Path.Combine(directory, "src", "util", "math.loom"), "export fn double(n: number): number { return n * 2; }");
            var path = Path.Combine(directory, "src", "main.loom");
            var source = "import { double } from \"\";";
            File.WriteAllText(path, source);

            var store = new DocumentStore();
            var uri = DocumentUri.FromFileSystemPath(path);
            store.Open(uri, source);

            var completions = await new CompletionHandler(store).Handle(
                new CompletionParams { TextDocument = new TextDocumentIdentifier(uri), Position = new Position(0, 24) },
                TestContext.Current.CancellationToken
            );

            var specifier = Assert.Single(completions);
            Assert.Equal("./util/math", specifier.Label);
            Assert.Equal(CompletionItemKind.Module, specifier.Kind);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

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
        var directory = Path.Combine(Path.GetTempPath(), "loom-lsp-test-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(directory, "src"));
        try
        {
            File.WriteAllText(Path.Combine(directory, "loom-config.toml"), "[files]\nsource_directory = \"src\"\noutput_directory = \"dist\"\n");
            var path = Path.Combine(directory, "src", "main.loom");
            File.WriteAllText(path, source);

            var store = new DocumentStore();
            var uri = DocumentUri.FromFileSystemPath(path);
            store.Open(uri, source);

            var position = line != null && character != null
                ? new Position(line.Value, character.Value)
                : EndOfPenultimateLine(source);

            var completions = await new CompletionHandler(store).Handle(
                new CompletionParams { TextDocument = new TextDocumentIdentifier(uri), Position = position },
                TestContext.Current.CancellationToken
            );

            return completions.ToArray();
        }
        finally
        {
            Directory.Delete(directory, true);
        }
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
