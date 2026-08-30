using Loom.Core.Parsing.AST;
using Loom.Core.Resolving.Symbols;
using Loom.Testing;

namespace Loom.Testing.Resolving;

/// <summary>Each symbol class's debug representation, which nothing else asserts on since it exists for a debugger to read rather than for the compiler to act on.</summary>
public class SymbolToStringTest
{
    [Fact]
    public void Symbol_ToString_NamesKindAndName()
    {
        var model = Utility.GetSemanticModel("let x = 1;");
        var symbol = Assert.IsType<VariableSymbol>(model.GetDeclarationSymbol(model.Tree.Statements[0]));

        Assert.Equal("Symbol(Variable, x)", symbol.ToString());
    }

    [Fact]
    public void InterfaceSymbol_ToString_ListsPropertiesImplementsAndConstraints()
    {
        var model = Utility.GetSemanticModel(
            """
            trait Getter { fn get(): number }
            interface Base { base_value: number }
            interface Foo: Base { value: number }

            implement Getter for Foo {
                fn get() -> value;
            }
            """
        );

        var foo = Assert.IsType<InterfaceDeclaration>(model.Tree.Statements[2]);
        var symbol = Assert.IsType<InterfaceSymbol>(model.GetDeclarationSymbol(foo, SymbolKind.Interface));

        Assert.Equal("InterfaceSymbol(Foo, IsSealed: False, Properties: [value] Implements: [Getter], Constraints: [Base])", symbol.ToString());
    }

    [Fact]
    public void TraitSymbol_ToString_ListsImplementers()
    {
        var model = Utility.GetSemanticModel(
            """
            trait Getter { fn get(): number }
            interface Foo { value: number }

            implement Getter for Foo {
                fn get() -> value;
            }
            """
        );

        var trait = Assert.IsType<TraitDeclaration>(model.Tree.Statements[0]);
        var symbol = Assert.IsType<TraitSymbol>(model.GetDeclarationSymbol(trait, SymbolKind.Trait));

        Assert.Equal("TraitSymbol(Getter, ImplementedBy: [Foo])", symbol.ToString());
    }

    [Fact]
    public void InjectedPropertyVariableSymbol_ToString_NamesTheInterfaceItCameFrom()
    {
        var model = Utility.GetSemanticModel(
            """
            trait Getter { fn get(): number }
            interface Foo { value: number }

            implement Getter for Foo {
                fn get() -> value;
            }
            """
        );

        var symbol = Assert.IsType<InjectedPropertyVariableSymbol>(model.DeclaredSymbols.OfType<InjectedPropertyVariableSymbol>().Single());

        Assert.Equal("InjectedPropertyVariableSymbol(value, From: InterfaceSymbol(Foo, IsSealed: False, Properties: [value] Implements: [Getter], Constraints: []))", symbol.ToString());
    }

    /// <summary>An empty path has nothing to look up, so it answers with no properties rather than every one - the same emptiness a caller handed it.</summary>
    [Fact]
    public void InterfaceSymbol_GetPropertiesAtPath_EmptyPath_ReturnsEmpty()
    {
        var model = Utility.GetSemanticModel("interface Foo { value: number }");
        var foo = Assert.IsType<InterfaceDeclaration>(model.Tree.Statements[0]);
        var symbol = Assert.IsType<InterfaceSymbol>(model.GetDeclarationSymbol(foo, SymbolKind.Interface));

        Assert.Empty(symbol.GetPropertiesAtPath([]));
    }
}
