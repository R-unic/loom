using Loom.Core.TypeChecking.Types;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.Core.TypeChecking;

/// <summary>
///     Measures a type against a <see cref="ConditionalArm" />'s pattern, binding whatever the pattern's
///     <c>let</c> names stood for.
/// </summary>
/// <remarks>
///     A pattern with no binders in it is an ordinary assignability question and is answered as one - that
///     is what <c>T is string ? ... </c> means. Binders are what force the structural walk: nothing about
///     "is this assignable" can say which part of the source landed on <c>let R</c>, so those positions
///     have to be reached by taking both types apart in step.
///     <para>
///         One instance per match, holding the state the walk carries: what has been bound, which pairs are
///         already being compared, and which patterns were found to hold a binder. Threading those through
///         every method as parameters is the same thing without a place to keep the memo, and the memo is
///         what stops <see cref="ContainsBinder" /> re-walking the pattern at every step.
///     </para>
/// </remarks>
internal sealed class TypeMatcher(IReadOnlyList<TypeParameter> binders, TypeParameterSubstitution bindings)
{
    private readonly Dictionary<Type, bool> _hasBinder = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<(Type, Type)> _visiting = new(ReferencePairComparer.Instance);

    public static bool TryMatch(Type subject, Type pattern, IReadOnlyList<TypeParameter> binders, TypeParameterSubstitution bindings) =>
        binders.Count == 0
            ? Matches(subject, pattern)
            : new TypeMatcher(binders, bindings).Match(subject, pattern);

    /// <summary>
    ///     Whether <paramref name="subject" /> fits <paramref name="pattern" />, which is assignability plus
    ///     one special case: <c>none</c> in type position lexes as a literal, and a literal only ever matches
    ///     itself, so a <c>none</c> arm would miss the <c>none</c> half of an optional - which is the whole
    ///     of what a NonNullable is written to catch.
    /// </summary>
    private static bool Matches(Type subject, Type pattern) =>
        Type.IsNone(pattern) && Type.IsNone(subject) || subject.IsAssignableTo(pattern);

    private bool Match(Type subject, Type pattern)
    {
        if (pattern is TypeParameter parameter && IsBinder(parameter))
            return Bind(parameter, subject);

        if (!ContainsBinder(pattern))
            return Matches(subject, pattern);

        var pair = (subject, pattern);
        if (!_visiting.Add(pair))
            return true;

        try
        {
            return MatchStructure(subject, pattern);
        }
        finally
        {
            _visiting.Remove(pair);
        }
    }

    private bool MatchStructure(Type subject, Type pattern) =>
        (subject, pattern) switch
        {
            (OptionalType s, OptionalType p) => Match(s.NonNullableType, p.NonNullableType),
            (ArrayType s, ArrayType p) => Match(s.ElementType, p.ElementType),
            (TupleType s, TupleType p) => MatchInOrder(s.ElementTypes, p.ElementTypes),
            (FunctionType s, FunctionType p) => MatchFunction(s, p),
            (InstantiatedType s, InstantiatedType p) when s.GenericType.Equals(p.GenericType) => MatchInOrder(s.Arguments, p.Arguments),
            (InstantiatedType s, _) => Match(s.Expand(), pattern),
            (UnionType s, UnionType p) => MatchUnion(s, p),
            _ => Matches(subject, pattern)
        };

    private bool MatchInOrder(List<Type> subjects, List<Type> patterns)
    {
        if (subjects.Count != patterns.Count)
            return false;

        return !subjects.Where((t, i) => !Match(t, patterns[i])).Any();
    }

    /// <summary>
    ///     Matches union members by finding each pattern member a partner, rather than pairing them off in
    ///     order: a union's members have no order anybody wrote, so pairing by position would make
    ///     <c>string | let R</c> match <c>string | number</c> and not <c>number | string</c>.
    /// </summary>
    /// <remarks>
    ///     Members with no binder in them are matched first, so a binder does not consume the partner a
    ///     literal arm needed. A failed attempt has to put back whatever it bound on the way, since a
    ///     later partner may still succeed.
    /// </remarks>
    private bool MatchUnion(UnionType subject, UnionType pattern)
    {
        if (subject.Types.Count != pattern.Types.Count)
            return false;

        var unmatched = new List<Type>(subject.Types);
        foreach (var member in pattern.Types.OrderBy(ContainsBinder))
        {
            var partner = unmatched.FindIndex(candidate => TryMatchMember(candidate, member));
            if (partner < 0)
                return false;

            unmatched.RemoveAt(partner);
        }

        return true;
    }

