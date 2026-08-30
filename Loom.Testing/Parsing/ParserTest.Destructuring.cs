using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Testing;

namespace Loom.Testing.Parsing;

public partial class ParserTest
{
    [Fact]
    public void Parses_ArrayDestructuringTarget()
    {
        var result = Utility.AssertNoErrors(Utility.Parse("let [first, second] = array;"));
        var destructuringDeclaration = Assert.IsType<DestructuringDeclaration>(result.Tree.Statements.Single());
        var target = Assert.IsType<ArrayDestructuringTarget>(destructuringDeclaration.Target);
        Assert.Equal(["first", "second"], target.Elements.Select(e => e.Name!.Text));
    }

    [Fact]
    public void Parses_ObjectDestructuringTarget()
    {
        var result = Utility.AssertNoErrors(Utility.Parse("let { name, age } = user;"));
        var destructuringDeclaration = Assert.IsType<DestructuringDeclaration>(result.Tree.Statements.Single());
        var target = Assert.IsType<ObjectDestructuringTarget>(destructuringDeclaration.Target);
        Assert.Equal(["name", "age"], target.Fields.Select(f => f.Name.Text));
        Assert.All(target.Fields, f => Assert.Null(f.Alias));
    }

    [Fact]
    public void Parses_ObjectDestructuringTarget_WithFieldAlias()
    {
        var result = Utility.AssertNoErrors(Utility.Parse("let { age: userAge } = user;"));
        var destructuringDeclaration = Assert.IsType<DestructuringDeclaration>(result.Tree.Statements.Single());
        var target = Assert.IsType<ObjectDestructuringTarget>(destructuringDeclaration.Target);
        var field = Assert.Single(target.Fields);
        Assert.Equal("age", field.Name.Text);
        Assert.Equal("userAge", field.Alias!.Text);
        Assert.Equal("userAge", field.BindingName.Text);
    }

    [Fact]
    public void ThrowsFor_RestElement_InArrayDestructuringTarget()
    {
        var diagnostics = Utility.GetParserDiagnostics("let [first, ..rest] = array;");
        var diagnostic = diagnostics.Find(d => d.Code == InternalCodes.InvalidDestructureTarget);
        Assert.NotNull(diagnostic);
    }

    [Fact]
    public void ThrowsFor_RestElement_InObjectDestructuringTarget()
    {
        var diagnostics = Utility.GetParserDiagnostics("let { name, ..rest } = user;");
        var diagnostic = diagnostics.Find(d => d.Code == InternalCodes.InvalidDestructureTarget);
        Assert.NotNull(diagnostic);
    }

    [Fact]
    public void Parses_PlainVariableDeclaration_Unaffected()
    {
        var result = Utility.AssertNoErrors(Utility.Parse("let x = 1;"));
        var variableDeclaration = Assert.IsType<VariableDeclaration>(result.Tree.Statements.Single());
        Assert.Equal("x", variableDeclaration.Name.Text);
    }

    [Fact]
    public void Parses_ObjectDestructuringField_WithNestedObjectTarget()
    {
        var result = Utility.AssertNoErrors(Utility.Parse("let { address: { city } } = user;"));
        var destructuringDeclaration = Assert.IsType<DestructuringDeclaration>(result.Tree.Statements.Single());
        var target = Assert.IsType<ObjectDestructuringTarget>(destructuringDeclaration.Target);
        var field = Assert.Single(target.Fields);
        Assert.Equal("address", field.Name.Text);
        Assert.Null(field.Alias);

        var nested = Assert.IsType<ObjectDestructuringTarget>(field.NestedTarget);
        var nestedField = Assert.Single(nested.Fields);
        Assert.Equal("city", nestedField.Name.Text);
        Assert.Null(nestedField.NestedTarget);
    }

