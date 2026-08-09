using Loom.Core.TypeChecking.Types;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.LanguageServer;

public static class TypeMembers
{
    private const int MaximumDepth = 8;

    public static IReadOnlyList<ObjectProperty> Of(Type? type) => Resolve(type, 0);

    public static Type? PropertyType(Type? receiver, string name) =>
        Of(receiver).FirstOrDefault(property => property.Name == name)?.ValueType;

    private static IReadOnlyList<ObjectProperty> Resolve(Type? type, int depth)
    {
        if (type == null || depth >= MaximumDepth)
            return [];

        switch (type)
        {
            case NativelyIndexableType indexable:
                return indexable.Properties;
            case InstantiatedType instantiated:
                return Resolve(instantiated.Expand(), depth + 1);
            case OptionalType optional:
                return Resolve(optional.NonNullableType, depth + 1);
            case TypeParameter parameter:
                return Resolve(parameter.Constraint, depth + 1);
            case IntersectionType intersection:
                return Distinct(intersection.Types.SelectMany(member => Resolve(member, depth + 1)));
            case UnionType union:
                return CommonToEvery(union, depth);
            default:
                return [];
        }
    }

    private static IReadOnlyList<ObjectProperty> CommonToEvery(UnionType union, int depth)
    {
        var perMember = union.Types.ConvertAll(member => Resolve(member, depth + 1));
        if (perMember.Count == 0 || perMember.Exists(properties => properties.Count == 0))
            return [];

        var shared = perMember[0].Where(property => perMember.TrueForAll(other => other.Any(candidate => candidate.Name == property.Name)));
        return Distinct(shared);
    }

    private static IReadOnlyList<ObjectProperty> Distinct(IEnumerable<ObjectProperty> properties) =>
        properties.GroupBy(property => property.Name).Select(group => group.First()).ToArray();
}
