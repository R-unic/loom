using Loom.Core.Generation.Macros;
using Loom.Luau.AST;
using BinaryOperator = Loom.Luau.AST.BinaryOperator;
using Identifier = Loom.Luau.AST.Identifier;
using UnaryOperator = Loom.Luau.AST.UnaryOperator;

namespace Loom.Testing;

/// <summary>
///     The scan a fused array pipeline asks before binding a stage's parameter: every name the body
///     mentions, and whether the shape was understood at all.
/// </summary>
/// <remarks>
///     Both answers are allowed to be pessimistic and neither is allowed to be optimistic. A name it misses
///     is a name a later stage reads from outside the loop and gets bound to the wrong value; a shape it
///     silently accepts without walking is the same thing. Driving it through a fused pipeline tests the
///     decision by its consequences, which is why these go at it directly.
/// </remarks>
[Collection("Assembly")]
public class LuauIdentifiersTest
{
    private static HashSet<string> Collect(LuauNode? node)
    {
        var names = new HashSet<string>();
        Assert.True(LuauIdentifiers.TryCollect(node, names), $"did not understand {node?.GetType().Name ?? "null"}");

        return names;
    }

    [Fact]
    public void Literals_MentionNothing()
    {
        Assert.Empty(Collect(null));
        Assert.Empty(Collect(new NumberLiteral(1)));
        Assert.Empty(Collect(new StringLiteral("a")));
        Assert.Empty(Collect(new BooleanLiteral(true)));
        Assert.Empty(Collect(new NilLiteral()));
        Assert.Empty(Collect(new Break()));
        Assert.Empty(Collect(new Continue()));
        Assert.Empty(Collect(new NoOpStatement()));
    }

    [Fact]
    public void AnIdentifier_IsItsOwnName() => Assert.Equal(["n"], Collect(new Identifier("n")));

    [Fact]
    public void Operators_ReachBothSides()
    {
        Assert.Equal(["n"], Collect(new UnaryOperator("-", new Identifier("n"))));
        Assert.Equal(["a", "b"], Collect(new BinaryOperator(new Identifier("a"), "+", new Identifier("b"))));
        Assert.Equal(["n"], Collect(new Parenthesized(new Identifier("n"))));
    }

    /// <remarks>Only the target: the member name is not a binding anything could capture.</remarks>
    [Fact]
    public void Accesses_ReachTheirTargetAndIndex()
    {
        Assert.Equal(["t"], Collect(new PropertyAccess(new Identifier("t"), ["field"])));
        Assert.Equal(["t", "i"], Collect(new ElementAccess(new Identifier("t"), new Identifier("i"))));
    }

    [Fact]
    public void Calls_ReachTheCalleeAndEveryArgument() =>
        Assert.Equal(
            ["f", "a", "b"],
            Collect(new Call(new Identifier("f"), [new Identifier("a"), new Identifier("b")]))
        );

    [Fact]
    public void Tables_ReachEveryInitializer() =>
        Assert.Equal(
            ["k", "v", "plain"],
            Collect(new Table([new ComputedPropertyTableInitializer(new Identifier("k"), new Identifier("v")), new TableInitializer(new Identifier("plain"))]))
        );

    [Fact]
    public void InterpolatedStrings_ReachTheirHoles() =>
        Assert.Equal(
            ["n"],
            Collect(new InterpolatedString([new InterpolatedStringTextSegment("a"), new InterpolatedStringExpressionSegment(new Identifier("n"))]))
        );

    [Fact]
    public void Conditionals_ReachEveryBranch()
    {
        var expression = new IfExpression(
            new Identifier("c"),
            new Identifier("t"),
            [new ElseIfExpressionBranch(new Identifier("c2"), new Identifier("t2"))],
            new Identifier("e")
        );

        Assert.Equal(["c", "t", "c2", "t2", "e"], Collect(expression));
    }

    /// <remarks>A function's own parameters count: binding one of those names would capture them too.</remarks>
    [Fact]
    public void Functions_ReachTheirParametersAndBody()
    {
        Assert.Equal(
            ["p", "body"],
            Collect(new AnonymousFunction(null, [new Parameter("p")], null, new Chunk([new ExpressionStatement(new Identifier("body"))])))
        );

        Assert.Equal(
            ["named", "p", "body"],
            Collect(new Function("named", null, [new Parameter("p")], null, new Chunk([new ExpressionStatement(new Identifier("body"))])))
        );
    }

    [Fact]
    public void Declarations_ReachTheirNameAndInitializer()
    {
        Assert.Equal(["c", "init"], Collect(new ConstVariable("c", null, new Identifier("init"))));
        Assert.Equal(["l", "init"], Collect(new LocalVariable("l", null, new Identifier("init"))));
        Assert.Equal(["a", "b", "init"], Collect(new MultiConstVariable(["a", "b"], new Identifier("init"))));
    }

    [Fact]
    public void Returns_ReachTheirExpressions()
    {
        Assert.Equal(["r"], Collect(new Return(new Identifier("r"))));
        Assert.Equal(["a", "b"], Collect(new MultiReturn([new Identifier("a"), new Identifier("b")])));
    }

    [Fact]
    public void Statements_ReachTheirBodies()
    {
        Assert.Equal(["d"], Collect(new Do(new Chunk([new ExpressionStatement(new Identifier("d"))]))));
        Assert.Equal(["w", "b"], Collect(new WhileStatement(new Identifier("w"), new Chunk([new ExpressionStatement(new Identifier("b"))]))));
        Assert.Equal(
            ["c", "t", "c2", "t2", "e"],
            Collect(
                new IfStatement(
                    new Identifier("c"),
                    new Chunk([new ExpressionStatement(new Identifier("t"))]),
                    [new ElseIfBranch(new Identifier("c2"), new Chunk([new ExpressionStatement(new Identifier("t2"))]))],
                    new Chunk([new ExpressionStatement(new Identifier("e"))])
                )
            )
        );
    }

    [Fact]
    public void Loops_ReachTheirNamesAndBounds()
    {
        Assert.Equal(
            ["k", "v", "source", "body"],
            Collect(new ForStatement(["k", "v"], new Identifier("source"), new Chunk([new ExpressionStatement(new Identifier("body"))])))
        );

        Assert.Equal(
            ["i", "start", "stop", "step", "body"],
            Collect(
                new NumericForStatement(
                    "i",
                    new Identifier("start"),
                    new Identifier("stop"),
                    new Identifier("step"),
                    new Chunk([new ExpressionStatement(new Identifier("body"))])
                )
            )
        );
    }

    /// <remarks>
    ///     The one answer that matters most: a shape it does not model must decline rather than report an
    ///     empty set, because an empty set reads as "captures nothing" and green-lights the fusion.
    /// </remarks>
    [Fact]
    public void AnUnmodelledShape_Declines()
    {
        var names = new HashSet<string>();
        Assert.False(LuauIdentifiers.TryCollect(new Spread(new Identifier("xs")), names));
    }
}
