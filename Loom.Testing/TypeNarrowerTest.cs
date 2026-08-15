using Loom.Core.FlowAnalysis;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Core.TypeChecking;
using Loom.Core.TypeChecking.Types;
using LiteralType = Loom.Core.TypeChecking.Types.LiteralType;
using PrimitiveType = Loom.Core.TypeChecking.Types.PrimitiveType;

namespace Loom.Testing;

[Collection("Assembly")]
public class TypeNarrowerTest
{
    private static (SemanticModel model, Expression condition) GetCondition(string source)
    {
        var (_, semanticModel, flowAnalyzer) = Utility.FlowAnalyze(source);
        new TypeChecker(semanticModel, flowAnalyzer).Check();

        var tree = semanticModel.Tree;
        var conditionalNode = tree.GetDescendants<If>().FirstOrDefault() ?? tree.GetDescendants<While>().FirstOrDefault() as Statement;

        Assert.NotNull(conditionalNode);
        var condition = conditionalNode is If ifStmt ? ifStmt.Condition : ((While)conditionalNode).Condition;
        return (semanticModel, condition);
    }

    [Fact]
    public void ComputeBranchStates_LogicalAndOperator_DoesNotNarrow()
    {
        const string source = """
            let x: number | string = 42;
            let y: number = 10;
            if x == 42 && y == 10 { }
            """;

        var (model, condition) = GetCondition(source);
        var narrower = new TypeNarrower(model);
        var current = new FlowState();
        var binaryOp = Assert.IsType<BinaryOperator>(condition);
        var left = binaryOp.Left;
        var (trueState, falseState) = narrower.ComputeBranchStates(condition, current);
        var narrowed = narrower.TryGetNarrowedType(left, trueState, out _);
        Assert.False(narrowed);

        narrowed = narrower.TryGetNarrowedType(left, falseState, out _);
        Assert.False(narrowed);
    }

    [Fact]
    public void ComputeBranchStates_IdentifierEqualsIdentifier_DoesNotNarrow()
    {
        const string source = """
            let x: number | string = 42;
            let y: number | string = 42;
            if x == y { }
            """;

        var (model, condition) = GetCondition(source);
        var narrower = new TypeNarrower(model);
        var current = new FlowState();
        var binaryOp = Assert.IsType<BinaryOperator>(condition);
        var left = binaryOp.Left;
        var right = binaryOp.Right;
        var (trueState, falseState) = narrower.ComputeBranchStates(condition, current);

        Assert.False(narrower.TryGetNarrowedType(left, trueState, out _));
        Assert.False(narrower.TryGetNarrowedType(right, trueState, out _));
        Assert.False(narrower.TryGetNarrowedType(left, falseState, out _));
        Assert.False(narrower.TryGetNarrowedType(right, falseState, out _));
    }

    [Fact]
    public void ComputeBranchStates_LiteralEqualsLiteral_DoesNotNarrow()
    {
        const string source = "if 1 == 2 { }";
        var (model, condition) = GetCondition(source);
        var narrower = new TypeNarrower(model);
        var current = new FlowState();
        var binaryOp = Assert.IsType<BinaryOperator>(condition);
        var left = binaryOp.Left;
        var exception = Record.Exception(() => narrower.ComputeBranchStates(condition, current));
        Assert.Null(exception);

        var (trueState, falseState) = narrower.ComputeBranchStates(condition, current);
        Assert.False(narrower.TryGetNarrowedType(left, trueState, out _));
        Assert.False(narrower.TryGetNarrowedType(left, falseState, out _));
    }

    [Fact]
    public void ComputeBranchStates_CallExpressionEqualsLiteral_DoesNotNarrow()
    {
        const string source = """
            fn foo(): number { return 42; }
            if foo() == 42 { }
            """;

        var (model, condition) = GetCondition(source);
        var narrower = new TypeNarrower(model);
        var current = new FlowState();
        var binaryOp = Assert.IsType<BinaryOperator>(condition);
        var left = binaryOp.Left;
        var (trueState, falseState) = narrower.ComputeBranchStates(condition, current);
        Assert.False(narrower.TryGetNarrowedType(left, trueState, out _));
        Assert.False(narrower.TryGetNarrowedType(left, falseState, out _));
    }

