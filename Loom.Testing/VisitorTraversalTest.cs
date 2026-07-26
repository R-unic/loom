using Loom.Core.Parsing.AST;

namespace Loom.Testing;

[Collection("Assembly")]
public class VisitorTraversalTest
{
    private sealed class RecordingVisitor() : Visitor<bool>(_ => true)
    {
        public List<string> Log { get; } = [];

        public bool Record(Tree tree) => Visit(tree);
        protected override bool Visit(Node node) => LogAndVisit(node.GetType().Name, () => node.Accept(this));

        private bool LogAndVisit(string name, Func<bool> visitChildren)
        {
            Log.Add(name);
            return visitChildren();
        }
    }

    private static void AssertVisitOrder(string source, params string[] expectedOrder)
    {
        var tree = Utility.GetAST(source);
        var recorder = new RecordingVisitor();
        recorder.Record(tree);
        Assert.Equal(expectedOrder.Prepend("Tree"), recorder.Log);
    }

    [Fact]
    public void ExpressionStatement_VisitsExpression() => AssertVisitOrder("42", "ExpressionStatement", "Literal");

    [Theory]
    [InlineData("enum E { A }")]
    [InlineData("enum Empty { }")]
    [InlineData("interface I { }")]
    [InlineData("let x = 1")]
    public void ReturnsVisitorDefault_ForAbsentChildren(string source)
    {
        var recorder = new RecordingVisitor();
        Assert.True(recorder.Record(Utility.GetAST(source)));
    }
    
    [Fact]
    public void InterfaceInvocation_VisitsTypeArgumentsAndBody() =>
        AssertVisitOrder(
            "new Foo::<number> { foo: 69, [69]: true, bar }",
            "ExpressionStatement",
            "InterfaceInvocation",
            "Identifier",
            "TypeArguments",
            "PrimitiveType",
            "InterfaceInvocationBody",
            "PropertyInitializer",
            "Literal",
            "IndexInitializer",
            "Literal",
            "Literal",
            "ShorthandPropertyInitializer",
            "Identifier"
        );

    [Fact]
    public void Import_VisitsSpecifiersAndModulePath() =>
        AssertVisitOrder(
            "import { square, pi as PI } from \"./math\"",
            "ImportDeclaration",
            "ImportSpecifier",
            "ImportSpecifier",
            "Literal"
        );

    [Fact]
    public void ImportType_VisitsSpecifiersAndModulePath() =>
        AssertVisitOrder(
            "import type { Vector } from \"./vector\"",
            "ImportDeclaration",
            "ImportSpecifier",
            "Literal"
        );

    [Fact]
    public void ExportList_VisitsSpecifiersAndModulePath() =>
        AssertVisitOrder(
            "export { a, b as c } from \"./math\"",
            "ExportList",
            "ExportSpecifier",
            "ExportSpecifier",
            "Literal"
        );

    [Fact]
    public void NamespaceImport_DoesNotVisitItsModulePath() =>
        AssertVisitOrder("import * as math from \"./math\"", "NamespaceImport");

    [Fact]
    public void Interface_VisitsConstraintsAndMembers() =>
        AssertVisitOrder(
            "interface A: B, C { [bool]: number, a: number, [some_attribute(69), balls] b: number }",
            "InterfaceDeclaration",
            "ColonTypeListClause",
            "TypeName",
            "TypeName",
            "InterfaceBody",
            "IndexerDeclaration",
            "PrimitiveType",
            "ColonTypeClause",
            "PrimitiveType",
            "PropertyDeclaration",
            "ColonTypeClause",
            "PrimitiveType",
            "PropertyDeclaration",
            "Attributes",
            "Attribute",
            "Identifier",
            "Arguments",
            "Literal",
            "Attribute",
            "Identifier",
            "Arguments",
            "ColonTypeClause",
            "PrimitiveType"
        );
    
    [Fact]
    public void KeyOf_VisitsType() =>
        AssertVisitOrder(
            "type X = keyof(number);",
            "TypeAlias",
            "EqualsTypeClause",
            "KeyOf",
            "PrimitiveType"
        );
    
    [Fact]
    public void TypeOf_VisitsExpression() =>
        AssertVisitOrder(
            "type X = typeof(a.b);",
            "TypeAlias",
            "EqualsTypeClause",
            "TypeOf",
            "QualifiedName",
            "Identifier"
        );

    [Fact]
    public void Ternary_VisitsExpressions() =>
        AssertVisitOrder(
            "1 ? a : b()",
            "ExpressionStatement",
            "TernaryOperator",
            "Literal",
            "Identifier",
            "Invocation",
            "Identifier",
            "Arguments"
        );

    [Fact]
    public void Return_VisitsExpression() =>
        AssertVisitOrder(
            "fn f() -> 1",
            "FunctionDeclaration",
            "Parameters",
            "ExpressionBody",
            "Literal"
        );

    [Fact]
    public void ReturnInsideFunction_VisitsExpression() =>
        AssertVisitOrder(
            "fn f() { return 1 }",
            "FunctionDeclaration",
            "Parameters",
            "Block",
            "Return",
            "Literal"
        );
    
