using Loom.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Loom.Testing;

namespace Loom.Testing.LanguageServer;

[Collection("Assembly")]
public class NavigationHandlerTest
{
    private const string Traits = """
                                  trait ToString {
                                      fn to_string: string;
                                  }

                                  interface User {
                                      name: string;
                                  }

                                  implement ToString for User {
                                      fn to_string -> name
                                  }
                                  """;

    [Fact]
    public async Task TypeDefinition_FromAnInferredBinding_GoesToTheInterface()
    {
        var locations = await TypeDefinitionAsync(
            """
            interface Vec {
              x: number;
            }

            fn make(): Vec { return { x: 1 }; }

            fn main(): void {
              let position = make();
              print(position);
            }
            """,
            8,
            9
        );

        Assert.Equal(0, Assert.Single(locations).Range.Start.Line);
    }

    [Fact]
    public async Task TypeDefinition_LooksThroughAnArrayToItsElementType()
    {
        var locations = await TypeDefinitionAsync(
            """
            interface Vec {
              x: number;
            }

            fn main(points: Vec[]): void {
              print(points);
            }
            """,
            5,
            9
        );

        Assert.Equal(0, Assert.Single(locations).Range.Start.Line);
    }

    [Fact]
    public async Task TypeDefinition_ForAUnion_OffersEveryArm()
    {
        var locations = await TypeDefinitionAsync(
            """
            interface Vec { x: number; }
            interface Dot { y: number; }

            fn main(shape: Vec | Dot): void {
              print(shape);
            }
            """,
            4,
            9
        );

        Assert.Equal(2, locations.Length);
    }

    [Fact]
    public async Task TypeDefinition_ForAPrimitive_GoesNowhere() => Assert.Empty(await TypeDefinitionAsync("let count = 1;\nprint(count);", 1, 6));

    [Fact]
    public async Task Implementation_FromATraitMethod_GoesToEachBody()
    {
        var locations = await ImplementationAsync(Traits, 1, 9);
        Assert.Equal(9, Assert.Single(locations).Range.Start.Line);
    }

    [Fact]
    public async Task Implementation_FromATraitName_GoesToEachImplementBlock()
    {
        var locations = await ImplementationAsync(Traits, 0, 7);
        Assert.Equal(8, Assert.Single(locations).Range.Start.Line);
    }

    [Fact]
    public async Task Implementation_FromAnInterfaceName_GoesToTheTraitsItImplements()
    {
        var locations = await ImplementationAsync(Traits, 4, 11);
        Assert.Equal(8, Assert.Single(locations).Range.Start.Line);
    }

    [Fact]
    public async Task Implementation_ForSomethingWithNoImplementations_GoesNowhere() =>
        Assert.Empty(await ImplementationAsync("fn main(): void { }", 0, 4));

    /// <summary>A member name is a token of its access expression rather than a node, so looking the node up alone finds nothing for it.</summary>
    [Fact]
    public async Task Definition_OnAMember_GoesToThePropertyDeclaration()
    {
        var locations = await DefinitionAsync(
            """
            interface Vec {
              x: number;
            }

            fn main(v: Vec): void {
              print(v.x);
            }
            """,
            5,
            10
        );

        Assert.Equal(1, Assert.Single(locations).Range.Start.Line);
    }

    [Fact]
    public async Task Definition_OnAName_StillGoesToItsDeclaration()
    {
        var locations = await DefinitionAsync("fn double(n: number): number { return n * 2; }\nlet four = double(2);", 1, 11);
        Assert.Equal(0, Assert.Single(locations).Range.Start.Line);
    }

    /// <summary>
    ///     A trait method is a FunctionSymbol declared on a separate `implement` block rather than a
    ///     PropertySymbol of the interface itself, which the member-access lookup has to consider alongside
    ///     an interface's own properties or a call site never resolves to anything.
    /// </summary>
    [Fact]
    public async Task Definition_OnATraitMethodCall_GoesToTheImplementation()
    {
        var locations = await DefinitionAsync(
            $$"""
            {{Traits}}

            fn main(): void {
              let user = new User { name: "Poppy" };
              print(user.to_string());
            }
            """,
            14,
            17
        );

        Assert.Equal(9, Assert.Single(locations).Range.Start.Line);
    }

    [Fact]
    public async Task Definition_OnAnImportSpecifier_GoesToTheExportedDeclaration() =>
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var result = await new DefinitionHandler(store).Handle(
                    new DefinitionParams { TextDocument = new TextDocumentIdentifier(uri), Position = new Position(0, 11) },
                    TestContext.Current.CancellationToken
                );

                Assert.EndsWith("math.loom", Assert.Single(ToLocations(result)).Uri.Path);
            },
            "import { double } from \"./util/math\";\nlet four = double(2);",
            ("util/math.loom", "export fn double(n: number): number { return n * 2; }")
        );

    [Fact]
    public async Task TypeDefinition_ForAnUnknownDocument_ReturnsNothing()
    {
        var handler = new TypeDefinitionHandler(new DocumentStore());
        var uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.FromFileSystemPath(Path.Combine(Path.GetTempPath(), "does-not-exist.loom"));

        Assert.Null(
            await handler.Handle(
                new TypeDefinitionParams { TextDocument = new TextDocumentIdentifier(uri), Position = new Position(0, 0) },
                TestContext.Current.CancellationToken
            )
        );
    }

    private static async Task<Location[]> TypeDefinitionAsync(string source, int line, int character)
    {
        var locations = Array.Empty<Location>();
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var result = await new TypeDefinitionHandler(store).Handle(
                    new TypeDefinitionParams { TextDocument = new TextDocumentIdentifier(uri), Position = new Position(line, character) },
                    TestContext.Current.CancellationToken
                );

                locations = ToLocations(result);
            },
            source
        );

        return locations;
    }

    private static async Task<Location[]> ImplementationAsync(string source, int line, int character)
    {
        var locations = Array.Empty<Location>();
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var result = await new ImplementationHandler(store).Handle(
                    new ImplementationParams { TextDocument = new TextDocumentIdentifier(uri), Position = new Position(line, character) },
                    TestContext.Current.CancellationToken
                );

                locations = ToLocations(result);
            },
            source
        );

        return locations;
    }

    private static async Task<Location[]> DefinitionAsync(string source, int line, int character)
    {
        var locations = Array.Empty<Location>();
        await Utility.WithLspProjectAsync(
            async (store, uri) =>
            {
                var result = await new DefinitionHandler(store).Handle(
                    new DefinitionParams { TextDocument = new TextDocumentIdentifier(uri), Position = new Position(line, character) },
                    TestContext.Current.CancellationToken
                );

                locations = ToLocations(result);
            },
            source
        );

        return locations;
    }

    private static Location[] ToLocations(LocationOrLocationLinks? result) =>
        result?.Where(entry => entry.Location != null).Select(entry => entry.Location!).ToArray() ?? [];
}