    [Fact]
    public void ComputeBranchStates_ElementAccessWithVariableIndex_DoesNotNarrow()
    {
        const string source = """
            let arr = [1, 2, 3];
            let i = 0;
            if arr[i] == 1 { }
            """;

        var (model, condition) = GetCondition(source);
        var narrower = new TypeNarrower(model);
        var current = new FlowState();
        var binaryOp = Assert.IsType<BinaryOperator>(condition);
        var left = binaryOp.Left;
        var (trueState, falseState) = narrower.ComputeBranchStates(condition, current);

        Assert.False(narrower.TryGetNarrowedType(left, trueState, out _));
        Assert.False(narrower.TryGetNarrowedType(left, falseState, out _));
    }

    [Fact]
    public void ComputeBranchStates_NonUnionNotEqualsLiteral_KeepsBaseTypeUnchanged()
    {
        const string source = """
            let x: number = 42;
            if x != 42 { }
            """;

        var (model, condition) = GetCondition(source);
        var narrower = new TypeNarrower(model);
        var current = new FlowState();
        var binaryOp = Assert.IsType<BinaryOperator>(condition);
        var left = binaryOp.Left;
        var (trueState, falseState) = narrower.ComputeBranchStates(condition, current);
        var narrowed = narrower.TryGetNarrowedType(left, trueState, out var trueType);
        Assert.True(narrowed);

        var primitive = Assert.IsType<PrimitiveType>(trueType);
        Assert.Equal(PrimitiveTypeKind.Number, primitive.Kind);

        narrowed = narrower.TryGetNarrowedType(left, falseState, out var falseType);
        Assert.True(narrowed);
        var literal = Assert.IsType<LiteralType>(falseType);
        Assert.Equal(42L, literal.Value);
    }

    [Fact]
    public void ComputeBranchStates_UnionCollapsesToSingleMember_ThenExactMatchIsNever()
    {
        const string source = """
            let x: number | string | bool = 42;
            if x == 42 { }
            """;

        var (model, condition) = GetCondition(source);
        var narrower = new TypeNarrower(model);
        var current = new FlowState();

        var binaryOp = Assert.IsType<BinaryOperator>(condition);
        var left = binaryOp.Left;
        var (trueState, _) = narrower.ComputeBranchStates(condition, current);
        var narrowed = narrower.TryGetNarrowedType(left, trueState, out var trueType);
        Assert.True(narrowed);
        Assert.IsType<LiteralType>(trueType);

        var (_, innerFalseState) = narrower.ComputeBranchStates(condition, trueState);
        narrowed = narrower.TryGetNarrowedType(left, innerFalseState, out var falseType);
        Assert.True(narrowed);
        var primitive = Assert.IsType<PrimitiveType>(falseType);
        Assert.Equal(PrimitiveTypeKind.Never, primitive.Kind);
    }

    [Fact]
    public void ComputeBranchStates_PropertyAccessOnCallExpression_DoesNotNarrow()
    {
        const string source = """
            interface Obj { prop: number | string }
            fn getObj(): Obj { return none as never as Obj; }
            if getObj().prop == 42 { }
            """;

        var (model, condition) = GetCondition(source);
        var narrower = new TypeNarrower(model);
        var current = new FlowState();

        var binaryOp = Assert.IsType<BinaryOperator>(condition);
        var left = binaryOp.Left;

        var exception = Record.Exception(() => narrower.ComputeBranchStates(condition, current));
        Assert.Null(exception);

        var (trueState, falseState) = narrower.ComputeBranchStates(condition, current);
        Assert.False(narrower.TryGetNarrowedType(left, trueState, out _));
        Assert.False(narrower.TryGetNarrowedType(left, falseState, out _));
    }

