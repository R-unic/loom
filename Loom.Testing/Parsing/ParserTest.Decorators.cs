using Loom.Core.Parsing.AST;

namespace Loom.Testing.Parsing;

public partial class ParserTest
{
    [Fact]
    public void Parses_FunctionDeclaration_WithBareDecoratorAttribute()
    {
        var tree = Utility.GetAST("[log_everything] fn do_something() { }");
        var declaration = Assert.IsType<FunctionDeclaration>(Assert.Single(tree.Statements));
        var attribute = Assert.Single(declaration.Attributes!.AttributeList);

        Assert.False(attribute.IsInvoked);
        Assert.Equal("log_everything", Assert.IsType<Identifier>(attribute.Expression).Name.Text);
    }

    [Fact]
    public void Parses_FunctionDeclaration_WithInvokedDecoratorAttribute()
    {
        var tree = Utility.GetAST("[log(\"info\")] fn do_something() { }");
        var declaration = Assert.IsType<FunctionDeclaration>(Assert.Single(tree.Statements));
        var attribute = Assert.Single(declaration.Attributes!.AttributeList);

        Assert.True(attribute.IsInvoked);
        Assert.Equal("log", Assert.IsType<Identifier>(attribute.Expression).Name.Text);
        var argument = Assert.Single(attribute.Arguments.ArgumentList);
        Assert.Equal("info", Assert.IsType<Literal>(argument).Value);
    }

    [Fact]
    public void Parses_FunctionDeclaration_WithMultipleDecoratorAttributes()
    {
        var tree = Utility.GetAST("[log(\"info\"), twice] fn do_something() { }");
        var declaration = Assert.IsType<FunctionDeclaration>(Assert.Single(tree.Statements));
        Assert.Equal(2, declaration.Attributes!.AttributeList.Count);
    }

    [Fact]
    public void Parses_FunctionDeclaration_WithDecoratorAttribute_NoParenBody()
    {
        var tree = Utility.GetAST("[log(\"info\")]\nfn do_something -> print(\"hi\");");
        var declaration = Assert.IsType<FunctionDeclaration>(Assert.Single(tree.Statements));
        Assert.NotNull(declaration.Attributes);
        Assert.Null(declaration.Parameters);
    }

    [Fact]
    public void Parses_InterfaceDeclaration_WithBareDecoratorAttribute()
    {
        var tree = Utility.GetAST("[validate] interface Foo { x: number }");
        var declaration = Assert.IsType<InterfaceDeclaration>(Assert.Single(tree.Statements));
        var attribute = Assert.Single(declaration.Attributes!.AttributeList);
        Assert.False(attribute.IsInvoked);
    }

    [Fact]
    public void Parses_SealedInterfaceDeclaration_WithDecoratorAttribute()
    {
        var tree = Utility.GetAST("[validate] sealed interface Foo { x: number }");
        var declaration = Assert.IsType<InterfaceDeclaration>(Assert.Single(tree.Statements));
        Assert.NotNull(declaration.SealedKeyword);
        Assert.NotNull(declaration.Attributes);
    }

    [Fact]
    public void Parses_InterfaceDeclaration_WithInvokedDecoratorAttribute()
    {
        var tree = Utility.GetAST("[validate(\"strict\")] interface Foo { x: number }");
        var declaration = Assert.IsType<InterfaceDeclaration>(Assert.Single(tree.Statements));
        var attribute = Assert.Single(declaration.Attributes!.AttributeList);
        Assert.True(attribute.IsInvoked);
    }
    [Fact]
    public void Parses_TraitMember_WithInvokedDecoratorAttribute()
    {
        var tree = Utility.GetAST("trait Add<T> { [luau_metamethod(\"__add\")] fn add(other: T): T; }");
        var trait = Assert.IsType<TraitDeclaration>(Assert.Single(tree.Statements));
        var member = Assert.Single(trait.Body.Members);
        var attribute = Assert.Single(member.Attributes!.AttributeList);

        Assert.True(attribute.IsInvoked);
        Assert.Equal("luau_metamethod", Assert.IsType<Identifier>(attribute.Expression).Name.Text);
        var argument = Assert.Single(attribute.Arguments.ArgumentList);
        Assert.Equal("__add", Assert.IsType<Literal>(argument).Value);
    }

    [Fact]
    public void Parses_TraitMember_WithoutAttribute()
    {
        var tree = Utility.GetAST("trait Iterator { fn next(): number; }");
        var trait = Assert.IsType<TraitDeclaration>(Assert.Single(tree.Statements));
        var member = Assert.Single(trait.Body.Members);
        Assert.Null(member.Attributes);
    }

    [Fact]
    public void Parses_TraitMember_WithArrowBody_AsFunctionDeclaration()
    {
        var tree = Utility.GetAST("trait Greeting { fn greet(): string -> \"hi\"; }");
        var trait = Assert.IsType<TraitDeclaration>(Assert.Single(tree.Statements));
        var member = Assert.IsType<FunctionDeclaration>(Assert.Single(trait.Body.Members));
        var body = Assert.IsType<ExpressionBody>(member.Body);
        Assert.Equal("hi", Assert.IsType<Literal>(body.Expression).Value);
    }

    [Fact]
    public void Parses_TraitMember_WithBlockBody_AsFunctionDeclaration()
    {
        var tree = Utility.GetAST("trait Greeting { fn greet(): string { return \"hi\"; } }");
        var trait = Assert.IsType<TraitDeclaration>(Assert.Single(tree.Statements));
        var member = Assert.IsType<FunctionDeclaration>(Assert.Single(trait.Body.Members));
        Assert.IsType<Block>(member.Body);
    }

    [Fact]
    public void Parses_TraitMember_WithoutBody_AsDeclareFunctionSignature()
    {
        var tree = Utility.GetAST("trait Greeting { fn greet(): string; }");
        var trait = Assert.IsType<TraitDeclaration>(Assert.Single(tree.Statements));
        var member = Assert.Single(trait.Body.Members);
        Assert.IsNotType<FunctionDeclaration>(member);
        Assert.IsType<DeclareFunctionSignature>(member);
    }
}
