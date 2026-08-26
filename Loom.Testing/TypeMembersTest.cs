using Loom.Core.TypeChecking.Types;
using Loom.LanguageServer;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.Testing;

/// <summary>
///     What the editor offers after a dot. Completion is the one place a wrong answer is invisible - an
///     offered member that does not exist reads as the language having it, and a missing one reads as the
///     language not.
/// </summary>
[Collection("Assembly")]
public class TypeMembersTest
{
    private static ObjectType Object(params string[] names) =>
        new(null, [.. names.Select(name => new ObjectProperty(false, name, PrimitiveType.Number))]);

    private static string[] NamesOf(Type? type) => [.. TypeMembers.Of(type).Select(property => property.Name).OrderBy(name => name)];

    [Fact]
    public void NoType_HasNoMembers() => Assert.Empty(TypeMembers.Of(null));

    [Fact]
    public void APrimitive_HasNoMembers() => Assert.Empty(TypeMembers.Of(PrimitiveType.Number));

    [Fact]
    public void AnObject_OffersItsOwnProperties() => Assert.Equal(["a", "b"], NamesOf(Object("a", "b")));

    /// <remarks>The members are the ones you get after the optionality is dealt with, not none at all.</remarks>
    [Fact]
    public void AnOptional_OffersWhatItWraps() => Assert.Equal(["a"], NamesOf(new OptionalType(Object("a"))));

    [Fact]
    public void ATypeParameter_OffersWhatItsConstraintGuarantees()
    {
        Assert.Equal(["a"], NamesOf(new TypeParameter("T", Object("a"))));
        Assert.Empty(TypeMembers.Of(new TypeParameter("T")));
    }

    /// <remarks>An intersection is all of its parts at once, so every part's members are reachable.</remarks>
    [Fact]
    public void AnIntersection_OffersEveryPartsMembers() =>
        Assert.Equal(["a", "b"], NamesOf(new IntersectionType([Object("a"), Object("b")])));

    [Fact]
    public void AnIntersection_OffersAMemberSharedByItsParts_Once() =>
        Assert.Equal(["a"], NamesOf(new IntersectionType([Object("a"), Object("a")])));

    /// <remarks>
    ///     A union is only one of its members at a time, so offering anything not on all of them would offer
    ///     a member the value may well not have.
    /// </remarks>
    [Fact]
    public void AUnion_OffersOnlyWhatEveryMemberHas() =>
        Assert.Equal(["shared"], NamesOf(new UnionType([Object("shared", "a"), Object("shared", "b")])));

    [Fact]
    public void AUnion_WithAMemberThatHasNone_OffersNothing() =>
        Assert.Empty(TypeMembers.Of(new UnionType([Object("a"), PrimitiveType.Number])));

    [Fact]
    public void PropertyType_FindsAMemberByName()
    {
        Assert.Equal(PrimitiveType.Number, TypeMembers.PropertyType(Object("a"), "a"));
        Assert.Null(TypeMembers.PropertyType(Object("a"), "missing"));
        Assert.Null(TypeMembers.PropertyType(null, "a"));
    }

    /// <remarks>
    ///     A type that wraps itself would otherwise walk forever; the depth cap is what makes the walk total
    ///     rather than merely usually-terminating.
    /// </remarks>
    [Fact]
    public void ADeeplyNestedType_StopsRatherThanRecursingForever()
    {
        Type nested = Object("a");
        for (var depth = 0; depth < 20; depth++)
            nested = new OptionalType(nested);

        Assert.Empty(TypeMembers.Of(nested));
    }
}
