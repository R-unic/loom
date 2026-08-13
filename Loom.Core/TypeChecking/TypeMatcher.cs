using Loom.Core.TypeChecking.Types;
using ArrayType = Loom.Core.TypeChecking.Types.ArrayType;
using FunctionType = Loom.Core.TypeChecking.Types.FunctionType;
using PrimitiveType = Loom.Core.TypeChecking.Types.PrimitiveType;
using Type = Loom.Core.TypeChecking.Types.Type;
using TypeParameter = Loom.Core.TypeChecking.Types.TypeParameter;

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
/// </remarks>
internal static class TypeMatcher
{
    public static bool TryMatch(Type subject, Type pattern, IReadOnlyList<TypeParameter> binders, TypeParameterSubstitution bindings) =>
        binders.Count == 0
            ? Matches(subject, pattern)
            : Match(subject, pattern, binders, bindings, new HashSet<(Type, Type)>(ReferencePairComparer.Instance));

    /// <summary>
    ///     Whether <paramref name="subject" /> fits <paramref name="pattern" />, which is assignability plus
    ///     one special case: <c>none</c> in type position lexes as a literal, and a literal only ever matches
    ///     itself, so a <c>none</c> arm would miss the <c>none</c> half of an optional - which is the whole
    ///     of what a NonNullable is written to catch.
    /// </summary>
    private static bool Matches(Type subject, Type pattern) =>
        (Type.IsNone(pattern) && Type.IsNone(subject)) || subject.IsAssignableTo(pattern);

    private static bool Match(
        Type subject,
        Type pattern,
        IReadOnlyList<TypeParameter> binders,
        TypeParameterSubstitution bindings,
        HashSet<(Type, Type)> visiting)
    {
        if (pattern is TypeParameter parameter && IsBinder(binders, parameter))
            return Bind(parameter, subject, bindings);

        // Everything below walks structure, which only a pattern with a binder somewhere inside it needs.
        if (!ContainsBinder(pattern, binders))
            return Matches(subject, pattern);

        var pair = (subject, pattern);
        if (!visiting.Add(pair))
            return true;

        try
        {
            return MatchStructure(subject, pattern, binders, bindings, visiting);
        }
        finally
        {
            visiting.Remove(pair);
        }
    }