    [Fact]
    public void ComputeBranchStates_IdentifierEqualsLiteral_TrueNarrowsToLiteral()
    {
        const string source = """
            let x: number | string = 42;
            if x == 42 { }
            """;

        var (model, condition) = GetCondition(source);
        var narrower = new TypeNarrower(model);
        var current = new FlowState();

        var binaryOp = Assert.IsType<BinaryOperator>(condition);
        var left = binaryOp.Left;

        var (trueState, falseState) = narrower.ComputeBranchStates(condition, current);

        var narrowed = narrower.TryGetNarrowedType(left, trueState, out var trueType);
        Assert.True(narrowed);
        var literal = Assert.IsType<LiteralType>(trueType);
        Assert.Equal(42L, literal.Value);

        narrowed = narrower.TryGetNarrowedType(left, falseState, out var falseType);
        Assert.True(narrowed);
        var primitive = Assert.IsType<PrimitiveType>(falseType);
        Assert.Equal(PrimitiveTypeKind.String, primitive.Kind);
    }

    [Fact]
    public void ComputeBranchStates_IdentifierNotEqualsLiteral_TrueRemovesLiteral()
    {
        const string source = """
            let x: number | string = 42;
            if x != 42 { }
            """;

        var (model, condition) = GetCondition(source);
        var narrower = new TypeNarrower(model);
        var current = new FlowState();

        var binaryOp = Assert.IsType<BinaryOperator>(condition);
        var left = binaryOp.Left;

        var (trueState, falseState) = narrower.ComputeBranchStates(condition, current);

        var narrowed = narrower.TryGetNarrowedType(left, trueState, out var trueType);
        Assert.True(narrowed);
        var primitive = Assert.IsType<PrimitiveType>(trueType);
        Assert.Equal(PrimitiveTypeKind.String, primitive.Kind);

        narrowed = narrower.TryGetNarrowedType(left, falseState, out var falseType);
        Assert.True(narrowed);
        var literal = Assert.IsType<LiteralType>(falseType);
        Assert.Equal(42L, literal.Value);
    }

    [Fact]
    public void ComputeBranchStates_IdentifierEqualsNone_TrueNarrowsToNone()
    {
        const string source = """
            let x: number? = none;
            if x == none { }
            """;

        var (model, condition) = GetCondition(source);
        var narrower = new TypeNarrower(model);
        var current = new FlowState();

        var binaryOp = Assert.IsType<BinaryOperator>(condition);
        var left = binaryOp.Left;

        var (trueState, falseState) = narrower.ComputeBranchStates(condition, current);

        var narrowed = narrower.TryGetNarrowedType(left, trueState, out var trueType);
        Assert.True(narrowed);
        var literal = Assert.IsType<LiteralType>(trueType);
        Assert.Null(literal.Value);

        narrowed = narrower.TryGetNarrowedType(left, falseState, out var falseType);
        Assert.True(narrowed);
        var primitive = Assert.IsType<PrimitiveType>(falseType);
        Assert.Equal(PrimitiveTypeKind.Number, primitive.Kind);
    }

    [Fact]
    public void ComputeBranchStates_IdentifierNotNone_TrueNarrowsToNonNullable()
    {
        const string source = """
            let x: number? = 5;
            if x != none { }
            """;

        var (model, condition) = GetCondition(source);
        var narrower = new TypeNarrower(model);
        var current = new FlowState();

        var binaryOp = Assert.IsType<BinaryOperator>(condition);
        var left = binaryOp.Left;

        var (trueState, falseState) = narrower.ComputeBranchStates(condition, current);

        var narrowed = narrower.TryGetNarrowedType(left, trueState, out var trueType);
        Assert.True(narrowed);
        var primitive = Assert.IsType<PrimitiveType>(trueType);
        Assert.Equal(PrimitiveTypeKind.Number, primitive.Kind);

        narrowed = narrower.TryGetNarrowedType(left, falseState, out var falseType);
        Assert.True(narrowed);
        var literal = Assert.IsType<LiteralType>(falseType);
        Assert.Null(literal.Value);
    }

