using Loom.Core.Debug;
using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Text;
using Loom.Core.TypeChecking.Serialization;
using PrimitiveTypeKind = Loom.Core.TypeChecking.Types.PrimitiveTypeKind;

namespace Loom.Testing;

public partial class ParserTest
{
    [Fact]
    public void Parses_InterfaceEvent_WithAttribute()
    {
        const string source = """
            interface X {
                [luau_name("Foo")]
                event abc(x: number);
            }
            """;

        var result = Utility.AssertNoErrors(Utility.Parse(source));
        var interfaceDeclaration = Assert.IsType<InterfaceDeclaration>(result.Tree.Statements.Single());
        Assert.NotNull(interfaceDeclaration.Body);

        var eventDeclaration = Assert.IsType<EventDeclaration>(Assert.Single(interfaceDeclaration.Body.Members));
        Assert.NotNull(eventDeclaration.Attributes);

        var attribute = Assert.Single(eventDeclaration.Attributes.AttributeList);
        var identifier = Assert.IsType<Identifier>(attribute.Expression);
        Assert.Equal("luau_name", identifier.Name.Text);
    }

    [Fact]
    public void Parses_TopLevelEvent_WithAttribute()
    {
        const string source = """
            [luau_name("Foo")]
            event abc(x: number);
            """;

        var result = Utility.AssertNoErrors(Utility.Parse(source));
        var eventDeclaration = Assert.IsType<EventDeclaration>(result.Tree.Statements.Single());
        Assert.NotNull(eventDeclaration.Attributes);

        var attribute = Assert.Single(eventDeclaration.Attributes.AttributeList);
        var identifier = Assert.IsType<Identifier>(attribute.Expression);
        Assert.Equal("luau_name", identifier.Name.Text);
    }

    [Fact]
    public void Parses_InterpolatedStringLiteral()
    {
        var tree = Utility.GetAST("""$"Welcome, {name}!";""");
        var statement = Assert.IsType<ExpressionStatement>(Assert.Single(tree.Statements));
        var interpolated = Assert.IsType<InterpolatedStringLiteral>(statement.Expression);

        Assert.Equal(3, interpolated.Parts.Count);
        var leading = Assert.IsType<InterpolationTextPart>(interpolated.Parts[0]);
        Assert.Equal("Welcome, ", leading.Text);

        var hole = Assert.IsType<InterpolationHolePart>(interpolated.Parts[1]);
        var identifier = Assert.IsType<Identifier>(hole.Expression);
        Assert.Equal("name", identifier.Name.Text);

        var trailing = Assert.IsType<InterpolationTextPart>(interpolated.Parts[2]);
        Assert.Equal("!", trailing.Text);

        Assert.Equal([hole.Expression], interpolated.Expressions);
    }

    [Fact]
    public void Parses_InterpolatedStringLiteral_WithMultipleHoles()
    {
        var tree = Utility.GetAST("""$"{a}{b}";""");
        var statement = Assert.IsType<ExpressionStatement>(Assert.Single(tree.Statements));
        var interpolated = Assert.IsType<InterpolatedStringLiteral>(statement.Expression);

        Assert.Equal(2, interpolated.Parts.Count);
        Assert.All(interpolated.Parts, part => Assert.IsType<InterpolationHolePart>(part));
        Assert.Equal(2, interpolated.Expressions.Count);
    }

    [Fact]
    public void Parses_InterpolatedStringLiteral_WithNoHoles()
    {
        var tree = Utility.GetAST("""$"just text";""");
        var statement = Assert.IsType<ExpressionStatement>(Assert.Single(tree.Statements));
        var interpolated = Assert.IsType<InterpolatedStringLiteral>(statement.Expression);

        var part = Assert.Single(interpolated.Parts);
        var text = Assert.IsType<InterpolationTextPart>(part);
        Assert.Equal("just text", text.Text);
        Assert.Empty(interpolated.Expressions);
    }

    [Fact]
    public void Parses_InterpolatedStringLiteral_WithNestedExpression()
    {
        var tree = Utility.GetAST("""$"n is {1 + 2}";""");
        var statement = Assert.IsType<ExpressionStatement>(Assert.Single(tree.Statements));
        var interpolated = Assert.IsType<InterpolatedStringLiteral>(statement.Expression);

        var hole = Assert.IsType<InterpolationHolePart>(interpolated.Parts[1]);
        Assert.IsType<BinaryOperator>(hole.Expression);
    }

    [Fact]
    public void Parses_TopLevelArrayLiteralStatement_NotMistakenForAttributes()
    {
        var tree = Utility.GetAST("[1, 2, 3];");
        var statement = Assert.IsType<ExpressionStatement>(Assert.Single(tree.Statements));
        var arrayLiteral = Assert.IsType<ArrayLiteral>(statement.Expression);
        Assert.Equal(3, arrayLiteral.Expressions.Count);
    }

    [Theory]
    [InlineData("[..a];", 1, 0)]
    [InlineData("[1, ..a];", 2, 1)]
    [InlineData("[..a, 1];", 2, 0)]
    [InlineData("[..a, ..b];", 2, 0)]
    public void Parses_SpreadElements(string source, int elementCount, int spreadIndex)
    {
        var tree = Utility.GetAST(source);
        var statement = Assert.IsType<ExpressionStatement>(Assert.Single(tree.Statements));
        var arrayLiteral = Assert.IsType<ArrayLiteral>(statement.Expression);

        Assert.Equal(elementCount, arrayLiteral.Expressions.Count);
        var spread = Assert.IsType<SpreadElement>(arrayLiteral.Expressions[spreadIndex]);
        Assert.Equal(SyntaxKind.DotDot, spread.DotDot.Kind);
        Assert.IsType<Identifier>(spread.Expression);
    }

