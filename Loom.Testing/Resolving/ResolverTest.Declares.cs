using Loom.Core.Parsing.AST;
using Loom.Core.Resolving.Symbols;

namespace Loom.Testing.Resolving;

public partial class ResolverTest
{
    [Theory]
    [InlineData("Range")]
    [InlineData("Record<string, bool>")]
    [InlineData("MutRecord<string, bool>")]
    [InlineData("Event<number, string>")]
    [InlineData("ConsumerEvent<number, string>")]
    public void Declares_IntrinsicType_Symbols(string name) => Utility.AssertNoErrors(Utility.GetSemanticModel($"mut x: {name}"));

    [Fact]
    public void Declares_EventPropertySymbol()
    {
        var model = Utility.AssertNoErrors(Utility.GetSemanticModel("interface Foo { event abc; }"));
        var interfaceDeclaration = Assert.IsType<InterfaceDeclaration>(model.Tree.Statements.Single());
        Assert.NotNull(interfaceDeclaration.Body);

        var eventDeclaration = Assert.IsType<EventDeclaration>(Assert.Single(interfaceDeclaration.Body.Members));
        var symbol = model.GetDeclarationSymbol(interfaceDeclaration, SymbolKind.Interface);
        Assert.NotNull(symbol);

        var interfaceSymbol = Assert.IsType<InterfaceSymbol>(symbol);
        Assert.Equal("Foo", interfaceSymbol.Name);
        Assert.Single(interfaceSymbol.Properties);

        var property = interfaceSymbol.GetPropertyAtPath(["abc"]);
        Assert.NotNull(property);
        Assert.Equal(SymbolKind.Event, property.Kind);
        Assert.Equal(eventDeclaration, property.Declaration);
        Assert.False(property.IsIntrinsic);
        Assert.False(property.IsMutable);
    }

    [Fact]
    public void Declares_EventSymbol()
    {
        var model = Utility.AssertNoErrors(Utility.GetSemanticModel("event abc;"));
        var eventDeclaration = Assert.IsType<EventDeclaration>(model.Tree.Statements.Single());

        var symbol = model.GetDeclarationSymbol(eventDeclaration, SymbolKind.Event);
        Assert.NotNull(symbol);
        Assert.Equal("abc", symbol.Name);
        Assert.Equal(SymbolKind.Event, symbol.Kind);
        Assert.Equal(eventDeclaration, symbol.Declaration);
        Assert.False(symbol.IsAmbient);
        Assert.False(symbol.IsIntrinsic);
        Assert.False(symbol.IsMutable);
    }