    [Fact]
    public void ComputeBranchStates_PropertyAccessEqualsLiteral_TrueNarrowsProperty()
    {
        const string source = """
            interface Obj { prop: number | string }
            let obj = none as never as Obj;
            if obj.prop == 42 { }
            """;

        var (model, condition) = GetCondition(source);
        var narrower = new TypeNarrower(model);
        var current = new FlowState();

        var binaryOp = Assert.IsType<BinaryOperator>(condition);
        var left = binaryOp.Left;

        var (trueState, falseState) = narrower.ComputeBranchStates(condition, current);

        var narrowed = narrower.TryGetNarrowedType(left, trueState, out var trueType);
        Assert.True(narrowed);
        var literal = Assert.IsType<LiteralType>(trueType);
        Assert.Equal(42L, literal.Value);

        narrowed = narrower.TryGetNarrowedType(left, falseState, out var falseType);
        Assert.True(narrowed);
        var primitive = Assert.IsType<PrimitiveType>(falseType);
        Assert.Equal(PrimitiveTypeKind.String, primitive.Kind);
    }

    [Fact]
    public void ComputeBranchStates_InOperator_TrueNarrowsPropertyToNonNullable()
    {
        const string source = """
            interface Obj { field: number? }
            let obj = none as never as Obj;
            if "field" in obj {
                obj.field
            }
            """;

        var (model, condition) = GetCondition(source);
        var narrower = new TypeNarrower(model);
        var current = new FlowState();

        var propertyAccess = model.Tree.GetDescendants<QualifiedName>().First();
        var (trueState, falseState) = narrower.ComputeBranchStates(condition, current);

        var narrowed = narrower.TryGetNarrowedType(propertyAccess, trueState, out var trueType);
        Assert.True(narrowed);
        var primitive = Assert.IsType<PrimitiveType>(trueType);
        Assert.Equal(PrimitiveTypeKind.Number, primitive.Kind);

        narrowed = narrower.TryGetNarrowedType(propertyAccess, falseState, out var falseType);
        Assert.True(narrowed);
        var none = Assert.IsType<PrimitiveType>(falseType);
        Assert.Equal(PrimitiveTypeKind.None, none.Kind);
    }

    [Fact]
    public void ComputeBranchStates_InOperator_NonLiteralKey_DoesNotNarrow()
    {
        const string source = """
            interface Obj { field: number? }
            let obj = none as never as Obj;
            let key = "field";
            if key in obj {
                obj.field
            }
            """;

        var (model, condition) = GetCondition(source);
        var narrower = new TypeNarrower(model);
        var current = new FlowState();

        var propertyAccess = model.Tree.GetDescendants<QualifiedName>().First();
        var (trueState, _) = narrower.ComputeBranchStates(condition, current);

        var narrowed = narrower.TryGetNarrowedType(propertyAccess, trueState, out _);
        Assert.False(narrowed);
    }

    [Fact]
    public void ComputeBranchStates_InOperator_NonAddressableBase_DoesNotNarrow()
    {
        const string source = """
            interface Obj { field: number? }
            fn make(): Obj -> none as never as Obj;
            if "field" in make() { }
            """;

        var (model, condition) = GetCondition(source);
        var narrower = new TypeNarrower(model);
        var current = new FlowState();

        var (trueState, falseState) = narrower.ComputeBranchStates(condition, current);
        Assert.Equal(current, trueState);
        Assert.Equal(current, falseState);
    }

    [Fact]
    public void ComputeBranchStates_InOperator_UnknownField_DoesNotNarrow()
    {
        const string source = """
            interface Obj { field: number? }
            let obj = none as never as Obj;
            if "nonexistent" in obj { }
            """;

        var (model, condition) = GetCondition(source);
        var narrower = new TypeNarrower(model);
        var current = new FlowState();

        var (trueState, falseState) = narrower.ComputeBranchStates(condition, current);
        Assert.Equal(current, trueState);
        Assert.Equal(current, falseState);
    }

