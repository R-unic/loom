using Loom.Core.Diagnostics;
using Loom.Core.Lexing;
using Loom.Core.Parsing.AST;
using Loom.Core.Text;

namespace Loom.Testing;

[Collection("Assembly")]
public class SyntaxNodeTest
{
    private static Token T(string text, int start, int length, SyntaxKind kind = SyntaxKind.Identifier) =>
        new(kind, new LocationSpan(new Location(SourceFile.Empty, start), length), text);

    [Fact]
    public void Node_Id_IsAssigned_AndIncrements()
    {
        var token = T("x", 0, 1);
        var node1 = new Identifier(token);
        var node2 = new Identifier(token);
        Assert.True(node2.Id.Value > node1.Id.Value);
        Assert.NotEqual(node1.Id, node2.Id);
    }

    [Fact]
    public void Node_Parent_IsSet()
    {
        var literal = new Literal(T("42", 0, 2, SyntaxKind.NumberLiteral), 42L);
        var exprStmt = new ExpressionStatement(literal);
        Assert.Equal(exprStmt, literal.Parent);
    }

    [Fact]
    public void Node_Parent_IsNotSet_ForRoot()
    {
        var literal = new Literal(T("42", 0, 2, SyntaxKind.NumberLiteral), 42L);
        Assert.Null(literal.Parent);
    }

    [Fact]
    public void Node_Span_DerivedFromTokens()
    {
        var letKw = T("let", 0, 3, SyntaxKind.LetKeyword);
        var name = T("x", 4, 1);
        var decl = new VariableDeclaration(letKw, name, null, null);
        Assert.Equal(5, decl.LocationSpan.Length);
        Assert.Equal(1, decl.LocationSpan.Start.Line);
        Assert.Equal(0, decl.LocationSpan.Start.Position);
        Assert.Equal(1, decl.LocationSpan.End.Line);
        Assert.Equal(5, decl.LocationSpan.End.Position);
    }

    [Fact]
    public void Node_Span_Empty_WhenNoTokens()
    {
        var token = new Token(SyntaxKind.Identifier, LocationSpan.Empty(), "");
        var ident = new Identifier(token);
        Assert.Equal(SourceFile.Empty, ident.LocationSpan.File);
        Assert.Equal(0, ident.LocationSpan.Length);
        Assert.Equal(1, ident.LocationSpan.Start.Line);
        Assert.Equal(0, ident.LocationSpan.Start.Position);
        Assert.Equal(1, ident.LocationSpan.Start.Line);
        Assert.Equal(0, ident.LocationSpan.End.Position);
    }

    [Fact]
    public void GetDescendants_ReturnsChildrenAndGrandchildren()
    {
        var letKw = T("let", 0, 3, SyntaxKind.LetKeyword);
        var name = T("x", 4, 1);
        var equals = T("=", 6, 1, SyntaxKind.Equals);
        var lit = new Literal(T("1", 8, 1, SyntaxKind.NumberLiteral), 1L);
        var init = new EqualsValueClause(equals, lit);
        var varDecl = new VariableDeclaration(letKw, name, null, init);
        var lbrace = T("{", 0, 1, SyntaxKind.LBrace);
        var rbrace = T("}", 10, 1, SyntaxKind.RBrace);
        var block = new Block(lbrace, rbrace, [varDecl]);

        var descendants = block.GetDescendants();
        Assert.Equal(3, descendants.Count);
        Assert.Contains(varDecl, descendants);
        Assert.Contains(init, descendants);
        Assert.Contains(lit, descendants);
    }

    [Fact]
    public void GetDescendants_T_FiltersByType()
    {
        var letKw = T("let", 0, 3, SyntaxKind.LetKeyword);
        var nameX = T("x", 4, 1);
        var equals = T("=", 6, 1, SyntaxKind.Equals);
        var lit = new Literal(T("1", 8, 1, SyntaxKind.NumberLiteral), 1L);
        var init = new EqualsValueClause(equals, lit);
        var varDecl = new VariableDeclaration(letKw, nameX, null, init);

        var retKw = T("return", 10, 6, SyntaxKind.ReturnKeyword);
        var retExpr = new Identifier(T("x", 17, 1));
        var retStmt = new Return(retKw, retExpr);

        var lbrace = T("{", 0, 1, SyntaxKind.LBrace);
        var rbrace = T("}", 19, 1, SyntaxKind.RBrace);
        var block = new Block(lbrace, rbrace, [varDecl, retStmt]);

        var identifiers = block.GetDescendants<Identifier>();
        Assert.Single(identifiers);
        Assert.Contains(retExpr, identifiers);
    }

    [Fact]
    public void IsDescendantOf()
    {
        var lit = new Literal(T("42", 0, 2, SyntaxKind.NumberLiteral), 42L);
        var exprStmt = new ExpressionStatement(lit);
        var block = new Block(T("{", 0, 1, SyntaxKind.LBrace), T("}", 3, 1, SyntaxKind.RBrace), [exprStmt]);
        Assert.True(lit.IsDescendantOf<Block>());
        Assert.Equal(lit.FirstAncestorOfType<Block>(), block);
    }

    [Fact]
    public void FirstAncestorOfType_FindsImmediateParent()
    {
        var lit = new Literal(T("42", 0, 2, SyntaxKind.NumberLiteral), 42L);
        var exprStmt = new ExpressionStatement(lit);
        var block = new Block(T("{", 0, 1, SyntaxKind.LBrace), T("}", 3, 1, SyntaxKind.RBrace), [exprStmt]);

        var found = lit.FirstAncestorOfType<Block>();
        Assert.Same(block, found);
    }

    [Fact]
    public void FirstAncestorOfType_ClimbsMultipleLevels()
    {
        var lit = new Literal(T("42", 0, 2, SyntaxKind.NumberLiteral), 42L);
        var exprStmt = new ExpressionStatement(lit);
        var ifBody = new Block(T("{", 0, 1, SyntaxKind.LBrace), T("}", 3, 1, SyntaxKind.RBrace), [exprStmt]);
        var condition = new Identifier(T("cond", 10, 4));
        var ifKw = T("if", 14, 2, SyntaxKind.IfKeyword);
        var ifStmt = new If(ifKw, condition, ifBody, null);

        var found = lit.FirstAncestorOfType<If>();
        Assert.Same(ifStmt, found);

        var foundRoot = lit.FirstAncestorOfType<Block>();
        Assert.Same(ifBody, foundRoot);
    }

    [Fact]
    public void FirstAncestorOfType_ReturnsNull_WhenNone()
    {
        var lit = new Literal(T("42", 0, 2, SyntaxKind.NumberLiteral), 42L);
        Assert.Null(lit.FirstAncestorOfType<Block>());
    }

    [Fact]
    public void FirstAncestorOfType_ReturnsNull_ForTreeItself()
    {
        var lit = new Literal(T("42", 0, 2, SyntaxKind.NumberLiteral), 42L);
        var exprStmt = new ExpressionStatement(lit);
        var tree = new Tree(new LexerResult([], [], new DiagnosticBag()), [exprStmt]);
        Assert.Null(tree.FirstAncestorOfType<Tree>());
        Assert.Same(tree, lit.FirstAncestorOfType<Tree>());
    }

    [Fact]
    public void ToString_ReturnsSpanText()
    {
        var fileWithText = new SourceFile("content.loom", "hello");
        var token = new Token(SyntaxKind.Identifier, new LocationSpan(Location.Empty(fileWithText), 5), "hello");
        var ident = new Identifier(token);
        Assert.Equal("hello", ident.ToString());
    }
}