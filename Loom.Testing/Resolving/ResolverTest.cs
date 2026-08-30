using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving.Symbols;

namespace Loom.Testing.Resolving;

[Collection("Assembly")]
public partial class ResolverTest
{
    [Fact]
    public void WarnsFor_UseExpressionBody()
    {
        var diagnostics = Utility.GetSemanticModel("fn abc() { return 1; }").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.RedundantCode, "Use expression body.");
    }

    [Fact]
    public void TracksInitialization_ThroughNestedBlocks() => Utility.AssertNoErrors(Utility.GetSemanticModel("mut x: number; { x = 42; } x;"));

    /// <summary>
    ///     A hoisted declaration is visited after the hoisting pass already declared it, and declaring it
    ///     again would leave two symbols standing for one node - only the first reachable through a lookup,
    ///     so anything written to the other is lost, and symbol identity no longer answers "same declaration".
    /// </summary>
    [Theory]
    [InlineData("enum Color { Red, Green }", SymbolKind.Variable, SymbolKind.EnumType)]
    [InlineData("interface Foo { bar: number }", SymbolKind.Variable, SymbolKind.Interface)]
    [InlineData("type Foo = number;", SymbolKind.Type)]
    [InlineData("event ping(n: number);", SymbolKind.Event)]
    [InlineData("fn f(): number -> 1;", SymbolKind.Function)]
    [InlineData("let x = 1;", SymbolKind.Variable)]
    public void Declares_OneSymbolPerDeclaration(string source, params SymbolKind[] expectedKinds)
    {
        var model = Utility.GetSemanticModel(source);
        var symbols = model.GetDeclarationSymbols(model.Tree.Statements[0]);

        Assert.Equal(expectedKinds, symbols.Select(symbol => symbol.Kind));
        Assert.Equal(symbols.Count, symbols.Distinct().Count());
    }








    [Fact]
    public void DeclareFunctionSignature_TryGetIntrinsicAttribute_FindsAttributeByName()
    {
        var semanticModel = Utility.GetSemanticModel("[luau_name(\"Bar\")]\ndeclare fn foo(): void;");
        Utility.AssertNoErrors(semanticModel.Diagnostics);

        var signature = semanticModel.Tree.GetDescendants<DeclareFunctionSignature>().Single();
        Assert.True(signature.TryGetIntrinsicAttribute(semanticModel, "luau_name", out var attribute));
        Assert.Equal("luau_name", attribute.Name);
        Assert.False(signature.TryGetIntrinsicAttribute(semanticModel, "luau_method", out _));
    }

}
