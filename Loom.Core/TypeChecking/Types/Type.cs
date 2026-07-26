using System.Runtime.CompilerServices;

namespace Loom.Core.TypeChecking.Types;

internal sealed class ReferencePairComparer : IEqualityComparer<(Type, Type)>
{
    public static readonly ReferencePairComparer Instance = new();
    public bool Equals((Type, Type) x, (Type, Type) y) => ReferenceEquals(x.Item1, y.Item1) && ReferenceEquals(x.Item2, y.Item2);
    public int GetHashCode((Type, Type) obj) => HashCode.Combine(RuntimeHelpers.GetHashCode(obj.Item1), RuntimeHelpers.GetHashCode(obj.Item2));
}

public abstract class Type : IEquatable<Type>
{
    private static readonly HashSet<(Type, Type)> _equalsVisiting = new(ReferencePairComparer.Instance);
    private static readonly HashSet<(Type, Type)> _assignableToVisiting = new(ReferencePairComparer.Instance);

    public abstract bool Equals(Type? other);

    protected static bool GuardedEquals(Type a, Type? b, Func<bool> compare)
    {
        if (ReferenceEquals(a, b)) return true;
        if (b == null) return false;

        var pair = (a, b);
        if (!_equalsVisiting.Add(pair))
            return true;

        try
        {
            return compare();
        }
        finally
        {
            _equalsVisiting.Remove(pair);
        }
    }

    /// <summary>
    ///     Self-referential interface members (e.g. Roblox's Instance.Parent: Instance) make the Type
    ///     object graph genuinely cyclic, so ObjectType/InterfaceType's IsAssignableTo overrides - which
    ///     walk into nested member/property types - need the same cycle guard Equals already has via
    ///     GuardedEquals. Re-entering the same (a, b) pair while it's already being checked means we're
    ///     walking a cycle, so it's treated as already-consistent rather than checked again.
    /// </summary>
    protected static bool GuardedAssignableTo(Type a, Type b, Func<bool> compare)
    {
        if (ReferenceEquals(a, b)) return true;

        var pair = (a, b);
        if (!_assignableToVisiting.Add(pair))
            return true;

        try
        {
            return compare();
        }
        finally
        {
            _assignableToVisiting.Remove(pair);
        }
    }

    protected static int GetTypeListHash<T>(List<T> types)
        where T : Type =>
        types.Aggregate(0, (current, arg) => current ^ arg.GetHashCode());

    protected static bool ListEquals<T>(List<T> list, List<T> otherList)
        where T : Type
    {
        if (list.Count != otherList.Count)
            return false;

        var equals = true;
        for (var i = 0; i < list.Count; i++)
        {
            var type = list[i];
            var otherType = otherList[i];
            equals &= ReferenceEquals(type, otherType) || type.Equals(otherType);
        }

        return equals;
    }

    public abstract override string ToString();
    public override bool Equals(object? obj) => Equals(obj as Type);
    public override int GetHashCode() => 0;

    public static bool IsNotNever(Type type) => !IsNever(type);
    public static bool IsNever(Type type) => type is PrimitiveType { Kind: PrimitiveTypeKind.Never } and not LiteralType;
    public static bool IsNotUnknown(Type type) => !IsUnknown(type);
    public static bool IsUnknown(Type type) => type is PrimitiveType { Kind: PrimitiveTypeKind.Unknown } and not LiteralType;
    public static bool IsDefined(Type type) => !IsNone(type);
    public static bool IsNone(Type type) => type is PrimitiveType { Kind: PrimitiveTypeKind.Void or PrimitiveTypeKind.None };
    public static bool IsNotOptional(Type type) => !IsOptional(type);
    public static bool IsOptional(Type type) => IsNone(type) || type is OptionalType || type is UnionType union && union.Types.Any(IsOptional);
    protected static string ParenthesizeIfNeeded(Type type) => RequiresParentheses(type) ? $"({type})" : type.ToString();
    private static bool RequiresParentheses(Type type) => type is (UnionType or IntersectionType or FunctionType) and not OptionalType;

    public Type NonNullable() =>
        IsNone(this)
            ? PrimitiveType.Never
            : this is OptionalType optional
                ? optional.NonNullableType.NonNullable()
                : IsOptional(this)
                    ? TypeSimplifier.Simplify(this).NonNullable()
                    : this;

    public virtual Type Widen() => this;

    public virtual bool IsAssignableTo(Type other) =>
        other switch
        {
            InterfaceType interfaceType => IsAssignableTo(interfaceType.AssignabilityType),
            UnionType union => union.Types.Exists(IsAssignableTo),
            IntersectionType intersection => intersection.Types.TrueForAll(IsAssignableTo),
            PrimitiveType primitive => primitive.Kind == PrimitiveTypeKind.Unknown || this is PrimitiveType thisPrimitive && thisPrimitive.IsAssignableTo(primitive),
            InstantiatedType instantiated => IsAssignableTo(instantiated.Expand()),
            _ => Equals(other)
        };
}