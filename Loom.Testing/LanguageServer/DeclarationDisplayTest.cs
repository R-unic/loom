using Loom.Core.Resolving.Symbols;
using Loom.LanguageServer;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.Testing.LanguageServer;

/// <summary>
///     Renders a symbol the way its declaration reads. Exercised directly against the static API - the shared
///     piece hover, completion and the outline all render through - rather than through any one handler.
/// </summary>
[Collection("Assembly")]
public class DeclarationDisplayTest
{
    [Fact]
    public async Task Of_OnAPatternsInferBinder_ReadsAsALetDeclaration() =>
        await Utility.WithLspProjectAsync(
            (store, uri) =>
            {
                Assert.True(store.TryGetState(uri, out var state));
                var (symbol, type) = SymbolAt(state, "let R");

                Assert.StartsWith("let R", DeclarationDisplay.Of(symbol, type));
                return Task.CompletedTask;
            },
            "type ReturnType<T> = T is fn(..unknown[]): let R ? R : never;"
        );

    [Fact]
    public async Task Of_OnAMappedTypesKeyBinder_ReadsAsFromKeyof() =>
        await Utility.WithLspProjectAsync(
            (store, uri) =>
            {
                Assert.True(store.TryGetState(uri, out var state));
                var (symbol, type) = SymbolAt(state, "[K", offsetWithin: 1);

                Assert.Equal("K from keyof(T)", DeclarationDisplay.Of(symbol, type));
                return Task.CompletedTask;
            },
            "interface AsMut<T> { mut [K from keyof(T)]: T[K]; }"
        );

    [Fact]
    public async Task CallSignatures_OnADeclaredVariableTypedAsAFunction_RendersItsParameters() =>
        await Utility.WithLspProjectAsync(
            (store, uri) =>
            {
                Assert.True(store.TryGetState(uri, out var state));
                var (symbol, type) = SymbolAt(state, "apply(9)");

                var signature = Assert.Single(DeclarationDisplay.CallSignatures([symbol], type, "apply"));
                Assert.Contains("x: number", signature.Label);
                return Task.CompletedTask;
            },
            "declare let apply: fn(x: number): number;\nlet nine = apply(9);"
        );

    [Fact]
    public async Task CallSignatures_OnAParameterTypedAsAFunction_RendersItsParameters() =>
        await Utility.WithLspProjectAsync(
            (store, uri) =>
            {
                Assert.True(store.TryGetState(uri, out var state));
                var (symbol, type) = SymbolAt(state, "callback(1)");

                var signature = Assert.Single(DeclarationDisplay.CallSignatures([symbol], type, "callback"));
                Assert.Contains("x: number", signature.Label);
                return Task.CompletedTask;
            },
            "fn run(callback: fn(x: number): number): void { callback(1); }"
        );

    /// <remarks>A plain local holding a lambda has no declared parameter list of its own to read - the rendered signature falls back to naming each one positionally.</remarks>
    [Fact]
    public async Task CallSignatures_OnALocalHoldingALambda_FallsBackToPositionalParameterNames() =>
        await Utility.WithLspProjectAsync(
            (store, uri) =>
            {
                Assert.True(store.TryGetState(uri, out var state));
                var (symbol, type) = SymbolAt(state, "f(3)");

                var signature = Assert.Single(DeclarationDisplay.CallSignatures([symbol], type, "f"));
                Assert.Contains("arg1", signature.Label);
                return Task.CompletedTask;
            },
            "let f = fn(x: number): number -> x * 2;\nlet six = f(3);"
        );

    /// <remarks>The declared parameter names come from the AST, which does not itself know the type is optional - only the resolved shape does, so an optional function type still yields exactly one signature.</remarks>
    [Fact]
    public async Task CallSignatures_OnAnOptionalFunctionType_LooksThroughTheOptionalToItsShape() =>
        await Utility.WithLspProjectAsync(
            (store, uri) =>
            {
                Assert.True(store.TryGetState(uri, out var state));
                var (symbol, type) = SymbolAt(state, "= cb", offsetWithin: 2);

                var signature = Assert.Single(DeclarationDisplay.CallSignatures([symbol], type, "cb"));
                Assert.Contains("arg1: number", signature.Label);
                return Task.CompletedTask;
            },
            "fn call(cb: (fn(x: number): number)?): void { let held = cb; }"
        );

    /// <remarks>Only a declared (ambient) function signature carries its attributes on the symbol itself - a regular declaration's are read off the AST instead - so an attribute symbol has to come from one of those.</remarks>
    [Fact]
    public async Task Of_OnAnAttributeSymbol_RendersItInBrackets() =>
        await Utility.WithLspProjectAsync(
            (store, uri) =>
            {
                Assert.True(store.TryGetState(uri, out var state));
                var functionSymbol = state.File.SemanticModel.DeclaredSymbols.OfType<FunctionSymbol>().Single(candidate => candidate.Name == "old");
                var attribute = Assert.Single(functionSymbol.Attributes);

                Assert.Equal("[deprecated]", DeclarationDisplay.Of(attribute, null));
                return Task.CompletedTask;
            },
            "[deprecated(\"use 'add' instead\")]\ndeclare fn old(): void;"
        );

    [Fact]
    public async Task Of_OnAGenericTypeParameter_FallsBackToATypeAlias() =>
        await Utility.WithLspProjectAsync(
            (store, uri) =>
            {
                Assert.True(store.TryGetState(uri, out var state));
                var (symbol, type) = SymbolAt(state, "<T>", offsetWithin: 1);

                Assert.StartsWith("type T", DeclarationDisplay.Of(symbol, type));
                return Task.CompletedTask;
            },
            "fn identity<T>(x: T): T -> x;"
        );

    private static (Symbol Symbol, Type? Type) SymbolAt(DocumentState state, string needle, int offsetWithin = 0)
    {
        var text = state.File.SourceFile.SourceText;
        var offset = text.IndexOf(needle, StringComparison.Ordinal) + offsetWithin;
        Assert.True(offset >= 0, $"'{needle}' does not appear in the fixture");

        var symbol = SymbolReferences.At(state.File, offset);
        Assert.NotNull(symbol);

        var node = NodeFinder.FindAt(state.File.Tree, offset);
        var type = node == null ? null : state.File.SemanticModel.GetType(node);
        return (symbol, type);
    }
}
