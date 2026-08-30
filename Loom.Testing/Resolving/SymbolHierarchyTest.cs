using System.Reflection;
using Loom.Core.Resolving.Symbols;
using Loom.Testing;

namespace Loom.Testing.Resolving;

/// <summary>
///     Guards the two invariants the symbol hierarchy rests on: a class stands for exactly one
///     <see cref="SymbolKind" />, and where a class sits in the hierarchy says the same thing about a name
///     as <see cref="Symbol.IsTypeKind" /> does about its kind. The resolver reads the first off the class
///     and the second off a bare kind, so the two drifting apart would file a declaration in one lookup
///     table and search for it in the other.
/// </summary>
public class SymbolHierarchyTest
{
    private static readonly List<Type> _concreteSymbolClasses = [.. typeof(Symbol).Assembly.GetTypes().Where(type => type.IsSubclassOf(typeof(Symbol)) && !type.IsAbstract)];

    [Fact]
    public void EveryKind_HasExactlyOneClass()
    {
        var byKind = _concreteSymbolClasses.ToLookup(KindOf);
        foreach (var kind in Enum.GetValues<SymbolKind>())
            Assert.Single(byKind[kind]);
    }

    [Fact]
    public void EveryClass_SitsOnTheSideOfTheHierarchyItsKindSaysItDoes()
    {
        foreach (var symbolClass in _concreteSymbolClasses)
        {
            var isTypeSymbol = symbolClass.IsSubclassOf(typeof(TypeSymbol));
            Assert.Equal(Symbol.IsTypeKind(KindOf(symbolClass)), isTypeSymbol);
            Assert.False(isTypeSymbol && symbolClass.IsSubclassOf(typeof(ValueSymbol)));
        }
    }

    /// <remarks>
    ///     Read off the class rather than an instance: building one takes a declaration node, and the point
    ///     here is that <see cref="Symbol.Kind" /> does not depend on how the instance was built.
    /// </remarks>
    private static SymbolKind KindOf(Type symbolClass) =>
        (SymbolKind)symbolClass
            .GetProperty(nameof(Symbol.Kind), BindingFlags.Public | BindingFlags.Instance)!
            .GetMethod!
            .Invoke(System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(symbolClass), null)!;
}
