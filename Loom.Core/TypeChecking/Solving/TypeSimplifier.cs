using System.Runtime.CompilerServices;
using Loom.Core.Text;
using Loom.Core.TypeChecking.Types;
using InterfaceDeclaration = Loom.Core.Parsing.AST.InterfaceDeclaration;
using TraitDeclaration = Loom.Core.Parsing.AST.TraitDeclaration;
using TypeAlias = Loom.Core.Parsing.AST.TypeAlias;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.Core.TypeChecking.Solving;

public static class TypeSimplifier
{
    private static readonly ConditionalWeakTable<Type, Type> _simplifyCache = [];
    private static readonly ConditionalWeakTable<Type, Type> _expandCache = [];

    public static Type? GetMemberPropertyType(Type member, string propertyName) =>
        GetMemberType(member, m => m is NativelyIndexableType indexableType ? indexableType.GetProperty(propertyName)?.ValueType : null);

    public static Type? GetMemberElementType(Type member, Type indexType) =>
        GetMemberType(member, m => m is NativelyIndexableType indexableType ? indexableType.GetTypeAtIndex(indexType).BodyType?.ValueType : null);

    /// <summary>
    ///     Normalises <paramref name="type" /> - flattening, deduplicating and absorbing unions and
    ///     intersections - while leaving an instantiated generic standing as itself. This is the form a
    ///     node's type is bound as, so that a stored 'Result&lt;T, E&gt;' is still recognisable as one
    ///     (see <see cref="Expanded" /> for the counterpart, and issue #198 for what expanding here cost).
    /// </summary>
    public static Type Simplify(Type type) => Normalize(type, false);

    /// <summary>
    ///     <see cref="Simplify" />, with every instantiated generic it reaches replaced by its body. This
    ///     is the form for asking what a type is made of - which members it has, which union arms it
    ///     splits into, whether it is indexable - as opposed to what it is called.
    /// </summary>
    public static Type Expanded(Type type) => Normalize(type, true);

    private static Type Normalize(Type type, bool expand)
    {
        var cache = expand ? _expandCache : _simplifyCache;
        if (cache.TryGetValue(type, out var cached))
            return cached;

        var simplified = type switch
        {
            ObjectType objectType => SimplifyObject(objectType),
            UnionType union => SimplifyUnion(union, expand),
            IntersectionType intersection => SimplifyIntersection(intersection, expand),
            InstantiatedType instantiated => expand ? Normalize(instantiated.Expand(), true) : type,
            GenericType generic => Normalize(generic.UnderlyingType, expand),
            IndexedType indexed => ResolveIndex(indexed.Target, indexed.Index) ?? type,
            MappedType mapped => mapped.Resolve() ?? type,
            ConditionalType conditional => ConditionalTypeEvaluator.TryEvaluate(conditional) ?? type,
            _ => type
        };

        // AddOrUpdate rather than Add: a conditional whose arms name the alias it belongs to can re-enter
        // this for the very type being normalised, and Add throws on a key already present.
        cache.AddOrUpdate(type, simplified);
        return simplified;
    }

    /// <summary>
    ///     Resolves indexing <paramref name="target" /> by <paramref name="index" />, optionally to the same
    ///     receiver-kind/operator-kind rule <c>TypeChecker.MemberAccess.cs</c>'s <c>GetTypeAtIndexNative</c>
    ///     applies to a member access - a static member only valid where <paramref name="isInterfaceNamespaceAccess" />
    ///     says the receiver is the interface's own namespace value, an instance member only valid where it
    ///     doesn't, and <paramref name="dotKind" /> (when given) required to agree with which kind was found.
    ///     The two purely type-level callers (a deferred <c>T[K]</c>, a generic's <c>keyof</c> substitution)
    ///     pass neither and get the old, unchecked behaviour - they resolve what a type is made of, with no
    ///     receiver expression to reason about; only the intersection branch of <c>IndexType</c>, which is
    ///     resolving a real member-access expression, opts in.
    /// </summary>
    public static Type? ResolveIndex(Type target, Type index, SyntaxKind? dotKind = null, bool? isInterfaceNamespaceAccess = null)
    {
        if (index is TypeParameter or KeyOfType)
            return ResolveKeys(index) is { } keys ? ResolveIndex(target, keys, dotKind, isInterfaceNamespaceAccess) : null;

        if (Expanded(target) is not NativelyIndexableType indexable)
            return null;

        var (bodyType, _) = indexable.GetTypeAtIndex(index);
        if (bodyType is ObjectProperty property && indexable is InterfaceType && isInterfaceNamespaceAccess.HasValue)
        {
            if (property.IsStatic != isInterfaceNamespaceAccess.Value)
                return null;

            if (dotKind is SyntaxKind.Dot or SyntaxKind.QuestionDot or SyntaxKind.ColonColon
                && property.IsStatic != (dotKind == SyntaxKind.ColonColon))
                return null;
        }

        return bodyType?.ValueType;
    }