    [Fact]
    public void ReturnInsideFunction_NoExpression() =>
        AssertVisitOrder(
            "fn f() { return }",
            "FunctionDeclaration",
            "Parameters",
            "Block",
            "Return"
        );

    [Fact]
    public void Block_VisitsStatements() =>
        AssertVisitOrder(
            "{ 1; 2; }",
            "Block",
            "ExpressionStatement",
            "Literal",
            "ExpressionStatement",
            "Literal"
        );
    
    [Fact]
    public void Break() => AssertVisitOrder("break", "Break");
    
    [Fact]
    public void Continue() => AssertVisitOrder("continue", "Continue");
    
    [Fact]
    public void For_VisitsChildren() =>
        AssertVisitOrder(
            "for x : 1..10 { 1 }",
            "For",
            "Identifier",
            "RangeLiteral",
            "Literal",
            "Literal",
            "Block",
            "ExpressionStatement",
            "Literal"
        );
    
    [Fact]
    public void After_VisitsChildren() =>
        AssertVisitOrder(
            "after 5s { 1 }",
            "After",
            "Literal",
            "Block",
            "ExpressionStatement",
            "Literal"
        );
    
    [Fact]
    public void While_VisitsChildren() =>
        AssertVisitOrder(
            "while true { 1 }",
            "While",
            "Literal",
            "Block",
            "ExpressionStatement",
            "Literal"
        );

    [Fact]
    public void If_VisitsConditionThenElse() =>
        AssertVisitOrder(
            "if true { 1 } else { 2 }",
            "If",
            "Literal",
            "Block",
            "ExpressionStatement",
            "Literal",
            "ElseBranch",
            "Block",
            "ExpressionStatement",
            "Literal"
        );

    [Fact]
    public void FunctionDeclaration_VisitsAllParts() =>
        AssertVisitOrder(
            "fn f<T>(x: number): void { }",
            "FunctionDeclaration",
            "TypeParameters",
            "TypeParameter",
            "Parameters",
            "Parameter",
            "ColonTypeClause",
            "PrimitiveType",
            "ColonTypeClause",
            "PrimitiveType",
            "Block"
        );

    [Fact]
    public void VariableDeclaration_VisitsColonAndInit() =>
        AssertVisitOrder(
            "let x: number = 1;",
            "VariableDeclaration",
            "ColonTypeClause",
            "PrimitiveType",
            "EqualsValueClause",
            "Literal"
        );

    [Fact]
    public void DeclareVariable_VisitsColonType() =>
        AssertVisitOrder(
            "declare let x: number;",
            "Declare",
            "DeclareVariableSignature",
            "ColonTypeClause",
            "PrimitiveType"
        );

    [Fact]
    public void DeclareFunction_VisitsReturnTypeAndParams() =>
        AssertVisitOrder(
            "declare fn foo(x: number): void;",
            "Declare",
            "DeclareFunctionSignature",
            "Parameters",
            "Parameter",
            "ColonTypeClause",
            "PrimitiveType",
            "ColonTypeClause",
            "PrimitiveType"
        );

    [Fact]
    public void TypeAlias_VisitsTypeAndTypeParams() =>
        AssertVisitOrder(
            "type A<T> = T;",
            "TypeAlias",
            "TypeParameters",
            "TypeParameter",
            "EqualsTypeClause",
            "TypeName"
        );

    [Fact]
    public void TypeName_VisitsGenericArguments() =>
        AssertVisitOrder(
            "type X = A<T>",
            "TypeAlias",
            "EqualsTypeClause",
            "TypeName",
            "TypeArguments",
            "TypeName"
        );

    [Fact]
    public void EnumDeclaration_VisitsMembers() =>
        AssertVisitOrder(
            "enum E { A, B }",
            "EnumDeclaration",
            "EnumMember",
            "EnumMember"
        );

    [Fact]
    public void BinaryOperator_VisitsLeftRight() =>
        AssertVisitOrder(
            "1 + 2",
            "ExpressionStatement",
            "BinaryOperator",
            "Literal",
            "Literal"
        );

    [Fact]
    public void UnaryOperator_VisitsOperand() =>
        AssertVisitOrder(
            "!true",
            "ExpressionStatement",
            "UnaryOperator",
            "Literal"
        );

    [Fact]
    public void AssignmentOperator_VisitsLeftRight() =>
        AssertVisitOrder(
            "x = 1",
            "ExpressionStatement",
            "AssignmentOperator",
            "Identifier",
            "Literal"
        );

    [Fact]
    public void Invocation_VisitsExpressionAndArguments() =>
        AssertVisitOrder(
            "foo(1, 2)",
            "ExpressionStatement",
            "Invocation",
            "Identifier",
            "Arguments",
            "Literal",
            "Literal"
        );

    [Fact]
    public void QualifiedName_VisitsIdentifier() =>
        AssertVisitOrder(
            "a.b",
            "ExpressionStatement",
            "QualifiedName",
            "Identifier"
        );