    private static bool MatchStructure(
        Type subject,
        Type pattern,
        IReadOnlyList<TypeParameter> binders,
        TypeParameterSubstitution bindings,
        HashSet<(Type, Type)> visiting)
    {
        bool MatchChild(Type childSubject, Type childPattern) => Match(childSubject, childPattern, binders, bindings, visiting);

        switch (subject, pattern)
        {
            case (Types.OptionalType s, Types.OptionalType p):
                return MatchChild(s.NonNullableType, p.NonNullableType);

            case (ArrayType s, ArrayType p):
                return MatchChild(s.ElementType, p.ElementType);

            case (Types.TupleType s, Types.TupleType p) when s.ElementTypes.Count == p.ElementTypes.Count:
                return !s.ElementTypes.Where((element, i) => !MatchChild(element, p.ElementTypes[i])).Any();

            case (FunctionType s, FunctionType p):
                return MatchFunction(s, p, binders, bindings, visiting);

            // Compared as instantiations before either is expanded: 'Future<let V>' is asking which generic
            // the subject is, and expanding it first throws away the only thing that could answer.
            case (Types.InstantiatedType s, Types.InstantiatedType p)
                when s.GenericType.Equals(p.GenericType) && s.Arguments.Count == p.Arguments.Count:
                return !s.Arguments.Where((argument, i) => !MatchChild(argument, p.Arguments[i])).Any();

            case (Types.InstantiatedType s, _):
                return MatchChild(s.Expand(), pattern);

            case (Types.UnionType s, Types.UnionType p):
                return MatchUnion(s, p, binders, bindings, visiting);

            // Objects have no case of their own because no pattern can reach one: an object type is only
            // ever written as an interface name, and a name is an instantiation or an interface - handled
            // above - never a body with a binder loose inside it.
            default:
                return Matches(subject, pattern);
        }
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
    private static bool MatchUnion(
        Types.UnionType subject,
        Types.UnionType pattern,
        IReadOnlyList<TypeParameter> binders,
        TypeParameterSubstitution bindings,
        HashSet<(Type, Type)> visiting)
    {
        if (subject.Types.Count != pattern.Types.Count)
            return false;

        var unmatched = new List<Type>(subject.Types);
        foreach (var member in pattern.Types.OrderBy(member => ContainsBinder(member, binders)))
        {
            var partner = unmatched.FindIndex(candidate => TryMatchMember(candidate, member, binders, bindings, visiting));
            if (partner < 0)
                return false;

            unmatched.RemoveAt(partner);
        }

        return true;
    }

    private static bool TryMatchMember(
        Type subject,
        Type pattern,
        IReadOnlyList<TypeParameter> binders,
        TypeParameterSubstitution bindings,
        HashSet<(Type, Type)> visiting)
    {
        var restore = new TypeParameterSubstitution(bindings);
        if (Match(subject, pattern, binders, bindings, visiting))
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
    private static bool MatchFunction(
        FunctionType subject,
        FunctionType pattern,
        IReadOnlyList<TypeParameter> binders,
        TypeParameterSubstitution bindings,
        HashSet<(Type, Type)> visiting)
    {
        bool MatchChild(Type childSubject, Type childPattern) => Match(childSubject, childPattern, binders, bindings, visiting);

        if (subject.IsAsync != pattern.IsAsync)
            return false;

        var fixedCount = pattern.HasRestParameter ? pattern.ParameterTypes.Count - 1 : pattern.ParameterTypes.Count;
        if (subject.ParameterTypes.Count < fixedCount || !pattern.HasRestParameter && subject.ParameterTypes.Count != fixedCount)
            return false;

        for (var i = 0; i < fixedCount; i++)
            if (!MatchChild(subject.ParameterTypes[i], pattern.ParameterTypes[i]))
                return false;

        if (!pattern.HasRestParameter)
            return MatchChild(subject.ReturnType, pattern.ReturnType);

        var rest = pattern.ParameterTypes[^1];
        var remaining = subject.ParameterTypes.Skip(fixedCount).ToList();
        if (rest is TypeParameter restBinder && IsBinder(binders, restBinder))
        {
            if (!Bind(restBinder, new Types.TupleType(remaining), bindings))
                return false;
        }
        else
        {
            var element = rest is ArrayType array ? array.ElementType : rest;
            if (remaining.Any(parameterType => !MatchChild(parameterType, element)))
                return false;
        }

        return MatchChild(subject.ReturnType, pattern.ReturnType);
    }

    /// <summary>
    ///     Binds <paramref name="parameter" /> to <paramref name="type" />, or checks it against what an
    ///     earlier position already bound it to. One name appearing twice in a pattern therefore means the
    ///     two positions must agree, rather than silently widening to their union.
    /// </summary>
    private static bool Bind(TypeParameter parameter, Type type, TypeParameterSubstitution bindings)
    {
        if (parameter.Constraint != null && !type.IsAssignableTo(parameter.Constraint))
            return false;

        if (bindings.TryGetValue(parameter, out var existing))
            return existing.Equals(type);

        bindings[parameter] = type;
        return true;
    }

    private static bool IsBinder(IReadOnlyList<TypeParameter> binders, TypeParameter parameter) =>
        binders.Any(binder => ReferenceEquals(binder, parameter));

    /// <remarks>Only ever asked of a pattern that has binders - <see cref="TryMatch" /> answers the empty case before any walk starts.</remarks>
    private static bool ContainsBinder(Type type, IReadOnlyList<TypeParameter> binders)
    {
        var visited = new HashSet<Type>(ReferenceEqualityComparer.Instance);
        return Contains(type);

        bool Contains(Type current)
        {
            if (current is TypeParameter parameter && IsBinder(binders, parameter))
                return true;

            if (!visited.Add(current))
                return false;

            var found = false;
            TypeSolver.Transform(
                current,
                child =>
                {
                    found |= Contains(child);
                    return child;
                },
                simplify: false
            );

            return found;
        }
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
            TypeParameter or TypeVariable or KeyOfType or Types.IndexedType or ConditionalType or MappedType => true,
            Types.InstantiatedType instantiated => instantiated.Arguments.Any(IsUnresolved),
            ArrayType array => IsUnresolved(array.ElementType),
            Types.TupleType tuple => tuple.ElementTypes.Any(IsUnresolved),
            Types.OptionalType optional => IsUnresolved(optional.NonNullableType),
            Types.UnionType union => union.Types.Any(IsUnresolved),
            Types.IntersectionType intersection => intersection.Types.Any(IsUnresolved),
            FunctionType function => function.TypeParameters.Count == 0
                && (function.ParameterTypes.Any(IsUnresolved) || IsUnresolved(function.ReturnType)),
            _ => false
        };
}