    /// <summary>
    ///     The key union a deferred <c>keyof(T)</c> stands for once <c>T</c> is known, or null while it is
    ///     still a parameter. Shared by both substitution paths so a <c>keyof</c> resolves the same whether
    ///     the generic being instantiated is a function or a type alias.
    /// </summary>
    public static Type? ResolveKeys(Type type)
    {
        if (type is not KeyOfType keyOfType)
            return null;

        var target = keyOfType.Target;
        if (target is TypeParameter or TypeVariable or IndexedType or KeyOfType)
            return null;

        return Expanded(target) is ObjectType or InterfaceType ? ((NativelyIndexableType)Expanded(target)).KeyUnion() : null;
    }

    private static ObjectType SimplifyObject(ObjectType objectType) =>
        objectType.Properties.Count == 0 && objectType.Indexer != null && objectType.Indexer.KeyType.Equals(PrimitiveType.Number)
            ? new ArrayType(objectType.Indexer.ValueType, objectType.Indexer.IsMutable)
            : objectType;

    private static Type SimplifyUnion(UnionType union, bool expand)
    {
        var mergedInstantiations = CollapseGenericInstantiations(union.Types);
        var flattened = FlattenNestedUnions(mergedInstantiations.ConvertAll(t => Normalize(t, expand)));
        var collapsed = CollapseBooleanLiterals(flattened);
        var distinct = RemoveDuplicates(collapsed, true);
        var absorbed = ApplyAbsorption(distinct, true);
        if (!absorbed.Any(Type.IsNone))
            return absorbed.Count switch
            {
                0 => PrimitiveType.Never,
                1 => absorbed.First(),
                _ => new UnionType(absorbed)
            };

        var nonNullable = absorbed.FindAll(Type.IsDefined);
        return nonNullable.Count switch
        {
            0 => PrimitiveType.None,
            1 => new OptionalType(nonNullable.First()),
            _ => new OptionalType(SimplifyUnion(new UnionType(nonNullable), expand))
        };
    }

    private static List<Type> CollapseBooleanLiterals(List<Type> types)
    {
        var hasTrue = types.Any(t => t is LiteralType { Value: true });
        var hasFalse = types.Any(t => t is LiteralType { Value: false });

        if (!hasTrue || !hasFalse)
            return types;

        var result = types
            .Where(t => t is not LiteralType { Value: bool })
            .ToList();

        result.Add(PrimitiveType.Bool);
        return result;
    }

    private static Type SimplifyIntersection(IntersectionType intersection, bool expand)
    {
        var flattened = FlattenNestedIntersections(intersection.Types.ConvertAll(t => Normalize(t, expand)));
        var distinct = RemoveDuplicates(flattened, false);

        if (distinct.Count == 0 || distinct.Any(Type.IsNever))
            return PrimitiveType.Never;

        if (distinct.All(t => t is ObjectType or InterfaceType))
            return Normalize(MergeObjectTypes([.. distinct], expand), expand);

        if (distinct.Any(t => t is UnionType))
            return Normalize(DistributeIntersection(distinct, expand), expand);

        var absorbed = ApplyAbsorption(distinct, false);
        if (absorbed.Count > 1 && absorbed.All(t => t is PrimitiveType))
            return PrimitiveType.Never;

        return absorbed.Count == 1 ? absorbed.First() : new IntersectionType(absorbed);
    }