    [Theory]
    [InlineData("f(..a);", 1, 0)]
    [InlineData("f(1, ..a);", 2, 1)]
    [InlineData("f(..a, 1);", 2, 0)]
    public void Parses_SpreadArguments(string source, int argumentCount, int spreadIndex)
    {
        var tree = Utility.GetAST(source);
        var invocation = Assert.Single(tree.GetDescendants<Invocation>());

        Assert.Equal(argumentCount, invocation.Arguments.ArgumentList.Count);
        var spread = Assert.IsType<SpreadElement>(invocation.Arguments.ArgumentList[spreadIndex]);
        Assert.IsType<Identifier>(spread.Expression);
    }

    [Theory]
    [InlineData("let x = [..];", "']'")]
    [InlineData("let x = [1, ..];", "']'")]
    [InlineData("f(..);", "')'")]
    public void ThrowsFor_SpreadWithNoOperand(string source, string got) =>
        Utility.AssertDiagnostic(Utility.GetParserDiagnostics(source), InternalCodes.UnexpectedToken, $"Expected expression, got {got}.");

    [Fact]
    public void Parses_NamedArgument()
    {
        var tree = Utility.GetAST("f(target: 1);");
        var invocation = Assert.Single(tree.GetDescendants<Invocation>());

        var namedArgument = Assert.IsType<NamedArgument>(Assert.Single(invocation.Arguments.ArgumentList));
        Assert.Equal("target", namedArgument.Name.Text);
        Assert.Equal(SyntaxKind.Colon, namedArgument.Colon.Kind);
        var literal = Assert.IsType<Literal>(namedArgument.Value);
        Assert.Equal(1L, literal.Value);
    }

    [Fact]
    public void Parses_MixedPositionalAndNamedArguments()
    {
        var tree = Utility.GetAST("f(1, b: 2, c: 3);");
        var invocation = Assert.Single(tree.GetDescendants<Invocation>());

        Assert.Equal(3, invocation.Arguments.ArgumentList.Count);
        Assert.IsType<Literal>(invocation.Arguments.ArgumentList[0]);
        Assert.Equal("b", Assert.IsType<NamedArgument>(invocation.Arguments.ArgumentList[1]).Name.Text);
        Assert.Equal("c", Assert.IsType<NamedArgument>(invocation.Arguments.ArgumentList[2]).Name.Text);
    }

    [Fact]
    public void ThrowsFor_PositionalArgumentAfterNamed() =>
        Utility.AssertDiagnostic(
            Utility.GetParserDiagnostics("f(a: 1, 2);"),
            InternalCodes.PositionalArgumentAfterNamed,
            "A positional argument cannot follow a named argument."
        );

    [Fact]
    public void ThrowsFor_SpreadArgumentCombinedWithNamedArgument() =>
        Utility.AssertDiagnostic(
            Utility.GetParserDiagnostics("f(a: 1, ..b);"),
            InternalCodes.NamedArgumentWithSpread,
            "A spread argument cannot be combined with named arguments."
        );

    [Fact]
    public void ThrowsFor_SpreadArgumentBeforeNamedArgument() =>
        Utility.AssertDiagnostic(
            Utility.GetParserDiagnostics("f(..b, a: 1);"),
            InternalCodes.NamedArgumentWithSpread,
            "A spread argument cannot be combined with named arguments."
        );

    [Fact]
    public void ThrowsFor_DuplicateNamedArgument() =>
        Utility.AssertDiagnostic(
            Utility.GetParserDiagnostics("f(a: 1, a: 2);"),
            InternalCodes.DuplicateNamedArgument,
            "Argument 'a' is already specified."
        );

    [Fact]
    public void Parses_SpreadOfRange_AsOneOperand()
    {
        var tree = Utility.GetAST("[..a..b];");
        var statement = Assert.IsType<ExpressionStatement>(Assert.Single(tree.Statements));
        var arrayLiteral = Assert.IsType<ArrayLiteral>(statement.Expression);

        var spread = Assert.IsType<SpreadElement>(Assert.Single(arrayLiteral.Expressions));
        Assert.IsType<RangeLiteral>(spread.Expression);
    }

    [Fact]
    public void Parses_MutSpreadArrayLiteral()
    {
        var tree = Utility.GetAST("let x = mut [..a];");
        var arrayLiteral = Assert.Single(tree.GetDescendants<ArrayLiteral>());

        Assert.NotNull(arrayLiteral.MutKeyword);
        Assert.IsType<SpreadElement>(Assert.Single(arrayLiteral.Expressions));
    }

    [Fact]
    public void Parses_MutEvent_StillProducesMutEventError()
    {
        const string plainSource = """
            interface X {
                mut event abc;
            }
            """;

        const string attributedSource = """
            interface X {
                [luau_name("Foo")]
                mut event abc;
            }
            """;

        var plainDiagnostics = Utility.GetParserDiagnostics(plainSource);
        var attributedDiagnostics = Utility.GetParserDiagnostics(attributedSource);
        Assert.Equal(plainDiagnostics.Set.Count, attributedDiagnostics.Set.Count);
    }
}
