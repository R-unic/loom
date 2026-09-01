using Loom.Core.Diagnostics;
using Loom.Core.TypeChecking.Types;

namespace Loom.Testing.TypeChecking;

/// <summary>
///     Types the <c>Set</c>/<c>MutSet</c> intrinsics declared in <c>loom.loom</c>.
///     <see cref="Testing.Runtime.SetRuntimeTest" /> covers what they lower to and that it computes the right answer.
/// </summary>
[Collection("Assembly")]
public class SetIntrinsicTest
{
    [Theory]
    [InlineData("Set::of(1).to_array()", "number[]")]
    [InlineData("Set::of(1).has(1)", "bool")]
    [InlineData("Set::of(1).size", "number")]
    [InlineData("Set::of(1).is_empty", "bool")]
    public void Infers(string expression, string expected) => Assert.Equal(expected, Utility.GetLastStatementType(expression).ToString());

    /// <summary>
    ///     A set's member type reaches the surface as its indexer's key, whichever way it was built.
    ///     <c>Set::of(1)</c> widens to <c>number</c> the way <c>mut [1]</c> does - a one-element
    ///     <c>MutSet&lt;1&gt;</c> would have nothing that could be added to it.
    /// </summary>
    [Theory]
    [InlineData("Set::of(1, 2, 3)", "number")]
    [InlineData("Set::of(1)", "number")]
    [InlineData("Set::of(\"a\")", "string")]
    [InlineData("MutSet::of(1)", "number")]
    [InlineData("Set::empty::<string>()", "string")]
    [InlineData("[1, 2].to_set()", "number")]
    [InlineData("Set::of(1).union(MutSet::of(2))", "number")]
    public void InfersMemberType(string expression, string expected)
    {
        // Whether a set surfaces already expanded or still as an instantiation depends on how it was
        // built, and neither is what this is about.
        var type = Utility.GetLastStatementType(expression);
        if (type is InstantiatedType instantiated)
            type = instantiated.Expand();

        var set = Assert.IsType<NativelyIndexableType>(type, exactMatch: false);
        Assert.Equal(expected, Assert.IsType<ObjectIndexer>(set.Indexer).KeyType.ToString());
    }

    /// <summary>
    ///     Every algebra member takes <c>Set&lt;T&gt;</c>, so a <c>MutSet</c> that could not be passed to one
    ///     would be a trap. It works because dropping <c>mut</c> from the indexer is a widening.
    /// </summary>
    [Fact]
    public void MutableSetIsAssignableToImmutableSet() =>
        Utility.AssertNoErrors(Utility.GetTypeCheckerDiagnostics("let both = Set::of(1).union(MutSet::of(2)); let sub = MutSet::of(1).is_subset_of(Set::of(1));"));

    [Fact]
    public void ImmutableSetHasNoMutators()
    {
        var diagnostics = Utility.GetAnalysisDiagnostics("Set::of(1).add(2);");
        Assert.Contains(diagnostics.Set, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ImmutableSetHasNoClear()
    {
        var diagnostics = Utility.GetAnalysisDiagnostics("Set::of(1).clear();");
        Assert.Contains(diagnostics.Set, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ThrowsFor_MemberOfTheWrongElementType()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("Set::of(1, 2).has(\"three\");");
        Assert.Contains(diagnostics.Set, d => d.Code == InternalCodes.TypeMismatch);
    }

    /// <summary>
    ///     <c>to_set</c> is built in C# alongside the rest of an array's members, and can only name
    ///     <c>Set&lt;T&gt;</c> once the intrinsics have compiled - so its absence would look like an ordinary
    ///     missing-member error rather than a bootstrap ordering problem.
    /// </summary>
    [Fact]
    public void ArrayToSetCarriesTheElementType()
    {
        var instantiated = Assert.IsType<InstantiatedType>(Utility.GetLastStatementType("[\"a\", \"b\"].to_set()"));

        Assert.Equal("Set", instantiated.GenericType.Declaration.Name.Text);
        Assert.Equal(PrimitiveType.String, Assert.Single(instantiated.Arguments));
    }

    /// <summary>
    ///     Referencing a macro emits a lambda with one parameter per declared parameter, which cannot stand
    ///     for a variadic one - it would take a single argument and silently drop the rest. <c>Set::of</c> is
    ///     the first variadic macro, so this had no way of coming up before.
    /// </summary>
    [Fact]
    public void ThrowsFor_VariadicConstructorPassedAsAValue()
    {
        const string source = """
            declare fn consume<T>(make: fn(..values: T[]): Set<T>): void;
            consume(Set::of);
            """;

        Utility.AssertDiagnostic(
            Utility.GetTypeCheckerDiagnostics(source),
            InternalCodes.InvalidMacroReference,
            "Invocation macro 'of' takes a variable number of arguments, so it cannot be passed as a value."
        );
    }

    /// <summary>A set is a plain table of members, so it indexes with its element type.</summary>
    [Fact]
    public void IndexesWithTheElementType()
    {
        Assert.Equal(PrimitiveType.Bool, Utility.GetLastStatementType("Set::of(1, 2)[1]"));
        Assert.Contains(Utility.GetTypeCheckerDiagnostics("Set::of(1, 2)[\"a\"];").Set, d => d.Severity == DiagnosticSeverity.Error);
    }
}
