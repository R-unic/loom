using Loom.Core.Debug;
using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Text;
using PrimitiveTypeKind = Loom.Core.TypeChecking.Types.PrimitiveTypeKind;
using Loom.Testing;

namespace Loom.Testing.Parsing;

[Collection("Assembly")]
public partial class ParserTest
{
    public static readonly IEnumerable<TheoryDataRow<string, string, string, string?>> ErrorTestCases =
    [
        new("let", InternalCodes.UnexpectedEof, "Expected identifier, got EOF.", null),
        new("let x:", InternalCodes.UnexpectedEof, "Expected type, got EOF.", null),
        new("!", InternalCodes.UnexpectedEof, "Unexpected end of file.", null),
        new("if )", InternalCodes.UnexpectedToken, "Expected expression, got ')'.", null),
        new("nameof(123)", InternalCodes.InvalidNameOf, "'123' is not a valid name.", null),
        new("(1 + 2", InternalCodes.UnexpectedEof, "Expected ')' here to close '(' at character 0, got EOF.", null),
        new("(1 + 2]", InternalCodes.UnexpectedToken, "Expected ')' here to close '(' at character 0, got ']'.", null),
        new("[1, 2", InternalCodes.UnexpectedEof, "Expected ']', got EOF.", null),
        new("interface I { [number }", InternalCodes.UnexpectedToken, "Expected ']', got '}'.", null),
        new("arr[0", InternalCodes.UnexpectedEof, "Expected ']', got EOF.", null),
        new("1 = 1", InternalCodes.InvalidAssignmentTarget, "Invalid assignment target.", null),
        new("a ? b : c = d", InternalCodes.InvalidAssignmentTarget, "Invalid assignment target.", null),
        new("fn foo", InternalCodes.MissingFunctionBody, "Expected function body, got EOF.", null),
        new("{", InternalCodes.UnexpectedEof, "Expected '}', got EOF.", null),
        new("{{", InternalCodes.UnexpectedEof, "Expected '}', got EOF.", null),
        new("fn add() {", InternalCodes.UnexpectedEof, "Expected '}', got EOF.", null),
        new("fn add(a, b) {", InternalCodes.UnexpectedEof, "Expected '}', got EOF.", null),
        new("fn f() { if true {", InternalCodes.UnexpectedEof, "Expected '}', got EOF.", null),
        new("if true {", InternalCodes.UnexpectedEof, "Expected '}', got EOF.", null),
        new("while true {", InternalCodes.UnexpectedEof, "Expected '}', got EOF.", null),
        new("for x : items {", InternalCodes.UnexpectedEof, "Expected '}', got EOF.", null),
        new("fn f() { } fn g() {", InternalCodes.UnexpectedEof, "Expected '}', got EOF.", null),
        new("if true let x = 42", InternalCodes.DeclarationOutsideOfBlock, "Declarations can only be declared inside of a block.", "surround with '{' and '}'"),
        new(
            "if true { return 1 } else let x = 42",
            InternalCodes.DeclarationOutsideOfBlock,
            "Declarations can only be declared inside of a block.",
            "surround with '{' and '}'"
        ),
        new("while true let x = 1", InternalCodes.DeclarationOutsideOfBlock, "Declarations can only be declared inside of a block.", "surround with '{' and '}'"),
        new("after 10ms let x = 1", InternalCodes.DeclarationOutsideOfBlock, "Declarations can only be declared inside of a block.", null),
        new("declare fn foo(a: number)", InternalCodes.MissingDeclareFnReturnType, "Declared function signatures must have a return type.", null),
        new(
            "declare fn foo(a: number = 5): void",
            InternalCodes.UseOfDeclareFnParameterDefaults,
            "Parameters may not have default values in declared function signatures.",
            null
        ),
        new("declare fn foo(a): void", InternalCodes.MissingDeclareFnParameterType, "Parameters must have types in declared function signatures.", null),
        new("declare 123", InternalCodes.ExpectedDeclarationSignature, "Expected declaration signature, got '123'.", null),
        new(
            "export 123",
            InternalCodes.ExpectedExportableDeclaration,
            "Only 'fn', 'let', 'type', 'interface', 'enum', 'trait', and 'event' declarations can be exported, got '123'.",
            null
        ),
        new(
            "internal 123",
            InternalCodes.ExpectedExportableDeclaration,
            "Only 'fn', 'let', 'type', 'interface', 'enum', 'trait', and 'event' declarations can be marked internal, got '123'.",
            null
        ),
        new("export *", InternalCodes.UnexpectedEof, "Expected 'from', got EOF.", null),
        new("export * from math", InternalCodes.UnexpectedToken, "Expected module path, got 'math'.", null),
        new("export type * from", InternalCodes.UnexpectedEof, "Expected module path, got EOF.", null),
        new(
            "let v: math.Scalar = 1;",
            InternalCodes.NotImplemented,
            "Qualified type names are not supported yet.",
            "import the type by name with 'import type { ... }'"
        ),
        new("import from \"./math\"", InternalCodes.UnexpectedToken, "Expected '{' after 'import', got 'from'.", null),
        new("import { } from \"./math\"", InternalCodes.EmptyImportClause, "Import declaration must name at least one member.", null),
        new("import type { } from \"./math\"", InternalCodes.EmptyImportClause, "Import declaration must name at least one member.", null),
        new("import { square } \"./math\"", InternalCodes.UnexpectedToken, "Expected 'from', got '\"./math\"'.", null),
        new("import { square } form \"./math\"", InternalCodes.UnexpectedToken, "Expected 'from', got 'form'.", null),
        new("import { square } from math", InternalCodes.UnexpectedToken, "Expected module path, got 'math'.", null),
        new("import { square as } from \"./math\"", InternalCodes.UnexpectedToken, "Expected import alias, got '}'.", null),
        new("import { square }", InternalCodes.UnexpectedEof, "Expected 'from', got EOF.", null),
        new("import { square } from", InternalCodes.UnexpectedEof, "Expected module path, got EOF.", null),
        new("export { }", InternalCodes.EmptyExportList, "Export list must name at least one member.", null),
        new("export type { }", InternalCodes.EmptyExportList, "Export list must name at least one member.", null),
        new("export { pi as } from \"./math\"", InternalCodes.UnexpectedToken, "Expected export alias, got '}'.", null),
        new("export { pi } from", InternalCodes.UnexpectedEof, "Expected module path, got EOF.", null),
        new("type Fn = fn(number)", InternalCodes.MissingDeclareFnReturnType, "Function types must have a return type.", null),
        new("type Fn = fn(x: number = 5): number", InternalCodes.UseOfDeclareFnParameterDefaults, "Parameters may not have default values in function types.", null),
        new("type Fn = fn(x): number", InternalCodes.MissingDeclareFnParameterType, "Parameters must have types in function types.", null),
        new("type F = (fn())", InternalCodes.MissingDeclareFnReturnType, "Function types must have a return type.", null),
        new("fn foo(..a: number[], b: number) { }", InternalCodes.RestParameterNotLast, "A rest parameter must be the last parameter.", null),
        new("fn foo(..a) { }", InternalCodes.MissingRestParameterType, "A rest parameter must have an explicit array type.", null),
        new("fn foo(..a: number[] = [1, 2]) { }", InternalCodes.RestParameterHasDefaultValue, "A rest parameter may not have a default value.", null),
        new("interface I { name }", InternalCodes.ExpectedInterfaceMemberType, "Expected indexer type, got '}'.", null),
        new("interface I { [number] }", InternalCodes.UnexpectedToken, "Expected property name, got '}'.", null),
        new("interface { }", InternalCodes.UnexpectedToken, "Expected interface name, got '{'.", null),
        new("interface I { 123 }", InternalCodes.UnexpectedToken, "Expected property name, got '123'.", null),
        new("after { }", InternalCodes.UnexpectedToken, "Expected expression, got '{'.", null),
        new("after 5s", InternalCodes.UnexpectedEof, "Unexpected end of file.", null),
        new("every { }", InternalCodes.UnexpectedToken, "Expected expression, got '{'.", null),
        new("every 5s", InternalCodes.UnexpectedEof, "Unexpected end of file.", null),
        new("every 10ms let x = 1", InternalCodes.DeclarationOutsideOfBlock, "Declarations can only be declared inside of a block.", null),
        new("every 500ms while { }", InternalCodes.UnexpectedToken, "Expected expression, got '{'.", null),
        new("for : items { }", InternalCodes.UnexpectedToken, "Expected identifier, got ':'.", null),
        new("for x items { }", InternalCodes.UnexpectedToken, "Expected ':', got 'items'.", null),
        new("fn a<>() { }", InternalCodes.UnexpectedToken, "Expected type parameter name, got '>'.", null),
        new("fn a<T(x: T) { }", InternalCodes.UnexpectedToken, "Expected '>', got '('.", null),
        new("let x: List<number, string", InternalCodes.UnexpectedEof, "Expected '>', got EOF.", null),
        new("let x: List<number", InternalCodes.UnexpectedEof, "Expected '>', got EOF.", null),
        new("let x: List<>", InternalCodes.UnexpectedToken, "Expected type, got '>'.", null),
        new("let x: List<number,>", InternalCodes.UnexpectedToken, "Expected type, got '>'.", null),
        new("let x: List<number>>", InternalCodes.UnexpectedToken, "Expected expression, got '>'.", null),
        new("let x: number |", InternalCodes.UnexpectedEof, "Expected type, got EOF.", null),
        new("let x: number &", InternalCodes.UnexpectedEof, "Expected type, got EOF.", null),
        new("let x: (number", InternalCodes.UnexpectedEof, "Expected ')' here to close '(' at character 7, got EOF.", null),
        new("let x: keyof(T | number)", InternalCodes.UnexpectedToken, "Expected ')', got '|'.", null),
        new("let x: typeof", InternalCodes.UnexpectedEof, "Expected '(', got EOF.", null),
        new("let x: typeof(a", InternalCodes.UnexpectedEof, "Expected ')', got EOF.", null),
        new("let x: typeof()", InternalCodes.UnexpectedToken, "Expected expression, got ')'.", null),
        new("trait { }", InternalCodes.UnexpectedToken, "Expected trait name, got '{'.", null),
        new("trait Foo { fn bar() }", InternalCodes.MissingDeclareFnReturnType, "Trait members must have a return type.", null),
        new("trait Foo { fn bar(x): void }", InternalCodes.MissingDeclareFnParameterType, "Parameters must have types in trait members.", null),
        new(
            "trait Foo { fn bar(x: number = 5): void }",
            InternalCodes.UseOfDeclareFnParameterDefaults,
            "Parameters may not have default values in trait members.",
            null
        ),
        new("implement", InternalCodes.UnexpectedEof, "Expected trait name, got EOF.", null),
        new("implement Foo", InternalCodes.UnexpectedEof, "Expected 'for', got EOF.", null),
        new("implement Foo for", InternalCodes.UnexpectedEof, "Expected interface name, got EOF.", null),
        new("implement Foo for Bar", InternalCodes.UnexpectedEof, "Expected '{', got EOF.", null),
        new("implement Foo for Bar {", InternalCodes.UnexpectedEof, "Expected '}', got EOF.", null),
        new("implement Foo for Bar { fn }", InternalCodes.UnexpectedToken, "Expected function name, got '}'.", null),
        new("implement 123 for Bar { }", InternalCodes.UnexpectedToken, "Expected trait name, got '123'.", null),
        new("implement Foo for Bar<T> { }", InternalCodes.UnexpectedToken, "Expected '{', got '<'.", null),
        new("implement Foo 123 Bar { }", InternalCodes.UnexpectedToken, "Expected 'for', got '123'.", null),
        new("implement Foo for 123 { }", InternalCodes.UnexpectedToken, "Expected interface name, got '123'.", null),
        new("static", InternalCodes.UnexpectedEof, "Expected interface name, got EOF.", null),
        new("static Foo", InternalCodes.UnexpectedEof, "Expected '{', got EOF.", null),
        new("static Foo {", InternalCodes.UnexpectedEof, "Expected '}', got EOF.", null),
        new("static Foo { x", InternalCodes.MustHaveInitializer, "Static field 'x' must have an initializer, got EOF.", null),
        new("static 123 { }", InternalCodes.UnexpectedToken, "Expected interface name, got '123'.", null),
        new("a::", InternalCodes.UnexpectedEof, "Expected identifier, got EOF.", null),
        new("nameof::<number>()", InternalCodes.InvalidTypeArguments, "May only get name of type when the type is a type name.", null),
        new("nameof::<T>(1)", InternalCodes.UnexpectedToken, "Expected ')', got '1'.", null),
        new("nameof::<T, U>()", InternalCodes.GenericArity, "Exactly one type parameter is allowed for 'nameof::<T>()'.", null),
        new("match", InternalCodes.UnexpectedEof, "Unexpected end of file.", null),
        new("match x", InternalCodes.UnexpectedEof, "Expected '{', got EOF.", null),
        new("match x {", InternalCodes.UnexpectedEof, "Expected '}', got EOF.", null),
        new("match x { _ ", InternalCodes.UnexpectedEof, "Expected '->', got EOF.", null),
        new("match x { _ -> }", InternalCodes.UnexpectedToken, "Expected expression, got '}'.", null),
        new("match x { -> 1 }", InternalCodes.UnexpectedToken, "Expected pattern, got '->'.", null),
        new("match x { { 123: y } -> 1 }", InternalCodes.UnexpectedToken, "Expected property name, got '123'.", null),
        new("match x { 1 | -> 2 }", InternalCodes.UnexpectedToken, "Expected pattern, got '->'.", null),
        new("match x { 0.. -> 1 }", InternalCodes.UnexpectedToken, "Expected range end, got '->'.", null),
        new("match x { let -> 1 }", InternalCodes.UnexpectedToken, "Expected binding name, got '->'.", null),
        new("match x { name when -> 1 }", InternalCodes.UnexpectedToken, "Expected type, got '->'.", null),
        new("match x { _ when -> 1 }", InternalCodes.UnexpectedToken, "Expected expression, got '->'.", null),
        new("match x { [a, ..rest, b] -> 1 }", InternalCodes.UnexpectedToken, "Rest pattern must be the last element in an array pattern.", null),
        new("match x { [a, ..b, ..c] -> 1 }", InternalCodes.UnexpectedToken, "Rest pattern must appear at most once in an array pattern.", null),
        new("let x = mut 5", InternalCodes.UnexpectedToken, "Expected array literal after 'mut'.", null)
    ];

