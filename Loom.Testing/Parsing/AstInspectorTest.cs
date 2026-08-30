// ReSharper disable UnusedAutoPropertyAccessor.Local
using System.Reflection;
using Loom.Core.Debug;
using Loom.Core.Diagnostics;
using Loom.Core.Lexing;
using Loom.Core.Parsing.AST;
using Loom.Core.Text;
using Loom.Testing;

namespace Loom.Testing.Parsing;

[Collection("Assembly")]
public class AstInspectorTest
{
    private static string Inspect(object node)
    {
        var method = typeof(AstInspector).GetMethod(
            "Inspect",
            BindingFlags.NonPublic | BindingFlags.Static,
            null,
            [typeof(object)],
            null
        )!;

        return (string)method.Invoke(null, [node])!;
    }

    private static string InspectTree(Tree tree)
    {
        var method = typeof(AstInspector).GetMethod(
            "Inspect",
            BindingFlags.NonPublic | BindingFlags.Static,
            null,
            [typeof(Tree)],
            null
        )!;

        return (string)method.Invoke(null, [tree])!;
    }

    [Fact]
    public void StringProperty_IsQuoted()
    {
        var node = new InspectorTestNode { Name = "hello" };
        var inspectionResult = Inspect(node);
        Assert.Contains("\"hello\"", inspectionResult);
    }

    [Fact]
    public void TokenProperty_FormatsAsSyntaxKindAndText()
    {
        var node = new InspectorTestNode { MyToken = new Token(SyntaxKind.Identifier, LocationSpan.Empty(), "foo") };
        var inspectionResult = Inspect(node);
        Assert.Contains("Token(Identifier, \"foo\")", inspectionResult);
    }

    [Fact]
    public void NestedNode_RecursesAndIndents()
    {
        var innerNode = new InspectorTestNode { Name = "inner" };
        var outerNode = new InspectorTestNode { Child = innerNode };
        var inspectionResult = Inspect(outerNode);

        Assert.Contains("Child:", inspectionResult);
        Assert.Contains("InspectorTestNode(", inspectionResult);
        Assert.Contains("\"inner\"", inspectionResult);
    }

    [Fact]
    public void CollectionProperty_ShowsBracketedList()
    {
        var node = new InspectorTestNode { Items = [new InspectorTestNode { Name = "a" }, new InspectorTestNode { Name = "b" }] };
        var inspectionResult = Inspect(node);

        Assert.Contains("Items: [", inspectionResult);
        Assert.Contains("InspectorTestNode(", inspectionResult);
        Assert.Contains("\"a\"", inspectionResult);
        Assert.Contains("\"b\"", inspectionResult);
        Assert.Contains("]", inspectionResult);
    }

    [Fact]
    public void EmptyCollection_ShowsEmptyBrackets()
    {
        var node = new InspectorTestNode { Items = [] };
        var inspectionResult = Inspect(node);
        Assert.Contains("Items: []", inspectionResult);
    }

    [Fact]
    public void NullProperty_OutputsNull()
    {
        var node = new InspectorTestNode { Name = null };
        var inspectionResult = Inspect(node);
        Assert.Contains("null", inspectionResult);
    }

    [Fact]
    public void DefaultValue_FallsBackToToString()
    {
        var node = new InspectorTestNode { Number = 42 };
        var inspectionResult = Inspect(node);
        Assert.Contains("Number: 42", inspectionResult);
    }

    [Fact]
    public void IgnoresProperties_InIgnoreSet()
    {
        var node = new IndexedType(
            new Token(SyntaxKind.LBracket, LocationSpan.Empty(), "["),
            new Token(SyntaxKind.RBracket, LocationSpan.Empty(), "]"),
            new InspectorTestType("MyArray"),
            new InspectorTestType("number")
        );

        var inspectionResult = Inspect(node);

        Assert.DoesNotContain("Parent:", inspectionResult);
        Assert.DoesNotContain("Span:", inspectionResult);
        Assert.DoesNotContain("Tokens:", inspectionResult);
        Assert.DoesNotContain("Children:", inspectionResult);
        Assert.DoesNotContain("Id:", inspectionResult);
        Assert.DoesNotContain("File:", inspectionResult);
        Assert.DoesNotContain("Keyword:", inspectionResult);
    }

    [Fact]
    public void IndentationLevels_AreCorrect()
    {
        var innerNode = new InspectorTestNode { Name = "inner" };
        var outerNode = new InspectorTestNode { Child = innerNode, Name = "outer" };

        var inspectionResult = Inspect(outerNode);
        Assert.Equal(
            "InspectorTestNode(\n  Name: \"outer\"\n  MyToken: null\n  Child: InspectorTestNode(\n    Name: \"inner\"\n    MyToken: null\n    Child: null\n    Items: null\n    Number: 0\n  )\n  Items: null\n  Number: 0\n)",
            inspectionResult
        );
    }

    [Fact]
    public void Inspects_IndexedType()
    {
        var indexType = new IndexedType(
            new Token(SyntaxKind.LBracket, LocationSpan.Empty(), "["),
            new Token(SyntaxKind.RBracket, LocationSpan.Empty(), "]"),
            new InspectorTestType("MyArray"),
            new InspectorTestType("number")
        );

        var inspectionResult = Inspect(indexType);

        Assert.Contains("IndexedType(", inspectionResult);
        Assert.Contains("LeftBracket: Token(LBracket, \"[\")", inspectionResult);
        Assert.Contains("RightBracket: Token(RBracket, \"]\")", inspectionResult);
        Assert.Contains("Type:", inspectionResult);
        Assert.Contains("IndexType:", inspectionResult);
        Assert.Contains("InspectorTestType(", inspectionResult);
    }

    [Fact]
    public void InspectTree_RendersStatements()
    {
        var lexerResult = new LexerResult([], [], new DiagnosticBag());
        var tree = new Tree(lexerResult, [new InspectorTestStatement { Name = "first" }, new InspectorTestStatement { Name = "second" }]);

        var inspectionResult = InspectTree(tree);

        Assert.Contains("InspectorTestStatement(", inspectionResult);
        Assert.Contains("\"first\"", inspectionResult);
        Assert.Contains("\"second\"", inspectionResult);
    }

    private sealed class InspectorTestNode()
        : Node([], [])
    {
        public string? Name { get; set; }
        public Token? MyToken { get; set; }
        public InspectorTestNode? Child { get; set; }
        public IReadOnlyList<object>? Items { get; set; }
        public int Number { get; set; }

        public override T Accept<T>(Visitor<T> visitor) => throw new NotImplementedException();
    }

    private sealed class InspectorTestType(string typeName)
        : TypeExpression([], [])
    {
        // ReSharper disable once UnusedMember.Local
        public string TypeName { get; } = typeName;

        public override T Accept<T>(Visitor<T> visitor) => throw new NotImplementedException();
    }

    private sealed class InspectorTestStatement()
        : Statement([], [])
    {
        public string? Name { get; set; }

        public override T Accept<T>(Visitor<T> visitor) => throw new NotImplementedException();
    }
}