    private static Type MergeObjectTypes(List<Type> types, bool expand)
    {
        var propertyDictionary = new Dictionary<string, ObjectProperty>();
        ObjectIndexer? mergedIndexer = null;

        var objectTypes = types.ConvertAll(t => t is InterfaceType i ? i.ObjectType : t).Cast<ObjectType>().ToList();
        foreach (var objectType in objectTypes)
        {
            foreach (var property in objectType.Properties)
                if (propertyDictionary.TryGetValue(property.Name, out var existing))
                {
                    var valueType = Normalize(new IntersectionType([existing.ValueType, property.ValueType]), expand);
                    var isMutable = existing.IsMutable && property.IsMutable;
                    var isStatic = existing.IsStatic && property.IsStatic;
                    propertyDictionary[property.Name] = new ObjectProperty(isMutable, property.Name, valueType, isStatic);
                }
                else
                {
                    propertyDictionary[property.Name] = property;
                }

            if (objectType.Indexer == null) continue;
            if (mergedIndexer == null)
            {
                mergedIndexer = new ObjectIndexer(objectType.Indexer.IsMutable, objectType.Indexer.KeyType, objectType.Indexer.ValueType);
            }
            else
            {
                var newKeyType = Normalize(new IntersectionType([mergedIndexer.KeyType, objectType.Indexer.KeyType]), expand);
                var newValueType = Normalize(new IntersectionType([mergedIndexer.ValueType, objectType.Indexer.ValueType]), expand);
                var newIsMutable = mergedIndexer.IsMutable && objectType.Indexer.IsMutable;
                mergedIndexer = new ObjectIndexer(newIsMutable, newKeyType, newValueType);
            }
        }

        var properties = propertyDictionary.Values.ToList();
        if (mergedIndexer != null && mergedIndexer.KeyType.Equals(PrimitiveType.Number) && objectTypes.All(t => t is ArrayType))
            return new ArrayType(mergedIndexer.ValueType, mergedIndexer.IsMutable);

        return new ObjectType(mergedIndexer, properties);
    }

    private static Type DistributeIntersection(List<Type> types, bool expand)
    {
        for (var i = 0; i < types.Count; i++)
        {
            if (types[i] is not UnionType union)
                continue;

            var rest = new List<Type>(types);
            rest.RemoveAt(i);

            var distributed = union.Types
                .Select(variant => new IntersectionType([variant, ..rest]))
                .Select(t => Normalize(t, expand))
                .Where(simplified => !Type.IsNever(simplified))
                .ToList();

            return distributed.Count switch
            {
                0 => PrimitiveType.Never,
                1 => distributed.First(),
                _ => new UnionType(distributed)
            };
        }

        return new IntersectionType(types);
    }

    private static List<Type> ApplyAbsorption(List<Type> types, bool isUnion)
    {
        var result = new List<Type>();
        foreach (var t1 in types)
        {
            var isAbsorbed = false;
            foreach (var t2 in types.Where(t2 => !t1.Equals(t2)))
            {
                if (isUnion)
                {
                    if (!IsSubsetOf(t1, t2)) continue;
                    isAbsorbed = true;
                    break;
                }

                if (!IsSubsetOf(t2, t1)) continue;
                isAbsorbed = true;
                break;
            }

            if (isAbsorbed) continue;
            result.Add(t1);
        }

        return result;
    }

    private static bool IsSubsetOf(Type a, Type b) =>
        a switch
        {
            LiteralType la when b is LiteralType lb => Equals(la.Value, lb.Value),
            LiteralType lit when b is PrimitiveType prim => lit.Value switch
            {
                double or long or int => prim.Kind == PrimitiveTypeKind.Number,
                string => prim.Kind == PrimitiveTypeKind.String,
                bool => prim.Kind == PrimitiveTypeKind.Bool,
                null => prim.Kind is PrimitiveTypeKind.Unknown or PrimitiveTypeKind.None,
                _ => false
            },
            PrimitiveType when b is LiteralType => false,
            _ => (a, b) switch
            {
                (UnionType ua, UnionType ub) => ua.Types.All(t => IsSubsetOf(t, ub)),
                (UnionType ua, _) => ua.Types.All(t => IsSubsetOf(t, b)),
                (_, UnionType ub) => ub.Types.Any(t => IsSubsetOf(a, t)),
                (IntersectionType ia, _) => ia.Types.All(t => IsSubsetOf(t, b)),
                (_, IntersectionType ib) => ib.Types.All(t => IsSubsetOf(a, t)),
                _ => a.Equals(b) || a.IsAssignableTo(b)
            }
        };

