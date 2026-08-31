using Loom.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.Testing.LanguageServer;

[Collection("Assembly")]
public class HoverHandlerTest
{
    private const string Source = """
                                  ### Adds two numbers together.
                                  ###
                                  ### @param x the first addend
                                  ### @param y the second addend
                                  ### @returns their sum
                                  fn add(x: number, y: number): number {
                                    return x + y;
                                  }

                                  ### The packet sent on join.
                                  [serializable]
                                  interface Packet {
                                    ### The player's display name.
                                    name: string;
                                  }

                                  fn main(p: Packet): void {
                                    mut total = add(1, 2);
                                    print(total);
                                    print(p.name);
                                    print(math.floor(1.5));
                                  }
                                  """;

    [Fact]
    public async Task Handle_OnAFunction_ShowsTheSignatureWithParameterNames() =>
        Assert.Contains("fn add(x: number, y: number): number", await HoverAsync(Source, 5, 4));

    [Fact]
    public async Task Handle_OnAFunction_ShowsItsDocComment()
    {
        var hover = await HoverAsync(Source, 5, 4);
        Assert.Contains("Adds two numbers together.", hover);
        Assert.Contains("- `x` — the first addend", hover);
        Assert.Contains("**Returns** — their sum", hover);
    }

    [Fact]
    public async Task Handle_AtACallSite_DescribesTheFunctionBeingCalled() => Assert.Contains("fn add(x: number, y: number): number", await HoverAsync(Source, 17, 15));

    [Fact]
    public async Task Handle_OnAnInterface_ShowsItsAttributes()
    {
        var hover = await HoverAsync(Source, 11, 11);
        Assert.Contains("[serializable]\ninterface Packet", hover);
        Assert.Contains("The packet sent on join.", hover);
    }

    [Fact]
    public async Task Handle_OnAMutableLocal_ShowsTheMutKeyword() => Assert.Contains("mut total: number", await HoverAsync(Source, 17, 7));

    [Fact]
    public async Task Handle_OnAParameter_ShowsItsName() => Assert.Contains("x: number", await HoverAsync(Source, 5, 7));

    [Fact]
    public async Task Handle_OnAnIntrinsic_SaysWhereItCameFrom()
    {
        var hover = await HoverAsync(Source, 18, 4);
        Assert.Contains("fn print(..data: unknown[]): void", hover);
        Assert.Contains("*intrinsic*", hover);
    }

    [Fact]
    public async Task Handle_OnAMember_ShowsThePropertysOwnDocComment()
    {
        var hover = await HoverAsync(Source, 19, 12);
        Assert.Contains("name: string", hover);
        Assert.Contains("The player's display name.", hover);
    }

    [Fact]
    public async Task Handle_OnAMethod_RendersItAsACallRatherThanAProperty() => Assert.Contains("fn floor(x: number): number", await HoverAsync(Source, 20, 15));

    /// <summary>
    ///     A trait method is a FunctionSymbol on a separate `implement` block rather than a PropertySymbol of
    ///     the interface, which the member lookup has to consider or a call site describes nothing.
    /// </summary>
    [Fact]
    public async Task Handle_OnATraitMethodCall_DescribesTheImplementation()
    {
        var hover = await HoverAsync(
            """
            trait ToString {
                fn to_string: string;
            }

            interface User {
                name: string;
            }

            implement ToString for User {
                fn to_string -> name
            }

            fn main(): void {
              let user = new User { name: "Poppy" };
              print(user.to_string());
            }
            """,
            14,
            17
        );

        Assert.Contains("to_string", hover);
    }

    [Fact]
    public async Task Handle_OnAnIntrinsic_ShowsTheDocCommentTheIntrinsicWasDeclaredWith()
    {
        var hover = await HoverAsync(Source, 18, 4);
        Assert.Contains("Writes its arguments to the output", hover);
    }

    [Fact]
    public async Task Handle_OnADeprecatedDeclaration_SaysSo()
    {
        var hover = await HoverAsync(
            """
            [deprecated("use 'add' instead")]
            fn sum(x: number, y: number): number {
              return x + y;
            }
            """,
            1,
            4
        );

        Assert.Contains("**Deprecated** — use 'add' instead", hover);
    }

