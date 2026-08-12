using System.Text.Json;
using Loom.TypeGenerator.ApiTypes;
using ValueType = Loom.TypeGenerator.ApiTypes.ValueType;

namespace Loom.TypeGenerator;

internal static class ClassUtility
{
    private static readonly Dictionary<MemberBase, Security> _propertySecurityCache = [];

    // public static string? SafePropertyType(string? valueType) =>
    //     string.IsNullOrEmpty(valueType)
    //         ? null
    //         : Constants.PropertyTypeMap.GetValueOrDefault(valueType, valueType);

    public static string SafeRenamedInstance(string? name) =>
        name != null && Constants.RenamableAutoTypes.TryGetValue(name, out var value)
            ? value
            : SafeName(name);

    public static bool HasMatchingSuperclass(Class rbxClass, Dictionary<string, Class> classRefs, Func<Class, bool> predicate)
    {
        if (rbxClass.Superclass == Constants.RootClassName)
            return false;

        var superclass = classRefs[AssertClassName(rbxClass.Superclass, classRefs)];
        return predicate(superclass) || HasMatchingSuperclass(superclass, classRefs, predicate);
    }

    public static bool IsService(Class rbxClass) =>
        HasTag(rbxClass, "Service")
        && !HasTag(rbxClass, "Hidden")
        && !Constants.ClassBlacklist.Contains(rbxClass.Name);

    public static bool IsCreatable(Class rbxClass) =>
        !Constants.CreatableBlacklist.Contains(rbxClass.Name)
        && !HasTag(rbxClass, "NotCreatable")
        && !HasTag(rbxClass, "Service");

    public static string SafeValueType(ValueType valueType)
    {
        if (valueType.Category == "Enum")
            return $"Enum[\"{valueType.Name}\"]";

        var valueTypeName = SafeName(valueType.Name);
        if (string.IsNullOrEmpty(valueTypeName) || !valueTypeName.EndsWith('?'))
            return Constants.ValueTypeMap.GetValueOrDefault(valueType.Name, valueTypeName);

        var nonOptionalType = valueTypeName[..^1];
        return $"{Constants.ValueTypeMap.GetValueOrDefault(nonOptionalType, nonOptionalType)}?";
    }

    public static string? SafeReturnType(ValueType[]? valueTypes)
    {
        var typeNames = valueTypes?
            .Select(SafeValueType)
            .Where(valueType => !string.IsNullOrEmpty(valueType))
            .Select(valueType => Constants.ReturnTypeMap.GetValueOrDefault(valueType, valueType))
            .ToArray();

        return typeNames?.Length switch
        {
            <= 0 => null,
            null => "void",
            1 => typeNames[0],
            > 1 => $"({string.Join(", ", typeNames)})"
        };
    }

    public static string SafeRename(string? name) =>
        name != null && Constants.RenameMap.TryGetValue(name, out var value)
            ? value
            : SafeName(name);

    public static Security GetSecurity(string className, MemberBase member)
    {
        if (Constants.SecurityOverrides.TryGetValue(className, out var classSecurity)
            && member.Name != null
            && classSecurity!.TryGetValue(member.Name, out var securityOverride))
            return securityOverride;

        return member.MemberType switch
        {
            "Callback" => new Security { Read = "NotAccessibleSecurity", Write = member.Security?.ToString()! },
            "Function" => new Security { Read = member.Security?.ToString()!, Write = "NotAccessibleSecurity" },
            "Event" => new Security { Read = member.Security?.ToString()!, Write = "NotAccessibleSecurity" },
            "Property" => GetPropertySecurity(member),
            _ => throw new NotSupportedException($"Member type not supported: {member.MemberType}")
        };
    }

    private static Security GetPropertySecurity(MemberBase member)
    {
        if (_propertySecurityCache.TryGetValue(member, out var cached))
            return cached;

        var security = JsonSerializer.Deserialize<Security>(member.Security?.ToString()!)!;
        _propertySecurityCache[member] = security;
        return security;
    }

    public static bool HasTag(MemberBase container, string tag) => container.Tags != null && container.Tags.Any(t => t.ToString() == tag);
    public static bool HasTag(Class container, string tag) => container.Tags != null && container.Tags.Any(t => t.ToString() == tag);

    public static string SafeName(string? name) =>
        name == null
            ? ""
            : ContainsBadCharacter(name)
                ? name.Replace("\"", "\\\"")
                : name;

    /// <summary>Returns the given <see cref="className" /> if it's in <see cref="classRefs" />, throws if not</summary>
    public static string AssertClassName(string className, Dictionary<string, Class> classRefs)
    {
        if (classRefs.ContainsKey(className))
            return className;

        Log.Fatal($"undefined class name: {className}");
        return null;
    }

    private static bool ContainsBadCharacter(string name) => Constants.BadNameCharacters.Any(name.Contains);
}