    [Fact]
    public void Parses_ObjectDestructuringField_WithNestedArrayTarget()
    {
        var result = Utility.AssertNoErrors(Utility.Parse("let { scores: [first, second] } = summary;"));
        var destructuringDeclaration = Assert.IsType<DestructuringDeclaration>(result.Tree.Statements.Single());
        var target = Assert.IsType<ObjectDestructuringTarget>(destructuringDeclaration.Target);
        var field = Assert.Single(target.Fields);
        Assert.Equal("scores", field.Name.Text);

        var nested = Assert.IsType<ArrayDestructuringTarget>(field.NestedTarget);
        Assert.Equal(["first", "second"], nested.Elements.Select(e => e.Name!.Text));
    }

    [Fact]
    public void Parses_ArrayDestructuringElement_WithNestedObjectTarget()
    {
        var result = Utility.AssertNoErrors(Utility.Parse("let [{ x }] = points;"));
        var destructuringDeclaration = Assert.IsType<DestructuringDeclaration>(result.Tree.Statements.Single());
        var target = Assert.IsType<ArrayDestructuringTarget>(destructuringDeclaration.Target);
        var element = Assert.Single(target.Elements);
        Assert.Null(element.Name);

        var nested = Assert.IsType<ObjectDestructuringTarget>(element.NestedTarget);
        Assert.Equal("x", Assert.Single(nested.Fields).Name.Text);
    }

    [Fact]
    public void ThrowsFor_NestedPattern_InTupleDestructuringTarget()
    {
        var diagnostics = Utility.GetParserDiagnostics("let (a, { b }) = pair;");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidDestructureTarget, "Tuple destructuring does not support nested patterns.");
    }

    [Fact]
    public void Parses_ArrayDestructuringElement_WithDefault()
    {
        var result = Utility.AssertNoErrors(Utility.Parse("let [first, second = 0] = maybe_pair;"));
        var destructuringDeclaration = Assert.IsType<DestructuringDeclaration>(result.Tree.Statements.Single());
        var target = Assert.IsType<ArrayDestructuringTarget>(destructuringDeclaration.Target);
        Assert.Null(target.Elements[0].EqualsValueClause);
        Assert.IsType<Literal>(target.Elements[1].EqualsValueClause!.Value);
    }

    [Fact]
    public void Parses_ObjectDestructuringField_WithDefault()
    {
        var result = Utility.AssertNoErrors(Utility.Parse("let { retries = 3 } = config;"));
        var destructuringDeclaration = Assert.IsType<DestructuringDeclaration>(result.Tree.Statements.Single());
        var target = Assert.IsType<ObjectDestructuringTarget>(destructuringDeclaration.Target);
        var field = Assert.Single(target.Fields);
        Assert.Equal("retries", field.BindingName.Text);
        Assert.IsType<Literal>(field.EqualsValueClause!.Value);
    }

    [Fact]
    public void Parses_ObjectDestructuringField_WithAliasAndDefault()
    {
        var result = Utility.AssertNoErrors(Utility.Parse("let { age: userAge = 0 } = user;"));
        var destructuringDeclaration = Assert.IsType<DestructuringDeclaration>(result.Tree.Statements.Single());
        var target = Assert.IsType<ObjectDestructuringTarget>(destructuringDeclaration.Target);
        var field = Assert.Single(target.Fields);
        Assert.Equal("userAge", field.BindingName.Text);
        Assert.IsType<Literal>(field.EqualsValueClause!.Value);
    }

    [Fact]
    public void Parses_NestedDestructuringTarget_WithDefault()
    {
        var result = Utility.AssertNoErrors(Utility.Parse("let [[a, b] = [1, 2]] = pairs;"));
        var destructuringDeclaration = Assert.IsType<DestructuringDeclaration>(result.Tree.Statements.Single());
        var target = Assert.IsType<ArrayDestructuringTarget>(destructuringDeclaration.Target);
        var element = Assert.Single(target.Elements);
        Assert.NotNull(element.NestedTarget);
        Assert.IsType<ArrayLiteral>(element.EqualsValueClause!.Value);
    }

    [Fact]
    public void ThrowsFor_DefaultValue_InTupleDestructuringTarget()
    {
        var diagnostics = Utility.GetParserDiagnostics("let (a, b = 1) = pair;");
        Utility.AssertDiagnostic(diagnostics, InternalCodes.InvalidDestructureTarget, "Tuple destructuring does not support default values.");
    }
}