    private bool TryMatchMember(Type subject, Type pattern)
    {
        var restore = new TypeParameterSubstitution(bindings);
        if (Match(subject, pattern))
            return true;

        bindings.Clear();
        foreach (var (binder, bound) in restore)
            bindings[binder] = bound;

        return false;
    }

    /// <summary>
    ///     A rest parameter bound to a name takes the whole remainder of the signature as a tuple -
    ///     <c>fn(..let P): unknown</c> is how the parameter list itself is captured. Written as an element
    ///     array instead - <c>fn(..unknown[]): let R</c> - it is a shape every remaining parameter must fit,
    ///     which is what makes a pattern match a function of any arity.
    /// </summary>
    private bool MatchFunction(FunctionType subject, FunctionType pattern)
    {
        if (subject.IsAsync != pattern.IsAsync)
            return false;
        
        var hasRest = pattern is { HasRestParameter: true, ParameterTypes.Count: > 0 };
        var fixedCount = hasRest ? pattern.ParameterTypes.Count - 1 : pattern.ParameterTypes.Count;
        if (subject.ParameterTypes.Count < fixedCount || !hasRest && subject.ParameterTypes.Count != fixedCount)
            return false;

        for (var i = 0; i < fixedCount; i++)
            if (!Match(subject.ParameterTypes[i], pattern.ParameterTypes[i]))
                return false;

        return (!hasRest || MatchRestParameter(subject, pattern.ParameterTypes[^1], fixedCount))
            && Match(subject.ReturnType, pattern.ReturnType);
    }

    private bool MatchRestParameter(FunctionType subject, Type rest, int fixedCount)
    {
        var remaining = subject.ParameterTypes.GetRange(fixedCount, subject.ParameterTypes.Count - fixedCount);
        if (rest is TypeParameter restBinder && IsBinder(restBinder))
            return Bind(restBinder, new TupleType(remaining));

        var element = rest is ArrayType array ? array.ElementType : rest;
        return remaining.TrueForAll(parameterType => Match(parameterType, element));
    }

    /// <summary>
    ///     Binds <paramref name="parameter" /> to <paramref name="type" />, or checks it against what an
    ///     earlier position already bound it to. One name appearing twice in a pattern therefore means the
    ///     two positions must agree, rather than silently widening to their union.
    /// </summary>
    private bool Bind(TypeParameter parameter, Type type)
    {
        if (parameter.Constraint != null && !type.IsAssignableTo(parameter.Constraint))
            return false;

        if (bindings.TryGetValue(parameter, out var existing))
            return existing.Equals(type);

        bindings[parameter] = type;
        return true;
    }

    private bool IsBinder(TypeParameter parameter) => binders.Any(binder => ReferenceEquals(binder, parameter));

    /// <remarks>
    ///     Memoized because the walk asks this of every pattern it steps into, and answering it rebuilds the
    ///     whole subtree: <see cref="TypeSolver.Transform" /> is the only general child enumerator there is,
    ///     and it constructs a new type per composite to hand the children over.
    /// </remarks>
    private bool ContainsBinder(Type type)
    {
        if (_hasBinder.TryGetValue(type, out var known))
            return known;
        
        _hasBinder[type] = false;
        if (type is TypeParameter parameter && IsBinder(parameter))
            return _hasBinder[type] = true;

        var found = false;
        TypeSolver.Transform(
            type,
            child =>
            {
                found |= ContainsBinder(child);
                return child;
            },
            simplify: false
        );

        return _hasBinder[type] = found;
    }

    /// <summary>
    ///     Whether the arms can be run against <paramref name="type" /> at all, or whether what it stands
    ///     for is still waiting on an instantiation.
    /// </summary>
    /// <remarks>
    ///     Deliberately does not walk into an object's members: an interface body holds its own generic's
    ///     parameters, and reading those as "still unknown" would defer every conditional whose subject was
    ///     any generic interface at all. What a subject is made of is its arguments, not its members.
    /// </remarks>
    public static bool IsUnresolved(Type type) =>
        type switch
        {
            TypeParameter or TypeVariable or KeyOfType or IndexedType or ConditionalType or MappedType => true,
            InstantiatedType instantiated => instantiated.Arguments.Any(IsUnresolved),
            ArrayType array => IsUnresolved(array.ElementType),
            TupleType tuple => tuple.ElementTypes.Any(IsUnresolved),
            OptionalType optional => IsUnresolved(optional.NonNullableType),
            UnionType union => union.Types.Any(IsUnresolved),
            IntersectionType intersection => intersection.Types.Any(IsUnresolved),
            FunctionType function => function.TypeParameters.Count == 0
                && (function.ParameterTypes.Any(IsUnresolved) || IsUnresolved(function.ReturnType)),
            _ => false
        };
}