    [Fact]
    public void ComputeBranchStates_IsOperator_NonAddressableSubject_DoesNotNarrow()
    {
        const string source = """
            interface Foo;
            fn make(): unknown -> none as never as Foo;
            if make() is Foo { }
            """;

        var (model, condition) = GetCondition(source);
        var narrower = new TypeNarrower(model);
        var current = new FlowState();

        var (trueState, falseState) = narrower.ComputeBranchStates(condition, current);
        Assert.Equal(current, trueState);
        Assert.Equal(current, falseState);
    }

    [Fact]
    public void ComputeBranchStates_ElementAccessEqualsLiteral_TrueNarrowsElement()
    {
        const string source = """
            let arr = [1, 2, 3];
            if arr[0] == 1 { }
            """;

        var (model, condition) = GetCondition(source);
        var narrower = new TypeNarrower(model);
        var current = new FlowState();

        var binaryOp = Assert.IsType<BinaryOperator>(condition);
        var left = binaryOp.Left;

        var (trueState, falseState) = narrower.ComputeBranchStates(condition, current);

        var narrowed = narrower.TryGetNarrowedType(left, trueState, out var trueType);
        Assert.True(narrowed);
        var literal = Assert.IsType<LiteralType>(trueType);
        Assert.Equal(1L, literal.Value);

        narrowed = narrower.TryGetNarrowedType(left, falseState, out var falseType);
        Assert.True(narrowed);
        var primitive = Assert.IsType<PrimitiveType>(falseType);
        Assert.Equal(PrimitiveTypeKind.Number, primitive.Kind);
    }

    [Fact]
    public void ComputeBranchStates_QualifiedNameEnumMemberEquals_TrueNarrowsToLiteral()
    {
        const string source = """
            enum Status { Active, Inactive }
            let x: Status = Status.Active;
            if x == Status.Active { }
            """;

        var (model, condition) = GetCondition(source);
        var narrower = new TypeNarrower(model);
        var current = new FlowState();
        var binaryOp = Assert.IsType<BinaryOperator>(condition);
        var left = binaryOp.Left;
        var (trueState, falseState) = narrower.ComputeBranchStates(condition, current);
        var narrowed = narrower.TryGetNarrowedType(left, trueState, out var trueType);
        Assert.True(narrowed);

        var literal = Assert.IsType<LiteralType>(trueType);
        Assert.Equal(0d, literal.Value);

        narrowed = narrower.TryGetNarrowedType(left, falseState, out var falseType);
        Assert.True(narrowed);
        var literal2 = Assert.IsType<LiteralType>(falseType);
        Assert.Equal(1d, literal2.Value);
    }

    [Fact]
    public void ComputeBranchStates_NameOfEqualsLiteral_TrueNarrowsToStringLiteral()
    {
        const string source = """
            let s = nameof(x);
            if s == "x" { }
            """;

        var (model, condition) = GetCondition(source);
        var narrower = new TypeNarrower(model);
        var current = new FlowState();

        var binaryOp = Assert.IsType<BinaryOperator>(condition);
        var left = binaryOp.Left;

        var (trueState, falseState) = narrower.ComputeBranchStates(condition, current);

        var narrowed = narrower.TryGetNarrowedType(left, trueState, out var trueType);
        Assert.True(narrowed);
        var literal = Assert.IsType<LiteralType>(trueType);
        Assert.Equal("x", literal.Value);

        narrowed = narrower.TryGetNarrowedType(left, falseState, out var falseType);
        Assert.True(narrowed);
        var primitive = Assert.IsType<PrimitiveType>(falseType);
        Assert.Equal(PrimitiveTypeKind.Never, primitive.Kind);
    }

    [Fact]
    public void ComputeBranchStates_NestedPropertyAccess_TrueNarrowsCorrectly()
    {
        const string source = """
            interface Inner { prop: number | string }
            interface Outer { inner: Inner }
            let obj = none as never as Outer;
            if obj.inner.prop == 42 { }
            """;

        var (model, condition) = GetCondition(source);
        var narrower = new TypeNarrower(model);
        var current = new FlowState();

        var binaryOp = Assert.IsType<BinaryOperator>(condition);
        var left = binaryOp.Left;

        var (trueState, falseState) = narrower.ComputeBranchStates(condition, current);

        var narrowed = narrower.TryGetNarrowedType(left, trueState, out var trueType);
        Assert.True(narrowed);
        var literal = Assert.IsType<LiteralType>(trueType);
        Assert.Equal(42L, literal.Value);

        narrowed = narrower.TryGetNarrowedType(left, falseState, out var falseType);
        Assert.True(narrowed);
        var primitive = Assert.IsType<PrimitiveType>(falseType);
        Assert.Equal(PrimitiveTypeKind.String, primitive.Kind);
    }