    [Fact]
    public void ElementAccess_VisitsExpressionAndIndex() =>
        AssertVisitOrder(
            "a[0]",
            "ExpressionStatement",
            "ElementAccess",
            "Identifier",
            "Literal"
        );

    [Fact]
    public void RangeLiteral_VisitsMinMax() =>
        AssertVisitOrder(
            "1..5",
            "ExpressionStatement",
            "RangeLiteral",
            "Literal",
            "Literal"
        );

    [Fact]
    public void ArrayLiteral_VisitsExpressions() =>
        AssertVisitOrder(
            "[1, 2]",
            "ExpressionStatement",
            "ArrayLiteral",
            "Literal",
            "Literal"
        );

    [Fact]
    public void InterpolatedStringLiteral_VisitsHoleExpressions() =>
        AssertVisitOrder(
            """$"a {1} b {2}";""",
            "ExpressionStatement",
            "InterpolatedStringLiteral",
            "Literal",
            "Literal"
        );

    [Fact]
    public void Parenthesized_VisitsInner() =>
        AssertVisitOrder(
            "(1)",
            "ExpressionStatement",
            "Parenthesized",
            "Literal"
        );
    
    [Fact]
    public void NullExpression() =>
        AssertVisitOrder(
            "=",
            "ExpressionStatement",
            "NullExpression"
        );
    
    [Fact]
    public void NullTypeExpression() =>
        AssertVisitOrder(
            "type X = fn(a = 69): void;",
            "TypeAlias",
            "EqualsTypeClause",
            "NullTypeExpression"
        );
    
    [Fact]
    public void NullStatement() =>
        AssertVisitOrder(
            "if x let y = 1",
            "If",
            "Identifier",
            "NullStatement"
        );

    [Fact]
    public void NameOf_VisitsName() =>
        AssertVisitOrder(
            "nameof(x)",
            "ExpressionStatement",
            "NameOf",
            "Identifier"
        );

    [Fact]
    public void As_VisitsExpressionAndType() =>
        AssertVisitOrder(
            "x as number",
            "ExpressionStatement",
            "As",
            "Identifier",
            "PrimitiveType"
        );

    [Fact]
    public void ArrayType_VisitsElementType() =>
        AssertVisitOrder(
            "let x: number[];",
            "VariableDeclaration",
            "ColonTypeClause",
            "ArrayType",
            "PrimitiveType"
        );

    [Fact]
    public void OptionalType_VisitsNonNullable() =>
        AssertVisitOrder(
            "let x: number?;",
            "VariableDeclaration",
            "ColonTypeClause",
            "OptionalType",
            "PrimitiveType"
        );

    [Fact]
    public void UnionType_VisitsTypes() =>
        AssertVisitOrder(
            "let x: number | string;",
            "VariableDeclaration",
            "ColonTypeClause",
            "UnionType",
            "PrimitiveType",
            "PrimitiveType"
        );

    [Fact]
    public void IntersectionType_VisitsTypes() =>
        AssertVisitOrder(
            "let x: number & string;",
            "VariableDeclaration",
            "ColonTypeClause",
            "IntersectionType",
            "PrimitiveType",
            "PrimitiveType"
        );
    
    [Fact]
    public void IndexedType_VisitsTypes() =>
        AssertVisitOrder(
            "let x: Foo[number];",
            "VariableDeclaration",
            "ColonTypeClause",
            "IndexedType",
            "TypeName",
            "PrimitiveType"
        );

    [Fact]
    public void FunctionType_VisitsTypes() =>
        AssertVisitOrder(
            "let x: fn<T>(x: number, y: string): bool;",
            "VariableDeclaration",
            "ColonTypeClause",
            "FunctionType",
            "TypeParameters",
            "TypeParameter",
            "Parameters",
            "Parameter",
            "ColonTypeClause",
            "PrimitiveType",
            "Parameter",
            "ColonTypeClause",
            "PrimitiveType",
            "ColonTypeClause",
            "PrimitiveType"
        );

    [Fact]
    public void TypeArguments_VisitsTypeList() =>
        AssertVisitOrder(
            "fn id<T>(v: T) {} id::<number>();",
            "FunctionDeclaration",
            "TypeParameters",
            "TypeParameter",
            "Parameters",
            "Parameter",
            "ColonTypeClause",
            "TypeName",
            "Block",
            "ExpressionStatement",
            "Invocation",
            "Identifier",
            "TypeArguments",
            "PrimitiveType",
            "Arguments"
        );
    
    [Fact]
    public void LiteralType_VisitsLiteral() =>
        AssertVisitOrder(
            "let x: 42;",
            "VariableDeclaration",
            "ColonTypeClause",
            "LiteralType"
        );

    [Fact]
    public void ParenthesizedType_VisitsInnerType() =>
        AssertVisitOrder(
            "let x: (number);",
            "VariableDeclaration",
            "ColonTypeClause",
            "ParenthesizedType",
            "PrimitiveType"
        );

    [Fact]
    public void PropertyAccess_VisitsExpression() =>
        AssertVisitOrder(
            "\"hello\".length",
            "ExpressionStatement",
            "PropertyAccess",
            "Literal"
        );
}