    public static readonly IEnumerable<TheoryDataRow<string, string>> SnapshotFiles = Utility.GetSnapshotFiles("AST", ".ast");

    [Theory]
    [MemberData(nameof(ErrorTestCases))]
    public void Parser_ErrorDiagnostics(string source, string code, string expectedMessage, string? hint = null)
    {
        var diagnostics = Utility.GetParserDiagnostics(source);
        Utility.AssertDiagnostic(diagnostics, code, expectedMessage, hint);
    }

    [Fact]
    public void ParseInterfaceBody_BadMember_StillConsumesClosingBrace_NoCascadingError()
    {
        var diagnostics = Utility.GetParserDiagnostics("interface Foo { bar }");
        var diagnostic = Assert.Single(diagnostics.Set);
        Assert.Equal(InternalCodes.ExpectedInterfaceMemberType, diagnostic.Code);
    }

    [Theory]
    [MemberData(nameof(SnapshotFiles))]
    public void Parser_Snapshots(string sourcePath, string snapshotPath)
    {
        var source = File.ReadAllText(sourcePath);
        var result = Utility.Parse(source);
        Utility.AssertNoErrors(result);

        var actual = AstInspector.Inspect(result.Tree).Replace(Environment.NewLine, "\n");
        var expected = File.ReadAllText(snapshotPath).Replace(Environment.NewLine, "\n");
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Parses_MatchExpression_WildcardArm()
    {
        var tree = Utility.GetAST("match x { _ -> 0 }");
        var expressionStatement = Assert.IsType<ExpressionStatement>(Assert.Single(tree.Statements));
        var match = Assert.IsType<MatchExpression>(expressionStatement.Expression);

        Assert.Equal("x", Assert.IsType<Identifier>(match.Expression).Name.Text);
        var arm = Assert.Single(match.Arms);
        Assert.IsType<WildcardPattern>(arm.Pattern);
        Assert.Equal(0L, Assert.IsType<Literal>(arm.Body).Value);
    }

    [Fact]
    public void Parses_MatchExpression_ObjectPatternWithBindings()
    {
        var tree = Utility.GetAST("match result { { ok: true, value: v } -> v }");
        var expressionStatement = Assert.IsType<ExpressionStatement>(Assert.Single(tree.Statements));
        var match = Assert.IsType<MatchExpression>(expressionStatement.Expression);
        var arm = Assert.Single(match.Arms);
        var pattern = Assert.IsType<ObjectPattern>(arm.Pattern);

        Assert.Equal(2, pattern.Fields.Count);
        Assert.Equal("ok", pattern.Fields[0].Name.Text);
        Assert.Equal(true, Assert.IsType<LiteralPattern>(pattern.Fields[0].Pattern).Value);
        Assert.Equal("value", pattern.Fields[1].Name.Text);
        Assert.Equal("v", Assert.IsType<IdentifierPattern>(pattern.Fields[1].Pattern).Name.Text);
        Assert.Equal("v", Assert.IsType<Identifier>(arm.Body).Name.Text);
    }

    [Fact]
    public void Parses_MatchExpression_ObjectPatternShorthand()
    {
        var tree = Utility.GetAST("match value { { value } -> value }");
        var expressionStatement = Assert.IsType<ExpressionStatement>(Assert.Single(tree.Statements));
        var match = Assert.IsType<MatchExpression>(expressionStatement.Expression);
        var field = Assert.Single(Assert.IsType<ObjectPattern>(Assert.Single(match.Arms).Pattern).Fields);

        Assert.Equal("value", field.Name.Text);
        Assert.Null(field.Colon);
        Assert.Equal("value", Assert.IsType<IdentifierPattern>(field.Pattern).Name.Text);
    }

    [Fact]
    public void Parses_MatchExpression_AsVariableInitializer()
    {
        var tree = Utility.GetAST("let x = match n { 0 -> \"zero\", _ -> \"other\" }");
        var declaration = Assert.IsType<VariableDeclaration>(Assert.Single(tree.Statements));
        var match = Assert.IsType<MatchExpression>(declaration.EqualsValueClause!.Value);

        Assert.Equal(2, match.Arms.Count);
        Assert.Equal(0L, Assert.IsType<LiteralPattern>(match.Arms[0].Pattern).Value);
        Assert.IsType<WildcardPattern>(match.Arms[1].Pattern);
    }

    [Fact]
    public void Parses_MatchExpression_LiteralAndIdentifierArms()
    {
        var tree = Utility.GetAST("""match tag { "lava" -> 1, other -> 2 }""");
        var expressionStatement = Assert.IsType<ExpressionStatement>(Assert.Single(tree.Statements));
        var match = Assert.IsType<MatchExpression>(expressionStatement.Expression);

        Assert.Equal(2, match.Arms.Count);
        Assert.Equal("lava", Assert.IsType<LiteralPattern>(match.Arms[0].Pattern).Value);
        Assert.Equal("other", Assert.IsType<IdentifierPattern>(match.Arms[1].Pattern).Name.Text);
    }

    [Fact]
    public void Parses_MatchExpression_QualifiedNamePattern()
    {
        var tree = Utility.GetAST("match d { Direction.North -> 1, _ -> 0 }");
        var expressionStatement = Assert.IsType<ExpressionStatement>(Assert.Single(tree.Statements));
        var match = Assert.IsType<MatchExpression>(expressionStatement.Expression);

        Assert.Equal(2, match.Arms.Count);
        var pattern = Assert.IsType<QualifiedNamePattern>(match.Arms[0].Pattern);
        Assert.Equal("Direction", pattern.Name.Identifier.Name.Text);
        var name = Assert.Single(pattern.Name.Names);
        Assert.Equal("North", name.Name.Text);
    }

    /// <remarks>A single identifier with no dot after it still binds a name, same as ever - only a following '.' switches to a qualified reference.</remarks>
    [Fact]
    public void Parses_MatchExpression_BareIdentifierPattern_IsUnaffectedByQualifiedNameSupport()
    {
        var tree = Utility.GetAST("match d { direction -> direction }");
        var expressionStatement = Assert.IsType<ExpressionStatement>(Assert.Single(tree.Statements));
        var match = Assert.IsType<MatchExpression>(expressionStatement.Expression);

        Assert.Equal("direction", Assert.IsType<IdentifierPattern>(Assert.Single(match.Arms).Pattern).Name.Text);
    }

    [Fact]
    public void Parses_MatchExpression_OrPattern()
    {
        var tree = Utility.GetAST("match n { 2 | 3 | 4 -> true }");
        var expressionStatement = Assert.IsType<ExpressionStatement>(Assert.Single(tree.Statements));
        var arm = Assert.Single(Assert.IsType<MatchExpression>(expressionStatement.Expression).Arms);
        var pattern = Assert.IsType<OrPattern>(arm.Pattern);

        Assert.Equal(3, pattern.Patterns.Count);
        Assert.Equal(2, pattern.Pipes.Count);
        Assert.Equal(2L, Assert.IsType<LiteralPattern>(pattern.Patterns[0]).Value);
        Assert.Equal(3L, Assert.IsType<LiteralPattern>(pattern.Patterns[1]).Value);
        Assert.Equal(4L, Assert.IsType<LiteralPattern>(pattern.Patterns[2]).Value);
    }

    [Fact]
    public void Parses_MatchExpression_RangePatternWithAlternation()
    {
        var tree = Utility.GetAST("match n { 0..5 | 10..15 | 100 -> true }");
        var expressionStatement = Assert.IsType<ExpressionStatement>(Assert.Single(tree.Statements));
        var arm = Assert.Single(Assert.IsType<MatchExpression>(expressionStatement.Expression).Arms);
        var pattern = Assert.IsType<OrPattern>(arm.Pattern);

        Assert.Equal(3, pattern.Patterns.Count);
        var first = Assert.IsType<RangePattern>(pattern.Patterns[0]);
        Assert.Equal(0L, Assert.IsType<LiteralPattern>(first.Minimum).Value);
        Assert.Equal(5L, Assert.IsType<LiteralPattern>(first.Maximum).Value);
        var second = Assert.IsType<RangePattern>(pattern.Patterns[1]);
        Assert.Equal(10L, Assert.IsType<LiteralPattern>(second.Minimum).Value);
        Assert.Equal(15L, Assert.IsType<LiteralPattern>(second.Maximum).Value);
        Assert.Equal(100L, Assert.IsType<LiteralPattern>(pattern.Patterns[2]).Value);
    }

    [Fact]
    public void Parses_MatchExpression_TypePatternWithTypedBinding()
    {
        var tree = Utility.GetAST("match value { Foo { some_field: name when string } -> name }");
        var expressionStatement = Assert.IsType<ExpressionStatement>(Assert.Single(tree.Statements));
        var arm = Assert.Single(Assert.IsType<MatchExpression>(expressionStatement.Expression).Arms);
        var typePattern = Assert.IsType<TypePattern>(arm.Pattern);

        Assert.Equal("Foo", Assert.IsType<TypeName>(typePattern.Type).Name.Text);
        Assert.NotNull(typePattern.ObjectPattern);
        var field = Assert.Single(typePattern.ObjectPattern.Fields);
        Assert.Equal("some_field", field.Name.Text);
        var typed = Assert.IsType<TypedPattern>(field.Pattern);
        Assert.Equal("name", typed.Name.Text);
        Assert.Equal(PrimitiveTypeKind.String, Assert.IsType<PrimitiveType>(typed.Type).Kind);
    }

    [Fact]
    public void Parses_MatchExpression_TypedCastPattern_Primitive()
    {
        var tree = Utility.GetAST("match value { s when string -> s }");
        var expressionStatement = Assert.IsType<ExpressionStatement>(Assert.Single(tree.Statements));
        var arm = Assert.Single(Assert.IsType<MatchExpression>(expressionStatement.Expression).Arms);
        var typed = Assert.IsType<TypedPattern>(arm.Pattern);

        Assert.Equal("s", typed.Name.Text);
        Assert.Equal(PrimitiveTypeKind.String, Assert.IsType<PrimitiveType>(typed.Type).Kind);
        Assert.Null(typed.ObjectPattern);
        Assert.Null(arm.Guard);
        Assert.Equal("s", Assert.IsType<Identifier>(arm.Body).Name.Text);
    }

    [Fact]
    public void Parses_MatchExpression_TypedCastPattern_WithObjectPattern()
    {
        var tree = Utility.GetAST("match value { f when Foo { some_field: let n, other: name when string } -> n }");
        var expressionStatement = Assert.IsType<ExpressionStatement>(Assert.Single(tree.Statements));
        var arm = Assert.Single(Assert.IsType<MatchExpression>(expressionStatement.Expression).Arms);
        var typed = Assert.IsType<TypedPattern>(arm.Pattern);

        Assert.Equal("f", typed.Name.Text);
        Assert.Equal("Foo", Assert.IsType<TypeName>(typed.Type).Name.Text);
        Assert.NotNull(typed.ObjectPattern);
        Assert.Equal(2, typed.ObjectPattern.Fields.Count);

        Assert.Equal("some_field", typed.ObjectPattern.Fields[0].Name.Text);
        Assert.Equal("n", Assert.IsType<LetPattern>(typed.ObjectPattern.Fields[0].Pattern).Name.Text);

        Assert.Equal("other", typed.ObjectPattern.Fields[1].Name.Text);
        var nestedTyped = Assert.IsType<TypedPattern>(typed.ObjectPattern.Fields[1].Pattern);
        Assert.Equal("name", nestedTyped.Name.Text);
        Assert.Equal(PrimitiveTypeKind.String, Assert.IsType<PrimitiveType>(nestedTyped.Type).Kind);
    }

    [Fact]
    public void Parses_MatchExpression_NestedArrayInsideObjectInsideTypedPattern()
    {
        var tree = Utility.GetAST("match value { f when Foo { items: [first, ..rest] } -> first }");
        var expressionStatement = Assert.IsType<ExpressionStatement>(Assert.Single(tree.Statements));
        var arm = Assert.Single(Assert.IsType<MatchExpression>(expressionStatement.Expression).Arms);
        var typed = Assert.IsType<TypedPattern>(arm.Pattern);

        Assert.NotNull(typed.ObjectPattern);
        var field = Assert.Single(typed.ObjectPattern.Fields);
        Assert.Equal("items", field.Name.Text);

        var array = Assert.IsType<ArrayPattern>(field.Pattern);
        Assert.Single(array.Elements);
        Assert.Equal("first", Assert.IsType<IdentifierPattern>(array.Elements[0]).Name.Text);
        Assert.NotNull(array.Rest);
        Assert.Equal("rest", Assert.IsType<IdentifierPattern>(array.Rest.Pattern).Name.Text);
    }

    [Fact]
    public void Parses_MatchExpression_TypedCastPattern_WithGuard()
    {
        var tree = Utility.GetAST("match value { f when Foo { some_field: let n } when n > 0 -> n }");
        var expressionStatement = Assert.IsType<ExpressionStatement>(Assert.Single(tree.Statements));
        var arm = Assert.Single(Assert.IsType<MatchExpression>(expressionStatement.Expression).Arms);
        var typed = Assert.IsType<TypedPattern>(arm.Pattern);

        Assert.Equal("f", typed.Name.Text);
        Assert.Equal("Foo", Assert.IsType<TypeName>(typed.Type).Name.Text);
        Assert.NotNull(typed.ObjectPattern);
        Assert.NotNull(arm.Guard);
        var guard = Assert.IsType<BinaryOperator>(arm.Guard);
        Assert.Equal("n", Assert.IsType<Identifier>(guard.Left).Name.Text);
        Assert.Equal(0L, Assert.IsType<Literal>(guard.Right).Value);
    }

    [Fact]
    public void Parses_MatchExpression_TypePatternWithLetBinding()
    {
        var tree = Utility.GetAST("match value { Foo { some_field: let name } -> name }");
        var expressionStatement = Assert.IsType<ExpressionStatement>(Assert.Single(tree.Statements));
        var arm = Assert.Single(Assert.IsType<MatchExpression>(expressionStatement.Expression).Arms);
        var typePattern = Assert.IsType<TypePattern>(arm.Pattern);

        Assert.NotNull(typePattern.ObjectPattern);
        var field = Assert.Single(typePattern.ObjectPattern.Fields);
        var letPattern = Assert.IsType<LetPattern>(field.Pattern);
        Assert.Equal("name", letPattern.Name.Text);
    }

    [Fact]
    public void Parses_IsExpression_BareType()
    {
        var tree = Utility.GetAST("value is SomeType");
        var expressionStatement = Assert.IsType<ExpressionStatement>(Assert.Single(tree.Statements));
        var isExpression = Assert.IsType<Is>(expressionStatement.Expression);

        Assert.Equal("value", Assert.IsType<Identifier>(isExpression.Expression).Name.Text);
        var typePattern = Assert.IsType<TypePattern>(isExpression.Pattern);
        Assert.Equal("SomeType", Assert.IsType<TypeName>(typePattern.Type).Name.Text);
        Assert.Null(typePattern.ObjectPattern);
    }

    [Fact]
    public void Parses_IsExpression_PrimitiveType()
    {
        var tree = Utility.GetAST("value is number");
        var expressionStatement = Assert.IsType<ExpressionStatement>(Assert.Single(tree.Statements));
        var isExpression = Assert.IsType<Is>(expressionStatement.Expression);

        var typePattern = Assert.IsType<TypePattern>(isExpression.Pattern);
        Assert.Equal(PrimitiveTypeKind.Number, Assert.IsType<PrimitiveType>(typePattern.Type).Kind);
        Assert.Null(typePattern.ObjectPattern);
    }

    [Fact]
    public void Parses_IsExpression_WithObjectPattern()
    {
        var tree = Utility.GetAST("value is SomeType { some_field: 1..10 }");
        var expressionStatement = Assert.IsType<ExpressionStatement>(Assert.Single(tree.Statements));
        var isExpression = Assert.IsType<Is>(expressionStatement.Expression);

        var typePattern = Assert.IsType<TypePattern>(isExpression.Pattern);
        Assert.Equal("SomeType", Assert.IsType<TypeName>(typePattern.Type).Name.Text);
        Assert.NotNull(typePattern.ObjectPattern);
        var field = Assert.Single(typePattern.ObjectPattern.Fields);
        Assert.Equal("some_field", field.Name.Text);
        var range = Assert.IsType<RangePattern>(field.Pattern);
        Assert.Equal(1L, Assert.IsType<LiteralPattern>(range.Minimum).Value);
        Assert.Equal(10L, Assert.IsType<LiteralPattern>(range.Maximum).Value);
    }

    [Fact]
    public void Parses_IsExpression_ObjectPatternBinding()
    {
        var tree = Utility.GetAST("value is SomeType { some_field: x }");
        var expressionStatement = Assert.IsType<ExpressionStatement>(Assert.Single(tree.Statements));
        var isExpression = Assert.IsType<Is>(expressionStatement.Expression);

        var typePattern = Assert.IsType<TypePattern>(isExpression.Pattern);
        var field = Assert.Single(typePattern.ObjectPattern!.Fields);
        Assert.Equal("x", Assert.IsType<IdentifierPattern>(field.Pattern).Name.Text);
    }

    [Fact]
    public void Parses_IsExpression_BareType_InsideIfCondition_LeavesBodyBraceForThenBranch()
    {
        var tree = Utility.GetAST("if value is SomeType { print(value) }");
        var @if = Assert.IsType<If>(Assert.Single(tree.Statements));
        var isExpression = Assert.IsType<Is>(@if.Condition);

        Assert.Null(Assert.IsType<TypePattern>(isExpression.Pattern).ObjectPattern);
        var block = Assert.IsType<Block>(@if.ThenBranch);
        Assert.Single(block.Statements);
    }

    [Fact]
    public void Parses_IsExpression_WithObjectPattern_InsideIfCondition_LeavesBodyBraceForThenBranch()
    {
        var tree = Utility.GetAST("if value is SomeType { some_field: 1..10 } { print(value.some_field) }");
        var @if = Assert.IsType<If>(Assert.Single(tree.Statements));
        var isExpression = Assert.IsType<Is>(@if.Condition);

        Assert.NotNull(Assert.IsType<TypePattern>(isExpression.Pattern).ObjectPattern);
        var block = Assert.IsType<Block>(@if.ThenBranch);
        Assert.Single(block.Statements);
    }

    [Fact]
    public void Parses_IsNotExpression_BareType()
    {
        var tree = Utility.GetAST("value is not SomeType");
        var expressionStatement = Assert.IsType<ExpressionStatement>(Assert.Single(tree.Statements));
        var isExpression = Assert.IsType<Is>(expressionStatement.Expression);

        var notPattern = Assert.IsType<NotPattern>(isExpression.Pattern);
        var typePattern = Assert.IsType<TypePattern>(notPattern.Pattern);
        Assert.Equal("SomeType", Assert.IsType<TypeName>(typePattern.Type).Name.Text);
        Assert.Null(typePattern.ObjectPattern);
    }

    [Fact]
    public void Parses_IsNotExpression_PrimitiveType()
    {
        var tree = Utility.GetAST("value is not string");
        var expressionStatement = Assert.IsType<ExpressionStatement>(Assert.Single(tree.Statements));
        var isExpression = Assert.IsType<Is>(expressionStatement.Expression);

        var notPattern = Assert.IsType<NotPattern>(isExpression.Pattern);
        Assert.Equal(PrimitiveTypeKind.String, Assert.IsType<PrimitiveType>(Assert.IsType<TypePattern>(notPattern.Pattern).Type).Kind);
    }

    [Fact]
    public void Parses_IsNotExpression_WithObjectPattern()
    {
        var tree = Utility.GetAST("value is not SomeType { some_field: 1..10 }");
        var expressionStatement = Assert.IsType<ExpressionStatement>(Assert.Single(tree.Statements));
        var isExpression = Assert.IsType<Is>(expressionStatement.Expression);

        var notPattern = Assert.IsType<NotPattern>(isExpression.Pattern);
        var typePattern = Assert.IsType<TypePattern>(notPattern.Pattern);
        Assert.NotNull(typePattern.ObjectPattern);
        var field = Assert.Single(typePattern.ObjectPattern.Fields);
        Assert.Equal("some_field", field.Name.Text);
    }

    [Fact]
    public void Parses_MatchExpression_WhenGuard()
    {
        var tree = Utility.GetAST("match n { x when x > 0 -> x }");
        var expressionStatement = Assert.IsType<ExpressionStatement>(Assert.Single(tree.Statements));
        var arm = Assert.Single(Assert.IsType<MatchExpression>(expressionStatement.Expression).Arms);

        Assert.Equal("x", Assert.IsType<IdentifierPattern>(arm.Pattern).Name.Text);
        Assert.NotNull(arm.Guard);
        var guard = Assert.IsType<BinaryOperator>(arm.Guard);
        Assert.Equal("x", Assert.IsType<Identifier>(guard.Left).Name.Text);
        Assert.Equal(0L, Assert.IsType<Literal>(guard.Right).Value);
    }

    [Fact]
    public void Parses_MatchExpression_ArrayPattern()
    {
        var tree = Utility.GetAST("match xs { [a, b, c] -> a }");
        var expressionStatement = Assert.IsType<ExpressionStatement>(Assert.Single(tree.Statements));
        var arm = Assert.Single(Assert.IsType<MatchExpression>(expressionStatement.Expression).Arms);
        var array = Assert.IsType<ArrayPattern>(arm.Pattern);

        Assert.Null(array.Rest);
        Assert.Equal(3, array.Elements.Count);
        Assert.Equal("a", Assert.IsType<IdentifierPattern>(array.Elements[0]).Name.Text);
        Assert.Equal("b", Assert.IsType<IdentifierPattern>(array.Elements[1]).Name.Text);
        Assert.Equal("c", Assert.IsType<IdentifierPattern>(array.Elements[2]).Name.Text);
    }

    [Fact]
    public void Parses_MatchExpression_ArrayPatternWithRest()
    {
        var tree = Utility.GetAST("match xs { [head, ..rest] -> head }");
        var expressionStatement = Assert.IsType<ExpressionStatement>(Assert.Single(tree.Statements));
        var arm = Assert.Single(Assert.IsType<MatchExpression>(expressionStatement.Expression).Arms);
        var array = Assert.IsType<ArrayPattern>(arm.Pattern);

        Assert.Single(array.Elements);
        Assert.Equal("head", Assert.IsType<IdentifierPattern>(array.Elements[0]).Name.Text);
        Assert.NotNull(array.Rest);
        Assert.Equal("rest", Assert.IsType<IdentifierPattern>(array.Rest.Pattern).Name.Text);
    }

    [Fact]
    public void Parses_MatchExpression_ArrayPatternWithLiteralRest()
    {
        var result = Utility.AssertNoErrors(Utility.Parse("match xs { [a, ..0] -> a, _ -> 0 }"));
        var arm = result.Tree.Statements
            .OfType<ExpressionStatement>()
            .Select(s => Assert.IsType<MatchExpression>(s.Expression))
            .Single()
            .Arms[0];

        var array = Assert.IsType<ArrayPattern>(arm.Pattern);
        Assert.NotNull(array.Rest);
        Assert.Equal(0L, Assert.IsType<LiteralPattern>(array.Rest.Pattern).Value);
    }

    [Fact]
    public void Parses_MatchExpression_ArrayPatternWithTypedRest()
    {
        var result = Utility.AssertNoErrors(Utility.Parse("match xs { [a, ..n when number] -> a, _ -> 0 }"));
        var arm = result.Tree.Statements
            .OfType<ExpressionStatement>()
            .Select(s => Assert.IsType<MatchExpression>(s.Expression))
            .Single()
            .Arms[0];

        var array = Assert.IsType<ArrayPattern>(arm.Pattern);
        Assert.NotNull(array.Rest);
        var typedPattern = Assert.IsType<TypedPattern>(array.Rest.Pattern);
        Assert.Equal("n", typedPattern.Name.Text);
    }

    [Fact]
    public void Match_InvalidPattern_ProducesNullPattern()
    {
        var tree = Utility.GetAST("match x { -> 1 }");
        var expressionStatement = Assert.IsType<ExpressionStatement>(Assert.Single(tree.Statements));
        var match = Assert.IsType<MatchExpression>(expressionStatement.Expression);
        Assert.IsType<NullPattern>(Assert.Single(match.Arms).Pattern);
    }

    [Fact]
    public void Expect_MissingParen_PreservesFollowingDeclaration()
    {
        const string source = """
            let x = foo(1, 2
            let y = 3;
            """;

        var result = Utility.Parse(source);
        Utility.AssertDiagnostic(result.Diagnostics, InternalCodes.UnexpectedToken, "Expected ')', got 'let'.");

        Assert.Equal(2, result.Tree.Statements.Count);
        var first = Assert.IsType<VariableDeclaration>(result.Tree.Statements[0]);
        var second = Assert.IsType<VariableDeclaration>(result.Tree.Statements[1]);
        Assert.Equal("x", first.Name.Text);
        Assert.Equal("y", second.Name.Text);

        var invocation = Assert.IsType<Invocation>(first.EqualsValueClause!.Value);
        var rightParen = invocation.Arguments.RightParen;
        Assert.Equal(SyntaxKind.RParen, rightParen.Kind);
        Assert.Equal(0, rightParen.Span.Length);
        Assert.Equal(")", rightParen.Text);
    }

    [Fact]
    public void Expect_MissingParen_AtEof_InsertsZeroWidthToken()
    {
        var result = Utility.Parse("let x = foo(");
        var expectDiagnostic = result.Diagnostics.Find(d => d is { Code: InternalCodes.UnexpectedEof, Message: "Expected ')', got EOF." });
        Assert.NotNull(expectDiagnostic);

        var declaration = Assert.IsType<VariableDeclaration>(Assert.Single(result.Tree.Statements));
        var invocation = Assert.IsType<Invocation>(declaration.EqualsValueClause!.Value);
        var rightParen = invocation.Arguments.RightParen;
        Assert.Equal(SyntaxKind.RParen, rightParen.Kind);
        Assert.Equal(0, rightParen.Span.Length);
        Assert.Equal(")", rightParen.Text);
    }

    [Fact]
    public void Expect_MissingBracket_PreservesFollowingDeclaration()
    {
        const string source = """
            let x = arr[0
            let y = 1;
            """;

        var result = Utility.Parse(source);
        Utility.AssertDiagnostic(result.Diagnostics, InternalCodes.UnexpectedToken, "Expected ']', got 'let'.");

        Assert.Equal(2, result.Tree.Statements.Count);
        Assert.Equal("x", Assert.IsType<VariableDeclaration>(result.Tree.Statements[0]).Name.Text);
        Assert.Equal("y", Assert.IsType<VariableDeclaration>(result.Tree.Statements[1]).Name.Text);
    }

    [Fact]
    public void ParseBlock_UnterminatedAtEof_Terminates()
    {
        var result = Utility.Parse("fn foo() {");
        var fn = Assert.IsType<FunctionDeclaration>(Assert.Single(result.Tree.Statements));
        var block = Assert.IsType<Block>(fn.Body);
        Assert.Equal(SyntaxKind.RBrace, block.RightBrace.Kind);
        Assert.Equal(0, block.RightBrace.Span.Length);
        Assert.Equal("}", block.RightBrace.Text);
    }

    [Fact]
    public void ParseBlock_Unterminated_KeepsInnerStatements()
    {
        const string source = """
            fn foo() {
            let y = 1;
            """;

        var result = Utility.Parse(source);
        var fn = Assert.IsType<FunctionDeclaration>(Assert.Single(result.Tree.Statements));
        var block = Assert.IsType<Block>(fn.Body);
        var declaration = Assert.IsType<VariableDeclaration>(Assert.Single(block.Statements));
        Assert.Equal("y", declaration.Name.Text);
        Assert.Equal(0, block.RightBrace.Span.Length);
    }

    [Fact]
    public void ParseBlock_UnterminatedIf_Terminates()
    {
        var result = Utility.Parse("if true {");
        var @if = Assert.IsType<If>(Assert.Single(result.Tree.Statements));
        var block = Assert.IsType<Block>(@if.ThenBranch);
        Assert.Equal(0, block.RightBrace.Span.Length);
    }

    [Fact]
    public void Synchronize_JunkInsideBlock_PreservesFollowingDeclaration()
    {
        const string source = """
            fn f() {
            +
            let y = 1;
            }
            """;

        var result = Utility.Parse(source);
        Utility.AssertDiagnostic(result.Diagnostics, InternalCodes.UnexpectedToken, "Expected expression, got '+'.");

        var fn = Assert.IsType<FunctionDeclaration>(Assert.Single(result.Tree.Statements));
        var block = Assert.IsType<Block>(fn.Body);
        Assert.Equal(2, block.Statements.Count);
        Assert.IsType<NullExpression>(Assert.IsType<ExpressionStatement>(block.Statements[0]).Expression);
        Assert.Equal("y", Assert.IsType<VariableDeclaration>(block.Statements[1]).Name.Text);
        Assert.Equal(1, block.RightBrace.Span.Length);
    }

    [Fact]
    public void Synchronize_JunkBeforeClosingBrace_DoesNotConsumeBrace()
    {
        const string source = """
            fn f() {
            +
            }
            """;

        var result = Utility.Parse(source);
        var fn = Assert.IsType<FunctionDeclaration>(Assert.Single(result.Tree.Statements));
        var block = Assert.IsType<Block>(fn.Body);
        Assert.Equal(SyntaxKind.RBrace, block.RightBrace.Kind);
        Assert.Equal(1, block.RightBrace.Span.Length);
        Assert.Equal("}", block.RightBrace.Text);
    }

    [Fact]
    public void Synchronize_TopLevelJunk_PreservesFollowingDeclaration()
    {
        const string source = """
            +
            let y = 1;
            """;

        var result = Utility.Parse(source);
        Assert.Equal(2, result.Tree.Statements.Count);
        Assert.IsType<NullExpression>(Assert.IsType<ExpressionStatement>(result.Tree.Statements[0]).Expression);
        Assert.Equal("y", Assert.IsType<VariableDeclaration>(result.Tree.Statements[1]).Name.Text);
    }

    [Fact]
    public void Unfinished_ProducesNull()
    {
        var tree = Utility.GetAST("let");
        Assert.Single(tree.Statements);
        var declaration = Assert.IsType<VariableDeclaration>(tree.Statements.First());
        Assert.Null(declaration.ColonTypeClause);
        Assert.Null(declaration.EqualsValueClause);
    }

    public static readonly IEnumerable<TheoryDataRow<string>> IncompleteInputCases =
        TypedPrefixes(
                "fn add(a, b) -> a + b;",
                "fn add(a: number, b: number) { return a + b; }"
            )
            .Concat(
            [
                // Unclosed / nested blocks and control flow
                "{",
                "{{",
                "fn add() {",
                "fn add(a, b) {",
                "fn add() { return",
                "fn f() { if true {",
                "fn f() { } fn g() {",
                "if true {",
                "while true {",
                "for x : items {",
                "after 5 {",
                "every 5 {",
                // Other unfinished top-level forms
                "let x =",
                "mut x =",
                "type T =",
                "enum E {",
                "interface I {",
                "trait T {",
                "declare fn",
                "implement Foo for Bar {",
                "}{",
                "fn f() { } }",
            ]
            )
            .Distinct()
            .Select(source => new TheoryDataRow<string>(source));

    private static IEnumerable<string> TypedPrefixes(params string[] sources) =>
        sources.SelectMany(source => Enumerable.Range(1, source.Length).Select(length => source[..length]));

    [Theory]
    [MemberData(nameof(IncompleteInputCases))]
    public void IncompleteInput_Terminates(string source)
    {
        var result = Utility.Parse(source);
        Assert.NotNull(result.Tree);
        Assert.NotEmpty(result.Tree.Statements);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{{")]
    [InlineData("fn add() {")]
    [InlineData("fn add(a, b) {")]
    [InlineData("fn f() { if true {")]
    [InlineData("if true {")]
    [InlineData("while true {")]
    [InlineData("for x : items {")]
    public void UnclosedBlock_ReportsMissingBrace(string source)
    {
        var result = Utility.Parse(source);
        Utility.AssertDiagnostic(result.Diagnostics, InternalCodes.UnexpectedEof, "Expected '}', got EOF.");
    }

    [Fact]
    public void UnclosedTopLevelBlock_StillProducesBlock()
    {
        var tree = Utility.GetAST("{");
        Assert.Single(tree.Statements);
        Assert.IsType<Block>(tree.Statements.First());
    }

    [Fact]
    public void UnclosedFunctionBlock_StillProducesFunctionDeclaration()
    {
        var tree = Utility.GetAST("fn add(a, b) {");
        Assert.Single(tree.Statements);
        var function = Assert.IsType<FunctionDeclaration>(tree.Statements.First());
        Assert.IsType<Block>(function.Body);
    }

    [Fact]
    public void Error_ProducesNullExpression()
    {
        var tree = Utility.GetAST("=");
        Assert.Single(tree.Statements);

        var expressionStatement = Assert.IsType<ExpressionStatement>(tree.Statements.First());
        Assert.IsType<NullExpression>(expressionStatement.Expression);
    }

    [Fact]
    public void Error_ProducesNullTypeExpression()
    {
        var tree = Utility.GetAST("type X = fn(a = 69): void");
        Assert.Single(tree.Statements);

        var alias = Assert.IsType<TypeAlias>(tree.Statements.First());
        Assert.IsType<NullTypeExpression>(alias.EqualsTypeClause.Type);
    }

    [Fact]
    public void Error_ProducesNullStatement()
    {
        var tree = Utility.GetAST("if x let y = 1");
        Assert.Single(tree.Statements);

        var @if = Assert.IsType<If>(tree.Statements.First());
        Assert.IsType<NullStatement>(@if.ThenBranch);
    }

    [Theory]
    [InlineData("123", 123L)]
    [InlineData("1e3", 100L)]
    [InlineData("0x100", 256L)]
    [InlineData("0xFF_FF", 65535L)]
    [InlineData("0Xf0D", 3853L)]
    [InlineData("0b011100110", 230L)]
    [InlineData("0b01110_0110", 230L)]
    [InlineData("0B11001", 25L)]
    [InlineData("0o400", 256L)]
    [InlineData("0O2340", 1248L)]
    [InlineData("1.23e3", 1230L)]
    [InlineData("420.69", 420.69d)]
    [InlineData("1.2345e3", 1234.5)]
    [InlineData("1_0_0________.6_9e5_1", 1.0069e53)]
    [InlineData("5s", 5L)]
    [InlineData("500ms", 0.5)]
    [InlineData("20hz", 0.05)]
    [InlineData("2_0.4_5ms", 0.02045)]
    [InlineData("0.5m", 30L)]
    [InlineData("20m", 1800L)]
    [InlineData("2h", 7200L)]
    [InlineData("'hello'", "hello")]
    [InlineData("\"abc\"", "abc")]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("none", null)]
    public void Parses_Literals(string source, object? expectedValue)
    {
        var tree = Utility.GetAST(source);
        Assert.Single(tree.Statements);

        var statement = tree.Statements.First();
        var expressionStatement = Assert.IsType<ExpressionStatement>(statement);
        var literal = Assert.IsType<Literal>(expressionStatement.Expression);
        Assert.Equal(source, literal.Token.Text);
        if (expectedValue != null)
            Assert.IsType(expectedValue.GetType(), literal.Value);
        else
            Assert.Null(literal.Value);
    }







}
