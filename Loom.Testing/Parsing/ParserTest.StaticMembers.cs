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
    public void Parses_StaticPropertyDeclaration_OnInterface()
    {
        var tree = Utility.GetAST("interface Vector2 { static zero: Vector2 }");
        var declaration = Assert.IsType<InterfaceDeclaration>(Assert.Single(tree.Statements));
        var property = Assert.IsType<PropertyDeclaration>(Assert.Single(declaration.Body!.Members));

        Assert.True(property.IsStatic);
        Assert.NotNull(property.StaticKeyword);
        Assert.Null(property.MutKeyword);
        Assert.Equal("zero", property.Name.Text);
    }

    [Fact]
    public void Parses_StaticPropertyDeclaration_OnDeclaredInterface()
    {
        var tree = Utility.GetAST("declare interface Vector2 { static create: fn(x: number, y: number): Vector2 }");
        var declare = Assert.IsType<Declare>(Assert.Single(tree.Statements));
        var declaration = Assert.IsType<InterfaceDeclaration>(declare.Signature);
        var property = Assert.IsType<PropertyDeclaration>(Assert.Single(declaration.Body!.Members));

        Assert.True(property.IsStatic);
        Assert.Equal("create", property.Name.Text);
    }

    [Fact]
    public void Parses_NonStaticPropertyDeclaration_IsNotStatic()
    {
        var tree = Utility.GetAST("interface Vector2 { x: number }");
        var declaration = Assert.IsType<InterfaceDeclaration>(Assert.Single(tree.Statements));
        var property = Assert.IsType<PropertyDeclaration>(Assert.Single(declaration.Body!.Members));

        Assert.False(property.IsStatic);
        Assert.Null(property.StaticKeyword);
    }

    [Fact]
    public void Parses_ColonColon_AsQualifiedNameAccess()
    {
        var tree = Utility.GetAST("Vector2::create");
        var statement = Assert.IsType<ExpressionStatement>(Assert.Single(tree.Statements));
        var qualifiedName = Assert.IsType<QualifiedName>(statement.Expression);
        var dotName = Assert.Single(qualifiedName.Names);

        Assert.True(dotName.IsStatic);
        Assert.False(dotName.IsOptional);
        Assert.Equal("create", dotName.Name.Text);
    }

    [Fact]
    public void Parses_MixedDotAndColonColon_InOneChain()
    {
        var tree = Utility.GetAST("Vector2::create(1, 2).magnitude");
        var statement = Assert.IsType<ExpressionStatement>(Assert.Single(tree.Statements));
        var propertyAccess = Assert.IsType<PropertyAccess>(statement.Expression);
        var magnitude = Assert.Single(propertyAccess.Names);

        Assert.False(magnitude.IsStatic);
        Assert.Equal("magnitude", magnitude.Name.Text);

        var invocation = Assert.IsType<Invocation>(propertyAccess.Expression);
        var qualifiedName = Assert.IsType<QualifiedName>(invocation.Expression);
        var createAccess = Assert.Single(qualifiedName.Names);
        Assert.True(createAccess.IsStatic);
        Assert.Equal("create", createAccess.Name.Text);
    }

    [Fact]
    public void Parses_TurbofishInvocation_DistinctFromStaticAccess()
    {
        var tree = Utility.GetAST("foo::<number>()");
        var statement = Assert.IsType<ExpressionStatement>(Assert.Single(tree.Statements));
        var invocation = Assert.IsType<Invocation>(statement.Expression);

        Assert.IsType<Identifier>(invocation.Expression);
        Assert.NotNull(invocation.TypeArguments);
        Assert.Single(invocation.TypeArguments.ArgumentsList);
    }

    [Fact]
    public void Parses_StaticAccess_DistinctFromTurbofishInvocation()
    {
        var tree = Utility.GetAST("Foo::bar");
        var statement = Assert.IsType<ExpressionStatement>(Assert.Single(tree.Statements));
        var qualifiedName = Assert.IsType<QualifiedName>(statement.Expression);
        var dotName = Assert.Single(qualifiedName.Names);

        Assert.True(dotName.IsStatic);
        Assert.Equal("bar", dotName.Name.Text);
    }

    [Fact]
    public void Parses_StaticBlock_WithFieldsAndMethods()
    {
        const string source = """
            static Vector2 {
                zero = new Vector2 { x: 0, y: 0 };
                fn create(x, y) { return new Vector2 { x, y }; }
            }
            """;

        var tree = Utility.GetAST(source);
        var staticBlock = Assert.IsType<StaticBlock>(Assert.Single(tree.Statements));

        Assert.Equal("Vector2", staticBlock.InterfaceName.Name.Text);

        var field = Assert.Single(staticBlock.Body.Fields);
        Assert.Equal("zero", field.Name.Text);
        Assert.Null(field.ColonTypeClause);
        Assert.NotNull(field.EqualsValueClause);

        var method = Assert.Single(staticBlock.Body.Methods);
        Assert.Equal("create", method.Name.Text);
    }

    [Fact]
    public void Parses_StaticBlock_FieldWithExplicitType()
    {
        var tree = Utility.GetAST("static Vector2 { zero: Vector2 = new Vector2 { x: 0, y: 0 }; }");
        var staticBlock = Assert.IsType<StaticBlock>(Assert.Single(tree.Statements));
        var field = Assert.Single(staticBlock.Body.Fields);

        Assert.NotNull(field.ColonTypeClause);
    }

    [Fact]
    public void ThrowsFor_StaticOnNonInterfaceMember()
    {
        var diagnostics = Utility.GetParserDiagnostics("static x = 1;");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.UnexpectedToken, "Expected '{', got '='.");
    }

    [Fact]
    public void Parses_DeclareStaticBlock_WithSignatureMembers()
    {
        const string source = """
            declare static Result {
                ok: fn<T, Error>(value: T): Result<T, Error>;
                err: fn<T, Error>(error: Error): Result<T, Error>;
            }
            """;

        var tree = Utility.GetAST(source);
        var declare = Assert.IsType<Declare>(Assert.Single(tree.Statements));
        var staticBlock = Assert.IsType<DeclareStaticBlock>(declare.Signature);

        Assert.Equal("Result", staticBlock.Name.Text);
        Assert.Equal(2, staticBlock.Members.Count);
        Assert.Equal("ok", staticBlock.Members[0].Name.Text);
        Assert.Equal("err", staticBlock.Members[1].Name.Text);
    }

    [Fact]
    public void ThrowsFor_DeclareStaticBlockMember_MissingType()
    {
        var diagnostics = Utility.GetParserDiagnostics("declare static Result { ok }");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.ExpectedInterfaceMemberType, "Expected indexer type, got '}'.");
    }
}
