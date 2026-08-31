using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;

namespace Loom.Testing.Parsing;

public partial class ParserTest
{
    [Fact]
    public void Parses_TopLevelDeclareFunctionSignature_WithAttribute()
    {
        const string source = """
            [luau_name("typeof")]
            declare fn type_of(value: unknown): string;
            """;

        var result = Utility.AssertNoErrors(Utility.Parse(source));
        var declare = Assert.IsType<Declare>(result.Tree.Statements.Single());
        var signature = Assert.IsType<DeclareFunctionSignature>(declare.Signature);
        Assert.NotNull(signature.Attributes);

        var attribute = Assert.Single(signature.Attributes.AttributeList);
        var identifier = Assert.IsType<Identifier>(attribute.Expression);
        Assert.Equal("luau_name", identifier.Name.Text);
    }

    [Fact]
    public void Parses_TopLevelDeclareEventSignature_WithAttribute()
    {
        const string source = """
            [luau_name("OnConsume")]
            declare event consumer(param: string);
            """;

        var result = Utility.AssertNoErrors(Utility.Parse(source));
        var declare = Assert.IsType<Declare>(result.Tree.Statements.Single());
        var signature = Assert.IsType<EventDeclaration>(declare.Signature);
        Assert.NotNull(signature.Attributes);

        var attribute = Assert.Single(signature.Attributes.AttributeList);
        var identifier = Assert.IsType<Identifier>(attribute.Expression);
        Assert.Equal("luau_name", identifier.Name.Text);
    }

    [Fact]
    public void ThrowsFor_AttributesBeforeDeclareLet()
    {
        const string source = """
            [luau_name("x")]
            declare let y: number;
            """;

        var diagnostics = Utility.GetParserDiagnostics(source);
        Assert.Contains(diagnostics.Set, d => d.Code == InternalCodes.AttributesNotSupportedOnDeclaration);
    }

    [Fact]
    public void ThrowsFor_DoubleAttributesBeforeAndAfterDeclare()
    {
        const string source = """
            [luau_name("x")]
            declare [luau_name("y")]
            fn foo(): void;
            """;

        var diagnostics = Utility.GetParserDiagnostics(source);
        Assert.Contains(diagnostics.Set, d => d.Severity == DiagnosticSeverity.Error);
    }
}
