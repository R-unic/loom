using Loom.Core.Resolving;
using Loom.Core.TypeChecking.Types;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.LanguageServer;

public static class TypeMembers
{
    private const int MaximumDepth = 8;

    /// <param name="semanticModel">
    ///     When given, an <see cref="InterfaceType" /> reports the methods of every trait it implements
    ///     alongside its own properties - see <see cref="SemanticModel.GetMembersOf" />. Optional because a
    ///     caller with no semantic model to hand (none of the current ones) still gets the interface's own
    ///     properties rather than nothing.
    /// </param>
    public static IReadOnlyList<ObjectProperty> Of(Type? type, SemanticModel? semanticModel = null) => Resolve(type, 0, semanticModel);

    public static Type? PropertyType(Type? receiver, string name, SemanticModel? semanticModel = null) =>
        Of(receiver, semanticModel).FirstOrDefault(property => property.Name == name)?.ValueType;

    private static IReadOnlyList<ObjectProperty> Resolve(Type? type, int depth, SemanticModel? semanticModel)
    {
        if (type == null || depth >= MaximumDepth)
            return [];

        return type switch
        {
            // checked ahead of the NativelyIndexableType case it would otherwise fall into: a trait method
            // is not part of an interface's own ObjectType, so only GetMembersOf knows to add it
            InterfaceType interfaceType => semanticModel == null ? interfaceType.Properties : semanticModel.GetMembersOf(interfaceType),
            NativelyIndexableType indexable => indexable.Properties,
            InstantiatedType instantiated => Resolve(instantiated.Expand(), depth + 1, semanticModel),
            OptionalType optional => Resolve(optional.NonNullableType, depth + 1, semanticModel),
            TypeParameter parameter => Resolve(parameter.Constraint, depth + 1, semanticModel),
            IntersectionType intersection => Distinct(intersection.Types.SelectMany(member => Resolve(member, depth + 1, semanticModel))),
            UnionType union => CommonToEvery(union, depth, semanticModel),
            _ => [],
        };
    }

    private static IReadOnlyList<ObjectProperty> CommonToEvery(UnionType union, int depth, SemanticModel? semanticModel)
    {
        var perMember = union.Types.ConvertAll(member => Resolve(member, depth + 1, semanticModel));
        if (perMember.Count == 0 || perMember.Exists(properties => properties.Count == 0))
            return [];

        var shared = perMember[0].Where(property => perMember.TrueForAll(other => other.Any(candidate => candidate.Name == property.Name)));
        return Distinct(shared);
    }

    private static ObjectProperty[] Distinct(IEnumerable<ObjectProperty> properties) =>
        properties.GroupBy(property => property.Name).Select(group => group.First()).ToArray();
}