    [Fact]
    public void ComputeBranchStates_NonBinaryExpression_DoesNotNarrow()
    {
        const string source2 = """
            let b = true;
            if b { }
            """;

        var (model, condition) = GetCondition(source2);
        var narrower = new TypeNarrower(model);
        var current = new FlowState();

        var (trueState, falseState) = narrower.ComputeBranchStates(condition, current);

        var narrowed = narrower.TryGetNarrowedType(condition, trueState, out _);
        Assert.True(narrowed);
        narrowed = narrower.TryGetNarrowedType(condition, falseState, out _);
        Assert.True(narrowed);
    }

    [Fact]
    public void ComputeBranchStates_ElementAccessOnUnionBase_NarrowsBaseType()
    {
        const string source = """
            let arr: number[] | string[] = [1, 2, 3];
            if arr[0] == 1 { }
            """;

        var (model, condition) = GetCondition(source);
        var narrower = new TypeNarrower(model);
        var current = new FlowState();

        var binaryOp = Assert.IsType<BinaryOperator>(condition);
        var elementAccess = Assert.IsType<ElementAccess>(binaryOp.Left);
        var baseExpression = elementAccess.Expression;

        var (trueState, falseState) = narrower.ComputeBranchStates(condition, current);

        var narrowedTrue = narrower.TryGetNarrowedType(baseExpression, trueState, out var trueType);
        var narrowedFalse = narrower.TryGetNarrowedType(baseExpression, falseState, out var falseType);

        Assert.True(narrowedTrue);
        Assert.True(narrowedFalse);
        Assert.NotNull(trueType);
        Assert.NotNull(falseType);
    }

    [Fact]
    public void ComputeBranchStates_LogicalAnd_BothOperandsUnnarrowable_MergesEmptyStates()
    {
        const string source = """
            fn foo(): bool -> true;
            fn bar(): bool -> true;
            if foo() && bar() { }
            """;

        var (model, condition) = GetCondition(source);
        var narrower = new TypeNarrower(model);
        var current = new FlowState();

        var (trueState, falseState) = narrower.ComputeBranchStates(condition, current);

        Assert.True(trueState.NarrowedTypes.IsEmpty);
        Assert.True(falseState.NarrowedTypes.IsEmpty);
    }

    [Fact]
    public void ComputeBranchStates_LogicalOr_BothOperandsUnnarrowable_MergesEmptyStates()
    {
        const string source = """
            fn foo(): bool -> true;
            fn bar(): bool -> true;
            if foo() || bar() { }
            """;

        var (model, condition) = GetCondition(source);
        var narrower = new TypeNarrower(model);
        var current = new FlowState();

        var (trueState, falseState) = narrower.ComputeBranchStates(condition, current);

        Assert.True(trueState.NarrowedTypes.IsEmpty);
        Assert.True(falseState.NarrowedTypes.IsEmpty);
    }

    [Fact]
    public void ComputeBranchStates_TypePredicate_ParameterSubject_NarrowsArgument()
    {
        const string source = """
            fn is_number(value: unknown): value is number {
                return true;
            }
            let v: number | string = 420;
            if is_number(v) {
                v
            }
            """;

        var (model, condition) = GetCondition(source);
        var narrower = new TypeNarrower(model);
        var current = new FlowState();

        var invocation = Assert.IsType<Invocation>(condition);
        var argument = invocation.Arguments.ArgumentList[0];
        var (trueState, falseState) = narrower.ComputeBranchStates(condition, current);

        var narrowed = narrower.TryGetNarrowedType(argument, trueState, out var trueType);
        Assert.True(narrowed);
        var truePrimitive = Assert.IsType<PrimitiveType>(trueType);
        Assert.Equal(PrimitiveTypeKind.Number, truePrimitive.Kind);

        narrowed = narrower.TryGetNarrowedType(argument, falseState, out var falseType);
        Assert.True(narrowed);
        var falsePrimitive = Assert.IsType<PrimitiveType>(falseType);
        Assert.Equal(PrimitiveTypeKind.String, falsePrimitive.Kind);
    }