    private static List<Type> RemoveDuplicates(List<Type> types, bool isUnion) =>
        [.. types
            .Aggregate(
                new List<Type>(),
                (unique, item) =>
                {
                    if (!unique.Any(item.Equals))
                        unique.Add(item);

                    return unique;
                }
            )
            .Where(t => !isUnion || Type.IsNotNever(t))];

    /// <summary>
    ///     Collapses sibling union members that are instantiations of the same generic interface/trait/
    ///     alias into a single instantiation, combining each type-argument position per that parameter's
    ///     variance (union for covariant, intersection for contravariant). Invariant positions block the
    ///     collapse for that group unless every member already agrees on the argument. Must run before
    ///     each member is individually normalised, since <see cref="Expanded" /> replaces an
    ///     InstantiatedType with its structural form and loses the shared generic identity this relies on.
    /// </summary>
    private static List<Type> CollapseGenericInstantiations(List<Type> types)
    {
        var instantiations = types.OfType<InstantiatedType>().ToList();
        if (instantiations.Count < 2)
            return types;

        var consumed = new HashSet<InstantiatedType>();
        var replacements = new List<InstantiatedType>();
        foreach (var group in instantiations.GroupBy(i => i.GenericType))
        {
            var members = group.ToList();
            if (members.Count < 2 || !TryCollapseGroup(group.Key, members, out var replacement))
                continue;

            consumed.UnionWith(members);
            replacements.Add(replacement);
        }

        if (replacements.Count == 0)
            return types;

        var result = types.Where(t => t is not InstantiatedType instantiated || !consumed.Contains(instantiated)).ToList();
        result.AddRange(replacements);
        return result;
    }

    private static bool TryCollapseGroup(GenericType generic, List<InstantiatedType> members, out InstantiatedType replacement)
    {
        replacement = null!;
        if (generic.Declaration is not (InterfaceDeclaration or TraitDeclaration or TypeAlias))
            return false;

        var argumentCount = members[0].Arguments.Count;
        if (members.Any(m => m.Arguments.Count != argumentCount))
            return false;

        var newArguments = new List<Type>();
        for (var i = 0; i < argumentCount; i++)
        {
            var variance = i < generic.Parameters.Count ? generic.Parameters[i].Variance : Variance.Invariant;
            var argumentsAtIndex = members.ConvertAll(m => m.Arguments[i]);
            switch (variance)
            {
                case Variance.Covariant:
                    newArguments.Add(Simplify(new UnionType(argumentsAtIndex)));
                    break;
                case Variance.Contravariant:
                    newArguments.Add(Simplify(new IntersectionType(argumentsAtIndex)));
                    break;
                default:
                    var distinct = RemoveDuplicates(argumentsAtIndex, false);
                    if (distinct.Count != 1)
                        return false;

                    newArguments.Add(distinct[0]);
                    break;
            }
        }

        replacement = generic.Construct(newArguments);
        return true;
    }

    private static List<Type> FlattenNestedUnions(List<Type> types) => [.. types.SelectMany(t => t is UnionType union ? union.Types : [t])];

    private static List<Type> FlattenNestedIntersections(List<Type> types) =>
        [.. types.SelectMany(t => t is IntersectionType intersection ? intersection.Types : [t])];

    private static Type? GetMemberType(Type member, Func<Type, Type?> extractMember)
    {
        if (member is InstantiatedType instantiated)
            member = instantiated.Expand();

        if (member is not UnionType union)
            return extractMember(member);

        var members = union.Types
            .Select(t => GetMemberType(t, extractMember))
            .Where(t => t != null)
            .Cast<Type>()
            .ToList();

        return members.Count switch
        {
            0 => null,
            1 => members[0],
            _ => Simplify(new UnionType(members))
        };
    }
}