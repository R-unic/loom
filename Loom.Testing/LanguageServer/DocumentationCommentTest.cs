using Loom.Core.Parsing.AST;
using Loom.Core.Text;
using Loom.Testing;

namespace Loom.Testing.LanguageServer;

[Collection("Assembly")]
public class DocumentationCommentTest
{
    [Theory]
    [InlineData("### documented", SyntaxKind.DocComment)]
    [InlineData("## plain", SyntaxKind.Comment)]
    [InlineData("#### a rule, not prose", SyntaxKind.Comment)]
    [InlineData("###", SyntaxKind.DocComment)]
    [InlineData("##", SyntaxKind.Comment)]
    public void Lexer_TellsDocCommentsApartFromPlainOnes(string source, SyntaxKind expected) =>
        Assert.Equal(expected, Utility.GetTokens(source, true)[0].Kind);

    [Fact]
    public void DocComments_AreTrivia() => Assert.Single(Utility.GetTokens("### documented"));

    [Fact]
    public void Documentation_AttachesToTheDeclarationBelowIt()
    {
        var tree = Utility.GetAST(
            """
            ### Adds two numbers.
            fn add(x: number, y: number): number { return x + y; }
            """
        );

        Assert.Equal("Adds two numbers.", Assert.Single(tree.GetDescendants<FunctionDeclaration>()).Documentation);
    }

    [Fact]
    public void Documentation_JoinsAConsecutiveRunIntoOneBlock()
    {
        var tree = Utility.GetAST(
            """
            ### Adds two numbers.
            ###
            ### Returns their sum.
            fn add(x: number, y: number): number { return x + y; }
            """
        );

        Assert.Equal("Adds two numbers.\n\nReturns their sum.", Assert.Single(tree.GetDescendants<FunctionDeclaration>()).Documentation);
    }

    [Fact]
    public void Documentation_IgnoresAPlainCommentInterruptingTheRun()
    {
        var tree = Utility.GetAST(
            """
            ### Adds two numbers.
            ## an aside about the implementation
            fn add(x: number, y: number): number { return x + y; }
            """
        );

        Assert.Null(Assert.Single(tree.GetDescendants<FunctionDeclaration>()).Documentation);
    }

    [Fact]
    public void Documentation_IsFoundAboveAttributesAndBelowThem()
    {
        const string above = """
                             ### The packet sent on join.
                             [serializable]
                             interface Packet { id: u8; }
                             """;

        const string below = """
                             [serializable]
                             ### The packet sent on join.
                             interface Packet { id: u8; }
                             """;

        foreach (var source in new[] { above, below })
            Assert.Equal("The packet sent on join.", Assert.Single(Utility.GetAST(source).GetDescendants<InterfaceDeclaration>()).Documentation);
    }

    [Fact]
    public void Documentation_ReachesTheSignatureUnderADeclareKeyword()
    {
        var tree = Utility.GetAST(
            """
            ### Writes its arguments to the output.
            declare fn write(..data: unknown[]): void;
            """
        );

        Assert.Equal("Writes its arguments to the output.", Assert.Single(tree.GetDescendants<DeclareFunctionSignature>()).Documentation);
    }

    [Fact]
    public void Documentation_IsNullForAnUndocumentedDeclaration() =>
        Assert.Null(Assert.Single(Utility.GetAST("fn add(x: number): number { return x; }").GetDescendants<FunctionDeclaration>()).Documentation);

    [Fact]
    public void Documentation_KeepsIndentationBeyondTheOneSeparatingSpace()
    {
        var tree = Utility.GetAST(
            """
            ### Usage:
            ###     add(1, 2)
            fn add(x: number, y: number): number { return x + y; }
            """
        );

        Assert.Equal("Usage:\n    add(1, 2)", Assert.Single(tree.GetDescendants<FunctionDeclaration>()).Documentation);
    }

    [Fact]
    public void Documentation_DoesNotLeakOntoALaterDeclaration()
    {
        var tree = Utility.GetAST(
            """
            ### The first one.
            fn first(): void { }

            fn second(): void { }
            """
        );

        var functions = tree.GetDescendants<FunctionDeclaration>();
        Assert.Equal("The first one.", functions[0].Documentation);
        Assert.Null(functions[1].Documentation);
    }

    [Fact]
    public void DocComment_AtEndOfFile_DoesNotOverrun() => Utility.AssertNoErrors(Utility.GetLexerDiagnostics("###"));
}
