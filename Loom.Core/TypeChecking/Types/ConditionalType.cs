namespace Loom.Core.TypeChecking.Types;

/// <summary>
///     One <c>Pattern -&gt; Result</c> of a <see cref="ConditionalType" />.
/// </summary>
/// <param name="Binders">
///     The <c>let</c> names <see cref="Pattern" /> declares, held by reference so a pattern that also
///     names a type parameter of the enclosing generic can tell the two apart - <see cref="TypeParameter" />
///     equality is deliberately name-blind, so nothing structural could.
/// </param>
public sealed record ConditionalArm(Type Pattern, Type Result, IReadOnlyList<TypeParameter> Binders);

/// <summary>
///     A type-level branch: <c>T is U ? A : B</c>, and the n-armed <c>match T { ... }</c> it is the
///     two-armed case of.
/// </summary>
/// <remarks>
///     <para>
///         Deferred the same way <see cref="KeyOfType" /> and <see cref="IndexedType" /> are - what a
///         parameter stands for is known only once the generic is instantiated, and substitution is where
///         <see cref="ConditionalTypeEvaluator" /> answers it. Answering at the declaration would make
///         every conditional written over its own parameter wrong before it was ever used.
///     </para>
///     <para>
///         <see cref="Distributes" /> is <c>match each</c>: the arms run once per member of a union rather
///         than against the whole type. TypeScript distributes over a naked type parameter silently, which
///         is both what makes <c>Exclude</c> work and what forces the <c>[T] extends [U]</c> tuple-wrapping
///         trick to opt back out, and nobody discovers either.
///     </para>
/// </remarks>
public sealed class ConditionalType(Type subject, List<ConditionalArm> arms, bool distributes) : Type
{
    public Type Subject { get; } = subject;
    public List<ConditionalArm> Arms { get; } = arms;
    public bool Distributes { get; } = distributes;

    public override int GetHashCode() => HashCode.Combine(typeof(ConditionalType), Subject, Arms.Count, Distributes);

    public override bool Equals(Type? other) =>
        GuardedEquals(
            this,
            other,
            () => other is ConditionalType conditional
                && Distributes == conditional.Distributes
                && Subject.Equals(conditional.Subject)
                && SameArms(Arms, conditional.Arms)
        );

    private static bool SameArms(List<ConditionalArm> arms, List<ConditionalArm> otherArms)
    {
        if (arms.Count != otherArms.Count)
            return false;

        for (var i = 0; i < arms.Count; i++)
            if (!arms[i].Pattern.Equals(otherArms[i].Pattern) || !arms[i].Result.Equals(otherArms[i].Result))
                return false;

        return true;
    }

    public override string ToString() =>
        Arms is [{ } thenArm, { Pattern: PrimitiveType { Kind: PrimitiveTypeKind.Unknown } } elseArm] && !Distributes
            ? $"{Subject} is {thenArm.Pattern} ? {thenArm.Result} : {elseArm.Result}"
            : $"match {(Distributes ? "each " : "")}{Subject} {{ {string.Join(", ", Arms.Select(arm => $"{arm.Pattern} -> {arm.Result}"))} }}";
}