    [Fact]
    public void ComputeBranchStates_TypePredicate_SelfReceiver_NarrowsReceiver()
    {
        const string source = """
            interface Widget { label: string }
            interface Container { is_kind: fn<T>(): @ is T }
            let c = none as never as Container;
            if c.is_kind::<Widget>() {
                c
            }
            """;

        var (model, condition) = GetCondition(source);
        var narrower = new TypeNarrower(model);
        var current = new FlowState();

        var invocation = Assert.IsType<Invocation>(condition);
        var receiver = Assert.IsType<QualifiedName>(invocation.Expression).Identifier;
        var (trueState, _) = narrower.ComputeBranchStates(condition, current);

        var narrowed = narrower.TryGetNarrowedType(receiver, trueState, out var trueType);
        Assert.True(narrowed);
        var interfaceType = Assert.IsType<InterfaceType>(trueType);
        Assert.Equal("Widget", interfaceType.Name);
    }

    [Fact]
    public void ComputeBranchStates_TypePredicate_SelfReceiver_MultiSegment_NarrowsNestedReceiver()
    {
        const string source = """
            interface Widget { label: string }
            interface Container { is_kind: fn<T>(): @ is T }
            interface Wrapper { inner: Container }
            let w = none as never as Wrapper;
            if w.inner.is_kind::<Widget>() {
                w
            }
            """;

        var (model, condition) = GetCondition(source);
        var narrower = new TypeNarrower(model);
        var current = new FlowState();

        var invocation = Assert.IsType<Invocation>(condition);
        var callee = Assert.IsType<QualifiedName>(invocation.Expression);
        var receiver = new QualifiedName(callee.Identifier, callee.Names[..^1]);
        var (trueState, _) = narrower.ComputeBranchStates(condition, current);

        var narrowed = narrower.TryGetNarrowedType(receiver, trueState, out var trueType);
        Assert.True(narrowed);
        var interfaceType = Assert.IsType<InterfaceType>(trueType);
        Assert.Equal("Widget", interfaceType.Name);
    }

    [Fact]
    public void ComputeBranchStates_NonPredicateInvocation_DoesNotNarrow()
    {
        const string source = """
            fn foo(): bool -> true;
            if foo() { }
            """;

        var (model, condition) = GetCondition(source);
        var narrower = new TypeNarrower(model);
        var current = new FlowState();

        var (trueState, falseState) = narrower.ComputeBranchStates(condition, current);

        Assert.True(trueState.NarrowedTypes.IsEmpty);
        Assert.True(falseState.NarrowedTypes.IsEmpty);
    }

    [Fact]
    public void ComputeBranchStates_TypePredicate_ComposesWithLogicalAnd()
    {
        const string source = """
            fn is_number(value: unknown): value is number {
                return true;
            }
            let v: number | string = 420;
            if is_number(v) && true {
                v
            }
            """;

        var (model, condition) = GetCondition(source);
        var narrower = new TypeNarrower(model);
        var current = new FlowState();

        var andOp = Assert.IsType<BinaryOperator>(condition);
        var invocation = Assert.IsType<Invocation>(andOp.Left);
        var argument = invocation.Arguments.ArgumentList[0];
        var (trueState, _) = narrower.ComputeBranchStates(condition, current);

        var narrowed = narrower.TryGetNarrowedType(argument, trueState, out var trueType);
        Assert.True(narrowed);
        var primitive = Assert.IsType<PrimitiveType>(trueType);
        Assert.Equal(PrimitiveTypeKind.Number, primitive.Kind);
    }
}