    [Fact]
    public async Task Handle_OnALiteral_ShowsItsTypeDirectly() => Assert.Contains("```loom\n1\n```", await HoverAsync("let x = 1;\nprint(x);", 0, 8));

    [Fact]
    public async Task Handle_ThroughAChainedMemberAccess_DescribesTheFinalMember()
    {
        var hover = await HoverAsync(
            """
            interface Position {
              x: number;
            }

            interface Vec {
              pos: Position;
            }

            fn main(v: Vec): void {
              print(v.pos.x);
            }
            """,
            9,
            14
        );

        Assert.Contains("x: number", hover);
    }

    /// <summary>A member that does not exist on the receiver's type describes nothing through the member lookup, falling back to the error-recovery type the checker gave the access.</summary>
    [Fact]
    public async Task Handle_OnAMemberThatDoesNotExist_FallsBackToItsRecoveryType()
    {
        var hover = await HoverAsync(
            """
            interface Vec {
              x: number;
            }

            fn main(v: Vec): void {
              print(v.y);
            }
            """,
            5,
            10
        );

        Assert.Contains("```loom\nnever\n```", hover);
    }

    /// <summary>The base of a multi-level chain is not itself a dotted member, so the member lookup finds no matching segment and falls through to describing the base's own symbol.</summary>
    [Fact]
    public async Task Handle_OnTheBaseOfAChainedMemberAccess_DescribesTheBaseItself()
    {
        var hover = await HoverAsync(
            """
            interface Position {
              x: number;
            }

            interface Vec {
              pos: Position;
            }

            fn main(v: Vec): void {
              print(v.pos.x);
            }
            """,
            9,
            8
        );

        Assert.Contains("v: Vec", hover);
    }

    /// <summary>A member access whose receiver is not a plain identifier - here a call result - parses to a PropertyAccess rather than a QualifiedName.</summary>
    [Fact]
    public async Task Handle_OnAMemberOfACallResult_DescribesTheMember()
    {
        var hover = await HoverAsync(
            """
            interface Packet {
              name: string;
            }

            fn make(): Packet {
              return { name: "hi" };
            }

            fn main(): void {
              print(make().name);
            }
            """,
            9,
            16
        );

        Assert.Contains("name: string", hover);
    }

    [Fact]
    public async Task Handle_OnASymbolFromAnotherModule_NamesTheModuleItCameFrom() =>
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var hover = await new HoverHandler(store).Handle(
                    new HoverParams { TextDocument = new TextDocumentIdentifier(uri), Position = new Position(1, 11) },
                    TestContext.Current.CancellationToken
                );

                var value = hover?.Contents.MarkupContent?.Value ?? "";
                Assert.Contains("fn double(n: number): number", value);
                Assert.Contains("exported by `./util/math`", value);
            },
            "import { double } from \"./util/math\";\nlet four = double(2);",
            ("util/math.loom", "export fn double(n: number): number { return n * 2; }")
        );

    [Fact]
    public async Task Handle_ForAnUnknownDocument_ReturnsNothing()
    {
        var handler = new HoverHandler(new DocumentStore());
        var uri = DocumentUri.FromFileSystemPath(Path.Combine(Path.GetTempPath(), "does-not-exist.loom"));

        Assert.Null(
            await handler.Handle(
                new HoverParams { TextDocument = new TextDocumentIdentifier(uri), Position = new Position(0, 0) },
                TestContext.Current.CancellationToken
            )
        );
    }

    private static async Task<string> HoverAsync(string source, int line, int character)
    {
        var result = "";
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var hover = await new HoverHandler(store).Handle(
                    new HoverParams { TextDocument = new TextDocumentIdentifier(uri), Position = new Position(line, character) },
                    TestContext.Current.CancellationToken
                );

                result = hover?.Contents.MarkupContent?.Value ?? "";
            },
            source
        );

        return result;
    }
}
