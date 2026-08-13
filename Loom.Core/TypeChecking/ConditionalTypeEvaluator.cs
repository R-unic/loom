using Loom.Core.TypeChecking.Types;
using PrimitiveType = Loom.Core.TypeChecking.Types.PrimitiveType;
using Type = Loom.Core.TypeChecking.Types.Type;
using UnionType = Loom.Core.TypeChecking.Types.UnionType;

namespace Loom.Core.TypeChecking;

/// <summary>
///     Answers a <see cref="ConditionalType" /> once its subject is concrete, and leaves it standing
///     while it is not.
/// </summary>
/// <remarks>
///     <para>
///         Two bounds, as TypeScript uses. <see cref="MaxNestingDepth" /> is for a recursion that grows the
///         type on every step and so nests; <see cref="MaxTailIterations" /> is for the shape nearly every
///         recursive utility type actually takes - <c>Awaited&lt;V&gt;</c> as an arm's whole result - which
///         is unrolled iteratively here rather than nested, which is what keeps the small depth bound from
///         being hit by ordinary code.
///     </para>
///     <para>
///         A recursion that revisits an instantiation it has already produced does not need either bound:
///         <see cref="GenericType.Construct" /> hands back one instance per (definition, arguments) pair, so
///         seeing the same one twice within one evaluation means the unrolling has closed a loop and will
///         never terminate. The bounds are only for a recursion producing a genuinely new argument every step.
///     </para>
/// </remarks>
internal static class ConditionalTypeEvaluator
{
    private const int MaxNestingDepth = 50;
    private const int MaxTailIterations = 1000;

    // State of the evaluation that is running rather than of the type graph, so each thread gets its own -
    // the language server compiles more than one document at a time.
    [ThreadStatic] private static int _depth;
    [ThreadStatic] private static bool _overflowed;

    /// <summary>
    ///     Whether a bound was exceeded since this was last asked, clearing the flag. Evaluation runs deep
    ///     inside substitution, where there is no node to report against; the type checker asks here once an
    ///     instantiation is complete and reports it at the instantiation the user actually wrote.
    /// </summary>
    public static bool TakeOverflow()
    {
        var overflowed = _overflowed;
        _overflowed = false;
        return overflowed;
    }

    /// <summary>What <paramref name="conditional" /> works out to, or null while its subject is still unknown.</summary>
    public static Type? TryEvaluate(ConditionalType conditional)
    {
        if (_depth >= MaxNestingDepth)
            return Overflow();

        _depth++;
        try
        {
            var current = conditional;
            HashSet<InstantiatedType>? unrolled = null;
            for (var iteration = 0; iteration < MaxTailIterations; iteration++)
            {
                var result = EvaluateOnce(current, out var tailCall);
                if (tailCall == null)
                    return result;

                unrolled ??= new HashSet<InstantiatedType>(ReferenceEqualityComparer.Instance);
                if (!unrolled.Add(tailCall))
                    return Overflow();

                current = ConditionalBodyOf(tailCall);
            }

            return Overflow();
        }
        finally
        {
            _depth--;
        }
    }

    private static Type? Overflow()
    {
        _overflowed = true;
        return null;
    }

    /// <summary>
    ///     One pass of the arms. <paramref name="tailCall" /> comes back set where the arm's whole result is
    ///     another conditional alias, which <see cref="TryEvaluate" />'s loop takes rather than recursing
    ///     into - that is the tail-recursion elimination.
    /// </summary>
    private static Type? EvaluateOnce(ConditionalType conditional, out InstantiatedType? tailCall)
    {
        tailCall = null;
        var subject = TypeSimplifier.Simplify(conditional.Subject);
        if (TypeMatcher.IsUnresolved(subject))
            return null;

        if (conditional.Distributes && Distribute(conditional, subject, out var distributed))
            return distributed;

        foreach (var arm in conditional.Arms)
        {
            var bindings = new TypeParameterSubstitution();
            if (!TypeMatcher.TryMatch(subject, arm.Pattern, arm.Binders, bindings))
                continue;

            // A binder inside a part of the pattern that was answered by assignability rather than walked
            // into never got bound. Nothing is known about it, which is exactly what 'unknown' says - and
            // leaving it free would put a parameter of the pattern into the type the arm hands back.
            foreach (var binder in arm.Binders)
                bindings.TryAdd(binder, PrimitiveType.Unknown);

            var result = TypeSubstitution.Apply(arm.Result, bindings);
            if (result is not InstantiatedType { GenericType.UnderlyingType: ConditionalType } instantiated)
                return result;

            tailCall = instantiated;
            return null;
        }

        // Nothing matched. 'never' rather than a diagnostic: an arm list with no catch-all is a narrowing,
        // and the caller finds out by way of the type it gets back not being usable.
        return PrimitiveType.Never;
    }

    /// <summary>
    ///     Runs the arms once per member of a union - <c>match each</c>. An <see cref="Types.OptionalType" />
    ///     is a union with <c>none</c>, so <c>T?</c> splits the same way any other union does, which is what
    ///     makes a NonNullable out of the same three lines every other filter is written in.
    /// </summary>
    /// <returns>Whether the subject was a union at all; a subject that is not one falls through to the arms.</returns>
    private static bool Distribute(ConditionalType conditional, Type subject, out Type? distributed)
    {
        distributed = null;

        // 'never' is the union with no members, so there is nothing to run the arms against and nothing to
        // union back up. Falling through to the arms instead would match the first of them, since 'never' is
        // assignable to everything.
        if (Type.IsNever(subject))
        {
            distributed = PrimitiveType.Never;
            return true;
        }

        if (TypeSimplifier.Expanded(subject) is not UnionType union)
            return false;

        var results = new List<Type>();
        foreach (var member in union.Types)
        {
            var evaluated = TryEvaluate(new ConditionalType(member, conditional.Arms, false));
            if (evaluated == null)
                return true;

            results.Add(evaluated);
        }

        distributed = TypeSimplifier.Simplify(new UnionType(results));
        return true;
    }

    /// <summary>
    ///     The conditional <paramref name="instantiated" /> stands for, with the alias's parameters bound to
    ///     its arguments - one step of an <c>Awaited&lt;V&gt;</c>-shaped tail call.
    /// </summary>
    private static ConditionalType ConditionalBodyOf(InstantiatedType instantiated)
    {
        var substitution = new TypeParameterSubstitution();
        var parameters = instantiated.GenericType.Parameters;
        for (var i = 0; i < parameters.Count; i++)
            substitution[parameters[i]] = instantiated.Arguments.ElementAtOrDefault(i) ?? parameters[i].DefaultType ?? PrimitiveType.Unknown;

        return TypeSubstitution.SubstituteArms((ConditionalType)instantiated.GenericType.UnderlyingType, substitution);
    }
}
