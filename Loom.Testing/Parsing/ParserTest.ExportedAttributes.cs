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
    public void Parses_AttributesOnExportedInterface()
    {
        var tree = Utility.GetAST("[serializable]\nexport interface Foo { x: number }");
        var declaration = tree.GetDescendants<InterfaceDeclaration>().Single();

        Assert.NotNull(declaration.Attributes);
        Assert.Equal("serializable", Assert.Single(declaration.Attributes.AttributeList).Expression.ToString());
    }

    [Fact]
    public void Parses_AttributesOnExportedFunction()
    {
        var tree = Utility.GetAST("[luau_name(\"Bar\")]\nexport fn foo -> 1");
        var declaration = tree.GetDescendants<FunctionDeclaration>().Single();

        Assert.NotNull(declaration.Attributes);
        Assert.Single(declaration.Attributes.AttributeList);
    }

    [Fact]
    public void ThrowsFor_AttributesOnExportedTypeAlias()
    {
        var diagnostics = Utility.GetParserDiagnostics("[serializable]\nexport type Foo = number;");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.AttributesNotSupportedOnDeclaration,
            "Attributes are not supported on 'type' declarations."
        );
    }
}