    [Fact]
    public void Declares_EventPropertySymbol_WithAttribute()
    {
        var model = Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                interface Foo {
                    [luau_name("OnConsume")]
                    event abc(x: number);
                }
                """
            )
        );

        var interfaceDeclaration = Assert.IsType<InterfaceDeclaration>(model.Tree.Statements.Last());
        Assert.NotNull(interfaceDeclaration.Body);

        var symbol = model.GetDeclarationSymbol(interfaceDeclaration, SymbolKind.Interface);
        var interfaceSymbol = Assert.IsType<InterfaceSymbol>(symbol);

        var property = Assert.Single(interfaceSymbol.Properties);
        Assert.Equal(SymbolKind.Event, property.Kind);
        var attribute = Assert.Single(property.Attributes);
        Assert.Equal("luau_name", attribute.Name);
    }

    [Fact]
    public void Declares_EventSymbol_WithAttribute_HasNoEffect()
    {
        var model = Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                [luau_name("OnConsume")]
                event abc(x: number);
                """
            )
        );

        var eventDeclaration = Assert.IsType<EventDeclaration>(model.Tree.Statements.Last());
        var symbol = model.GetDeclarationSymbol(eventDeclaration, SymbolKind.Event);
        Assert.NotNull(symbol);
        Assert.Equal(SymbolKind.Event, symbol.Kind);
        Assert.IsNotType<PropertySymbol>(symbol);
    }

    [Fact]
    public void Declares_TraitSymbol()
    {
        var model = Utility.AssertNoErrors(
            Utility.GetSemanticModel(
                """
                trait Iterator {
                    fn next(): number
                }
                """
            )
        );

        var trait = Assert.IsType<TraitDeclaration>(model.Tree.Statements.Single());

        var symbol = model.GetDeclarationSymbol(trait, SymbolKind.Trait);
        Assert.NotNull(symbol);
        Assert.Equal("Iterator", symbol.Name);
        Assert.Equal(SymbolKind.Trait, symbol.Kind);
        Assert.Equal(trait, symbol.Declaration);
        Assert.False(symbol.IsIntrinsic);
        Assert.False(symbol.IsMutable);
    }

    [Fact]
    public void Declares_VariableSymbol()
    {
        var model = Utility.GetSemanticModel("let x = 1; x;");
        Assert.Equal(2, model.Tree.Statements.Count);

        var firstStatement = model.Tree.Statements.First();
        var secondStatement = model.Tree.Statements.Last();
        var variableDeclaration = Assert.IsType<VariableDeclaration>(firstStatement);
        var expressionStatement = Assert.IsType<ExpressionStatement>(secondStatement);
        var identifier = Assert.IsType<Identifier>(expressionStatement.Expression);
        var symbol = model.GetSymbol(identifier);
        Assert.NotNull(symbol);
        Assert.Equal("x", symbol.Name);
        Assert.Equal(SymbolKind.Variable, symbol.Kind);
        Assert.Equal(variableDeclaration, symbol.Declaration);

        var declaringSymbol = model.GetDeclaringSymbol(identifier);
        var declarationSymbol = model.GetDeclarationSymbol(variableDeclaration);
        Assert.NotNull(declaringSymbol);
        Assert.NotNull(declarationSymbol);
        Assert.Equal(declaringSymbol, declarationSymbol);
        Assert.Equal("x", declarationSymbol.Name);
        Assert.Equal(SymbolKind.Variable, declarationSymbol.Kind);
        Assert.Equal(variableDeclaration, declarationSymbol.Declaration);
    }

    [Fact]
    public void Declares_Enum_VariableSymbol()
    {
        var model = Utility.GetSemanticModel("enum Colors { Red, Green, Blue }; Colors::Red");
        Utility.AssertNoErrors(model);

        var declaration = model.Tree.Statements.First();
        var expressionStatement = Assert.IsType<ExpressionStatement>(model.Tree.Statements.Last());
        var qualifiedName = Assert.IsType<QualifiedName>(expressionStatement.Expression);
        var symbol = model.GetSymbol(qualifiedName.Identifier);
        Assert.NotNull(symbol);
        Assert.Equal("Colors", symbol.Name);
        Assert.Equal(SymbolKind.Variable, symbol.Kind);
        Assert.Equal(declaration, symbol.Declaration);
    }

    [Fact]
    public void Declares_EnumTypeSymbol()
    {
        var model = Utility.GetSemanticModel("enum Colors { Red, Green, Blue };");
        var enumDeclaration = Assert.IsType<EnumDeclaration>(model.Tree.Statements.First());
        var symbol = model.GetDeclarationSymbol(enumDeclaration, SymbolKind.EnumType);
        Assert.NotNull(symbol);
        Assert.Equal("Colors", symbol.Name);
        Assert.Equal(SymbolKind.EnumType, symbol.Kind);
        Assert.Equal(enumDeclaration, symbol.Declaration);
    }

    [Fact]
    public void Declares_ParameterSymbol()
    {
        var model = Utility.GetSemanticModel("fn test(x: number) { }");
        var functionDeclaration = Assert.IsType<FunctionDeclaration>(model.Tree.Statements.First());
        Assert.NotNull(functionDeclaration.Parameters);

        var parameter = functionDeclaration.Parameters!.ParameterList.First();
        var symbol = model.GetDeclarationSymbol(parameter);
        Assert.NotNull(symbol);
        Assert.Equal("x", symbol.Name);
        Assert.Equal(SymbolKind.Parameter, symbol.Kind);
        Assert.Equal(parameter, symbol.Declaration);
    }

    [Fact]
    public void Declares_FunctionSymbol()
    {
        var model = Utility.GetSemanticModel("fn test() { }");
        var functionDeclaration = Assert.IsType<FunctionDeclaration>(model.Tree.Statements.First());
        var symbol = model.GetDeclarationSymbol(functionDeclaration);
        Assert.NotNull(symbol);
        Assert.Equal("test", symbol.Name);
        Assert.Equal(SymbolKind.Function, symbol.Kind);
        Assert.Equal(functionDeclaration, symbol.Declaration);
    }

    [Theory]
    [InlineData("sealed interface Foo { foo: number }", true)]
    [InlineData("interface Foo { foo: number }")]
    [InlineData("interface Nutz; interface Ballz; sealed interface Foo: Nutz, Ballz { foo: number }", true, false, null, 2)]
    [InlineData("declare sealed interface Foo { foo: number }", true, true, typeof(Declare))]
    [InlineData("declare interface Foo { foo: number }", false, true, typeof(Declare))]
    public void Declares_InterfaceSymbol(string source, bool isSealed = false, bool isAmbient = false, Type? declarationType = null, int constraintCount = 0)
    {
        var model = Utility.AssertNoErrors(Utility.GetSemanticModel(source));
        var statement = model.Tree.Statements.Last();
        Assert.IsType(declarationType ?? typeof(InterfaceDeclaration), statement);

        if (declarationType == typeof(Declare))
            statement = ((Declare)statement).Signature;

        var symbol = model.GetDeclarationSymbol(statement, SymbolKind.Interface);
        Assert.NotNull(symbol);

        var interfaceSymbol = Assert.IsType<InterfaceSymbol>(symbol);
        Assert.Equal("Foo", interfaceSymbol.Name);
        Assert.Equal(SymbolKind.Interface, interfaceSymbol.Kind);
        Assert.Equal(statement, interfaceSymbol.Declaration);
        Assert.Equal(isSealed, interfaceSymbol.IsSealed);
        Assert.Equal(isAmbient, interfaceSymbol.IsAmbient);
        Assert.Empty(interfaceSymbol.Implementations);
        Assert.Empty(interfaceSymbol.Implements);

        var property = Assert.Single(interfaceSymbol.Properties);
        Assert.Equal("foo", property.Name);
        Assert.False(property.HasIntrinsicAttribute("hello"));
        Assert.True(property.IsValueSymbol);
        Assert.False(property.IsTypeSymbol);
        Assert.False(property.IsMutable);
        Assert.Null(property.PointsTo);
        Assert.Empty(property.Attributes);

        if (constraintCount > 0)
        {
            Assert.NotNull(interfaceSymbol.Constraints);
            Assert.Equal(constraintCount, interfaceSymbol.Constraints.Count);
        }

        Assert.False(interfaceSymbol.IsIntrinsic);
        Assert.False(interfaceSymbol.IsMutable);
    }

    [Fact]
    public void Declares_TypeAliasSymbol()
    {
        var model = Utility.GetSemanticModel("type MyNumber = number");
        var typeAlias = Assert.IsType<TypeAlias>(model.Tree.Statements.First());
        var symbol = model.GetDeclarationSymbol(typeAlias);
        Assert.NotNull(symbol);
        Assert.Equal("MyNumber", symbol.Name);
        Assert.Equal(SymbolKind.Type, symbol.Kind);
        Assert.Equal(typeAlias, symbol.Declaration);
    }

    [Fact]
    public void Declares_TypeParameterSymbol()
    {
        var model = Utility.GetSemanticModel("type Container<T> = T");
        var typeAlias = Assert.IsType<TypeAlias>(model.Tree.Statements.First());
        Assert.NotNull(typeAlias.TypeParameters);

        var typeParameter = typeAlias.TypeParameters.ParameterList.First();
        var symbol = model.GetDeclarationSymbol(typeParameter);
        Assert.NotNull(symbol);
        Assert.Equal("T", symbol.Name);
        Assert.Equal(SymbolKind.Type, symbol.Kind);
        Assert.Equal(typeParameter, symbol.Declaration);
    }

    [Fact]
    public void Declares_DeclareVariableSymbol()
    {
        var model = Utility.GetSemanticModel("declare let x: number");
        Utility.AssertNoErrors(model);

        var declare = Assert.IsType<Declare>(model.Tree.Statements.Single());
        var sig = Assert.IsType<DeclareVariableSignature>(declare.Signature);
        var symbol = model.GetDeclarationSymbol(sig);
        Assert.NotNull(symbol);
        Assert.Equal("x", symbol.Name);
        Assert.Equal(SymbolKind.Variable, symbol.Kind);
        Assert.False(symbol.IsMutable);
        Assert.Equal(sig, symbol.Declaration);
    }

    [Fact]
    public void Declares_DeclareVariableSymbol_Mutable()
    {
        var model = Utility.GetSemanticModel("declare mut y: string");
        var declare = Assert.IsType<Declare>(model.Tree.Statements.Single());
        var sig = Assert.IsType<DeclareVariableSignature>(declare.Signature);
        var symbol = model.GetDeclarationSymbol(sig);
        Assert.NotNull(symbol);
        Assert.Equal("y", symbol.Name);
        Assert.True(symbol.IsMutable);
    }

    [Fact]
    public void Declares_DeclareFunctionSymbol()
    {
        var model = Utility.GetSemanticModel("declare fn add(a: number, b: number): number");
        Utility.AssertNoErrors(model);

        var declare = Assert.IsType<Declare>(model.Tree.Statements.Single());
        var sig = Assert.IsType<DeclareFunctionSignature>(declare.Signature);
        var symbol = model.GetDeclarationSymbol(sig);
        Assert.NotNull(symbol);
        Assert.Equal("add", symbol.Name);
        Assert.Equal(SymbolKind.Function, symbol.Kind);
        Assert.Equal(sig, symbol.Declaration);
    }

    [Fact]
    public void Declares_DeclareFunction_InsideBlock()
    {
        var model = Utility.GetSemanticModel("{ declare fn helper(): string; helper; }");
        Utility.AssertNoErrors(model);
    }

    [Fact]
    public void Declares_DeclareEventSymbol()
    {
        var model = Utility.AssertNoErrors(Utility.GetSemanticModel("declare event consumer(param: string);"));

        var declare = Assert.IsType<Declare>(model.Tree.Statements.Single());
        var sig = Assert.IsType<EventDeclaration>(declare.Signature);
        var symbol = model.GetDeclarationSymbol(sig, SymbolKind.Event);
        Assert.NotNull(symbol);
        Assert.Equal("consumer", symbol.Name);
        Assert.Equal(SymbolKind.Event, symbol.Kind);
        Assert.Equal(sig, symbol.Declaration);
        Assert.True(symbol.IsAmbient);
        Assert.False(symbol.IsMutable);
    }
}
