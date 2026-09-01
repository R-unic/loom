using Loom.Config;
using Loom.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Version = Loom.Config.Version;

namespace Loom.Testing.LanguageServer;

/// <summary>
///     Built once per compile and read by every completion request; exercised directly against the static
///     builder and the snapshot's own API rather than through the completion handler.
/// </summary>
[Collection("Assembly")]
public class CompletionSnapshotTest
{
    [Fact]
    public void Empty_HasNoIdentifiersOrMemberScopes()
    {
        Assert.Empty(CompletionSnapshot.Empty.Identifiers);
        Assert.Empty(CompletionSnapshot.Empty.MemberScopes);
    }

    /// <remarks>A dependency's own module specifier is offerable by its package name, alongside the entry project's sibling files.</remarks>
    [Fact]
    public void ModuleSpecifiers_OffersADependencysPackageName()
    {
        var directory = Path.Combine(Path.GetTempPath(), "loom-snapshot-test-" + Guid.NewGuid());
        var packageDirectory = Path.Combine(directory, "packages", "math");
        Directory.CreateDirectory(Path.Combine(directory, "src"));
        Directory.CreateDirectory(Path.Combine(packageDirectory, "src"));
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "loom-config.toml"),
                "[dependencies]\nmath = \"^1.0\"\n[files]\nsource_directory = \"src\"\noutput_directory = \"dist\"\n"
            );
            File.WriteAllText(
                Path.Combine(packageDirectory, "loom-config.toml"),
                "project_type = \"library\"\n[package]\nname = \"math\"\nversion = \"1.0.0\"\n[files]\nsource_directory = \"src\"\noutput_directory = \"dist\"\n"
            );
            File.WriteAllText(Path.Combine(packageDirectory, "src", "init.loom"), "export let pi = 3;");
            File.WriteAllText(Path.Combine(directory, "src", "main.loom"), "import { pi } from \"math\";\nlet x: number = pi;");
            new LockFile([new LockedPackage(PackageName.Parse("math"), Version.Parse("1.0.0"))]).WriteTo(directory);

            var mainPath = Path.Combine(directory, "src", "main.loom");
            var store = new DocumentStore();
            var uri = OmniSharp.Extensions.LanguageServer.Protocol.DocumentUri.FromFileSystemPath(mainPath);
            Assert.NotNull(store.Open(uri, File.ReadAllText(mainPath)));

            Assert.True(store.TryGetState(uri, out var state));
            Assert.Contains(state.Completions.ModuleSpecifiers, symbol => symbol.Name == "math");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <remarks>A completion item's detail and documentation are resolved lazily, on the resolve request rather than while the list is built - here forced by calling them directly.</remarks>
    [Fact]
    public async Task ImportScope_ResolvesTheDetailAndDocumentationOfAnImportedName() =>
        await Utility.WithLspProjectAsync(
            (store, uri) =>
            {
                Assert.True(store.TryGetState(uri, out var state));
                var insideBraces = state.File.SourceFile.SourceText.IndexOf('{') + 1;

                var candidates = state.Completions.At(insideBraces);
                var @double = Assert.Single(candidates, symbol => symbol.Name == "double");

                Assert.Equal("(n: number): number", @double.Detail());
                Assert.Contains("fn double(n: number): number", @double.Documentation());
                return Task.CompletedTask;
            },
            "import {  } from \"./util/math\";",
            ("util/math.loom", "export fn double(n: number): number { return n * 2; }")
        );

    /// <summary>An exported event is a candidate inside an import list the same as a function or a type is.</summary>
    [Fact]
    public async Task ImportScope_OffersAnExportedEventAsAnEventKind() =>
        await Utility.WithLspProjectAsync(
            (store, uri) =>
            {
                Assert.True(store.TryGetState(uri, out var state));
                var insideBraces = state.File.SourceFile.SourceText.IndexOf('{') + 1;

                var fired = Assert.Single(state.Completions.At(insideBraces), symbol => symbol.Name == "fired");
                Assert.Equal(CompletionItemKind.Event, fired.Kind);
                return Task.CompletedTask;
            },
            "import {  } from \"./events\";",
            ("events.loom", "export event fired(value: number);")
        );

    /// <remarks>A local declared inside a for-loop's body is scoped to the loop, not to the whole file.</remarks>
    [Fact]
    public async Task Identifiers_ALocalDeclaredInsideAForLoop_IsScopedToTheLoop() =>
        await Utility.WithLspProjectAsync(
            (store, uri) =>
            {
                Assert.True(store.TryGetState(uri, out var state));
                var text = state.File.SourceFile.SourceText;

                var insideLoop = text.IndexOf("print(item)", StringComparison.Ordinal) + 6;
                var afterLoop = text.Length;

                Assert.Contains(state.Completions.At(insideLoop), symbol => symbol.Name == "item");
                Assert.DoesNotContain(state.Completions.At(afterLoop), symbol => symbol.Name == "item");
                return Task.CompletedTask;
            },
            "fn main(): void {\n  for item: 0..3 {\n    print(item);\n  }\n}"
        );

    /// <remarks>A member reached through a call result offers its members the same way a plain identifier receiver does.</remarks>
    [Fact]
    public async Task MemberScopes_AfterADotOnACallResult_OffersItsMembers() =>
        await Utility.WithLspProjectAsync(
            (store, uri) =>
            {
                Assert.True(store.TryGetState(uri, out var state));
                var text = state.File.SourceFile.SourceText;
                var afterDot = text.LastIndexOf('.') + 1;

                var members = state.Completions.At(afterDot);
                Assert.Contains(members, symbol => symbol.Name == "name");

                var name = Assert.Single(members, symbol => symbol.Name == "name");
                Assert.Equal("string", name.Detail());
                return Task.CompletedTask;
            },
            """
            interface Packet { name: string; }
            fn make(): Packet { return new Packet { name: "x" }; }
            fn main(): void { print(make().name); }
            """
        );
}
