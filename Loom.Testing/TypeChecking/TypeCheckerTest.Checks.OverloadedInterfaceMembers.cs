using Loom.Core.Diagnostics;
using Loom.Core.TypeChecking;
using Loom.Core.TypeChecking.Types;
using Type = Loom.Core.TypeChecking.Types.Type;
using Loom.Core.TypeChecking.Solving;
using Loom.Core.TypeChecking.Intrinsic;


namespace Loom.Testing;

public partial class TypeCheckerTest
{
    [Fact]
    public void Checks_InterfaceDeclaration_DuplicateFunctionProperty_MergesIntoIntersection()
    {
        const string source = """
            declare interface ShapeStatic {
                create: fn(): number;
                create: fn(x: number, y: number): number;
            }
            """;

        var type = Utility.GetLastStatementType(source);
        var interfaceType = Assert.IsType<InterfaceType>(type);
        var createProperty = interfaceType.GetProperty("create")!;
        var intersection = Assert.IsType<IntersectionType>(createProperty.ValueType);
        Assert.Equal(2, intersection.Types.Count);
        Assert.All(intersection.Types, t => Assert.IsType<FunctionType>(t));
    }

    [Fact]
    public void Checks_OverloadedInvocation_PicksCandidateByArity()
    {
        const string source = """
            declare interface Shape { x: number; y: number; }
            declare interface ShapeStatic {
                create: fn(): Shape;
                create: fn(x: number, y: number): Shape;
            }
            declare let Shape: ShapeStatic;

            Shape.create(1, 2)
            """;

        var type = Utility.GetLastStatementType(source);
        Assert.IsType<InterfaceType>(type);
        Assert.Equal("Shape", ((InterfaceType)type).Name);
    }

    [Fact]
    public void Checks_OverloadedInvocation_NoArgs_PicksZeroArityCandidate()
    {
        const string source = """
            declare interface Shape { x: number; y: number; }
            declare interface ShapeStatic {
                create: fn(): Shape;
                create: fn(x: number, y: number): Shape;
            }
            declare let Shape: ShapeStatic;

            Shape.create()
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Checks_OverloadedInvocation_PicksRestParameterCandidate_WithManyArguments()
    {
        const string source = """
            declare interface Shape { x: number; y: number; }
            declare interface ShapeStatic {
                create: fn(x: number): Shape;
                create: fn(..points: number[]): Shape;
            }
            declare let Shape: ShapeStatic;

            Shape.create(1, 2, 3, 4, 5)
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void ThrowsFor_OverloadedInvocation_NoCandidateMatches()
    {
        const string source = """
            declare interface Shape { x: number; y: number; }
            declare interface ShapeStatic {
                create: fn(): Shape;
                create: fn(x: number, y: number): Shape;
            }
            declare let Shape: ShapeStatic;

            Shape.create("nope")
            """;

        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        var diagnostic = diagnostics.Find(d => d.Code == InternalCodes.NoOverloadMatch);
        Assert.NotNull(diagnostic);
    }

    [Theory]
    [InlineData("CFrame::create()")]
    [InlineData("CFrame::create(Vector3::create())")]
    [InlineData("CFrame::create(Vector3::create(), Vector3::create())")]
    [InlineData("CFrame::create(1, 2, 3)")]
    [InlineData("CFrame::create(1, 2, 3, 0, 0, 0, 1)")]
    [InlineData("CFrame::create(1, 2, 3, 1, 0, 0, 0, 1, 0, 0, 0, 1)")]
    public void Checks_CFrameCreate_ResolvesEachOverloadShape(string source)
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(source);
        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void ThrowsFor_CFrameCreate_NoOverloadMatches()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("CFrame::create(\"not a number\")");
        var diagnostic = diagnostics.Find(d => d.Code == InternalCodes.NoOverloadMatch);
        Assert.NotNull(diagnostic);
    }

    /// <summary>
    ///     An overload set with more than one generic candidate used to reject every call: candidate
    ///     selection measured each argument against the candidate's own, still-unbound type parameters
    ///     directly, and a concrete argument is never assignable to a bare, uninstantiated one. Picking
    ///     an arity-appropriate candidate should defer that question to the inference
    ///     <see cref="TypeChecker.CheckGenericInvocation" /> runs once a candidate is chosen, the same way
    ///     a lone (non-overloaded) generic candidate always has. Issue found writing jecs.d.loom, where
    ///     every one of <c>World</c>'s <c>get</c>/<c>has</c>/<c>query</c> overloads is generic.
    /// </summary>
    [Theory]
    [InlineData("taker.take(1, 5)", "5?")]
    [InlineData("taker.take(1, 5, \"hi\")", "(5?, \"hi\"?)")]
    public void Checks_OverloadedGenericInvocation_ResolvesEachArity(string call, string expected)
    {
        var type = Utility.GetLastStatementType(
            $$"""
            declare sealed interface Taker {
                take: fn<A>(entity: number, a: A): A?;
                take: fn<A, B>(entity: number, a: A, b: B): (A?, B?);
            }

            declare let taker: Taker;
            {{call}}
            """
        );

        Assert.Equal(expected, TypeSimplifier.Expanded(type).ToString());
    }
}
