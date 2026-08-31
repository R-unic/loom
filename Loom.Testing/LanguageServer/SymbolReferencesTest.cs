using Loom.LanguageServer;

namespace Loom.Testing.LanguageServer;

/// <summary>
///     Every place a symbol is written, inverted from what the resolver already answers. Exercised directly
///     against the static API rather than through a handler, since finding references is the shared piece
///     every navigation and rename request sits on top of.
/// </summary>
[Collection("Assembly")]
public class SymbolReferencesTest
{
    [Fact]
    public async Task At_PastTheEndOfTheFile_FindsNothing() =>
        await Utility.WithLspProjectAsync(
            (store, uri) =>
            {
                Assert.True(store.TryGetState(uri, out var state));
                Assert.Null(SymbolReferences.At(state.File, state.File.SourceFile.SourceText.Length + 1000));
                return Task.CompletedTask;
            },
            "let x = 1;"
        );

    /// <remarks>
    ///     A re-export names a symbol this file never declares itself, so it has no reference or declaration of
    ///     its own to resolve through - the specifier's own binding is the only place the answer comes from.
    /// </remarks>
    [Fact]
    public async Task At_OnAReExportSpecifier_FindsTheExportedSymbol() =>
        await Utility.WithLspProjectAsync(
            (store, uri) =>
            {
                Assert.True(store.TryGetState(uri, out var state));
                var offset = state.File.SourceFile.SourceText.IndexOf("pi", StringComparison.Ordinal);

                var symbol = SymbolReferences.At(state.File, offset);
                Assert.NotNull(symbol);
                Assert.Equal("pi", symbol.Name);
                return Task.CompletedTask;
            },
            "export { pi } from \"./util\";",
            ("util.loom", "export let pi = 3.14;")
        );

    /// <remarks>A member reached through a call result is a PropertyAccess node, which finding references and finding the symbol under the cursor both have to walk the same way a QualifiedName is walked.</remarks>
    [Fact]
    public async Task At_OnAMemberReachedThroughAnElementAccessResult_FindsTheMember() =>
        await Utility.WithLspProjectAsync(
            (store, uri) =>
            {
                Assert.True(store.TryGetState(uri, out var state));
                var offset = state.File.SourceFile.SourceText.IndexOf("get", state.File.SourceFile.SourceText.IndexOf("boxes[0]", StringComparison.Ordinal), StringComparison.Ordinal);

                var symbol = SymbolReferences.At(state.File, offset);
                Assert.NotNull(symbol);
                Assert.Equal("get", symbol.Name);
                return Task.CompletedTask;
            },
            """
            interface Box {
              get: fn(): number;
            }

            fn use(boxes: Box[]): number {
              return boxes[0].get();
            }
            """
        );

    /// <remarks>A dotted chain of more than one link has to keep walking the receiver's type forward past the first link that is not the one under the cursor.</remarks>
    [Fact]
    public async Task At_OnTheLastLinkOfAMultiLevelChain_FindsIt() =>
        await Utility.WithLspProjectAsync(
            (store, uri) =>
            {
                Assert.True(store.TryGetState(uri, out var state));
                var text = state.File.SourceFile.SourceText;
                var offset = text.LastIndexOf("value", StringComparison.Ordinal);

                var symbol = SymbolReferences.At(state.File, offset);
                Assert.NotNull(symbol);
                Assert.Equal("value", symbol.Name);
                return Task.CompletedTask;
            },
            """
            interface Inner { value: number; }
            interface Outer { inner: Inner; }
            fn use(outer: Outer): number { return outer.inner.value; }
            """
        );

    /// <remarks>A member reached through a call result is walked the same way when collecting references across the whole file, not just when finding the symbol under the cursor.</remarks>
    [Fact]
    public async Task In_FindsAReferenceToAMemberReachedThroughAnElementAccessResult() =>
        await Utility.WithLspProjectAsync(
            (store, uri) =>
            {
                Assert.True(store.TryGetState(uri, out var state));
                var declarationOffset = state.File.SourceFile.SourceText.IndexOf("get:", StringComparison.Ordinal);
                var getSymbol = SymbolReferences.At(state.File, declarationOffset);
                Assert.NotNull(getSymbol);

                var references = SymbolReferences.In(getSymbol, state.File);
                Assert.Contains(references, reference => !reference.IsDeclaration);
                return Task.CompletedTask;
            },
            """
            interface Box {
              get: fn(): number;
            }

            fn use(boxes: Box[]): number {
              return boxes[0].get();
            }
            """
        );

    /// <remarks>An export list names the symbol at its source, which a rename has to follow the same way it follows every read.</remarks>
    [Fact]
    public async Task In_FindsTheReExportListNamingTheSymbol() =>
        await Utility.WithLspProjectAsync(
            (store, uri) =>
            {
                Assert.True(store.TryGetState(uri, out var state));
                var declarationOffset = state.File.SourceFile.SourceText.IndexOf("target", StringComparison.Ordinal);
                var symbol = SymbolReferences.At(state.File, declarationOffset);
                Assert.NotNull(symbol);

                var references = SymbolReferences.In(symbol, state.File);
                Assert.Equal(3, references.Count);
                return Task.CompletedTask;
            },
            "let target = 1;\nexport { target };\nfn use(): number { return target; }"
        );
}
