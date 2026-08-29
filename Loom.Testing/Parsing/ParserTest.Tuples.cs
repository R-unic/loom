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
    public void Parses_TupleType()
    {
        var result = Utility.AssertNoErrors(Utility.Parse("let x: (string, number) = (\"a\", 1);"));
        var variableDeclaration = Assert.IsType<VariableDeclaration>(result.Tree.Statements.Single());
        var tupleType = Assert.IsType<TupleType>(variableDeclaration.ColonTypeClause!.Type);
        Assert.Equal(2, tupleType.Types.Count);
        Assert.IsType<PrimitiveType>(tupleType.Types[0]);
        Assert.IsType<PrimitiveType>(tupleType.Types[1]);
    }

    [Theory]
    [InlineData("u8", NumberType.U8)]
    [InlineData("u16", NumberType.U16)]
    [InlineData("u32", NumberType.U32)]
    [InlineData("i8", NumberType.I8)]
    [InlineData("i16", NumberType.I16)]
    [InlineData("i32", NumberType.I32)]
    [InlineData("f32", NumberType.F32)]
    [InlineData("f64", NumberType.F64)]
    public void Parses_SizedNumberType(string typeName, NumberType expected)
    {
        var result = Utility.AssertNoErrors(Utility.Parse($"let x: {typeName} = 1;"));
        var variableDeclaration = Assert.IsType<VariableDeclaration>(result.Tree.Statements.Single());
        var primitiveType = Assert.IsType<PrimitiveType>(variableDeclaration.ColonTypeClause!.Type);

        Assert.Equal(PrimitiveTypeKind.Number, primitiveType.Kind);
        Assert.Equal(expected, primitiveType.Width);
    }

    [Fact]
    public void Parses_StringWithTypeArgument()
    {
        var result = Utility.AssertNoErrors(Utility.Parse("let x: string<u8> = \"a\";"));
        var variableDeclaration = Assert.IsType<VariableDeclaration>(result.Tree.Statements.Single());
        var primitiveType = Assert.IsType<PrimitiveType>(variableDeclaration.ColonTypeClause!.Type);

        Assert.Equal(PrimitiveTypeKind.String, primitiveType.Kind);
        var argument = Assert.Single(primitiveType.TypeArguments!.ArgumentsList);
        Assert.Equal(NumberType.U8, Assert.IsType<PrimitiveType>(argument).Width);
    }

    [Fact]
    public void Parses_BareString_WithNoTypeArguments()
    {
        var result = Utility.AssertNoErrors(Utility.Parse("let x: string = \"a\";"));
        var variableDeclaration = Assert.IsType<VariableDeclaration>(result.Tree.Statements.Single());
        var primitiveType = Assert.IsType<PrimitiveType>(variableDeclaration.ColonTypeClause!.Type);

        Assert.Null(primitiveType.TypeArguments);
    }

    [Fact]
    public void Parses_TupleExpression()
    {
        var result = Utility.AssertNoErrors(Utility.Parse("let x = (\"a\", 1);"));
        var variableDeclaration = Assert.IsType<VariableDeclaration>(result.Tree.Statements.Single());
        var tupleExpression = Assert.IsType<TupleExpression>(variableDeclaration.EqualsValueClause!.Value);
        Assert.Equal(2, tupleExpression.Expressions.Count);
    }

    [Fact]
    public void Parses_ParenthesizedExpression_WithoutComma_StaysGrouping()
    {
        var result = Utility.AssertNoErrors(Utility.Parse("let x = (1);"));
        var variableDeclaration = Assert.IsType<VariableDeclaration>(result.Tree.Statements.Single());
        Assert.IsType<Parenthesized>(variableDeclaration.EqualsValueClause!.Value);
    }

    [Fact]
    public void Parses_ParenthesizedType_WithoutComma_StaysGrouping()
    {
        var result = Utility.AssertNoErrors(Utility.Parse("let x: (number) = 1;"));
        var variableDeclaration = Assert.IsType<VariableDeclaration>(result.Tree.Statements.Single());
        Assert.IsType<ParenthesizedType>(variableDeclaration.ColonTypeClause!.Type);
    }

    [Fact]
    public void Parses_TupleDestructuringTarget()
    {
        var result = Utility.AssertNoErrors(Utility.Parse("let (one, two) = t;"));
        var destructuringDeclaration = Assert.IsType<DestructuringDeclaration>(result.Tree.Statements.Single());
        var target = Assert.IsType<TupleDestructuringTarget>(destructuringDeclaration.Target);
        Assert.Equal(["one", "two"], target.Elements.Select(e => e.Name!.Text));
    }

    [Fact]
    public void Parses_TuplePattern_InMatch()
    {
        var result = Utility.AssertNoErrors(Utility.Parse("match t { (a, b) -> a, _ -> \"none\" };"));
        var matchExpression = Assert.IsType<MatchExpression>(Assert.IsType<ExpressionStatement>(result.Tree.Statements.Single()).Expression);
        var tuplePattern = Assert.IsType<TuplePattern>(matchExpression.Arms[0].Pattern);
        Assert.Equal(2, tuplePattern.Patterns.Count);
        Assert.IsType<IdentifierPattern>(tuplePattern.Patterns[0]);
        Assert.IsType<IdentifierPattern>(tuplePattern.Patterns[1]);
    }

    [Fact]
    public void ThrowsFor_RestElement_InTuplePattern()
    {
        var diagnostics = Utility.GetParserDiagnostics("match t { (a, ..b) -> a, _ -> \"none\" };");
        var diagnostic = diagnostics.Find(d => d.Code == InternalCodes.UnexpectedToken);
        Assert.NotNull(diagnostic);
    }
}
