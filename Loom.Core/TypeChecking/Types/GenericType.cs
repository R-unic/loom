using System.Runtime.CompilerServices;
using Loom.Core.Parsing.AST;

namespace Loom.Core.TypeChecking.Types;

public sealed class GenericType(GenericNamedDeclaration declaration, List<TypeParameter> parameters, Type underlyingType) : Type
{
    /// <summary>
    ///     Every <see cref="InstantiatedType" /> built from this definition, bucketed by its first type
    ///     argument. See <see cref="Construct" /> for what it is for and why it is keyed the way it is.
    /// </summary>
    private readonly ConditionalWeakTable<Type, List<InstantiatedType>> _instantiations = [];
    private InstantiatedType? _uninstantiated;
    private List<Type>? _defaultArguments;

    public GenericNamedDeclaration Declaration { get; } = declaration;
    public List<TypeParameter> Parameters { get; } = parameters;
    /// <summary>
    ///     Settable so a self-referential alias can be published before its body is known. Binding the
    ///     GenericType first and filling this in afterwards is what lets 'type R = A | B' be referenced
    ///     from inside A and B while it is still being resolved - the reference sees this instance and
    ///     observes the finished body once it lands, rather than an unbound variable that defaults to
    ///     'never'.
    /// </summary>
    public Type UnderlyingType { get; internal set; } = underlyingType;

    /// <summary>
    ///     The one <see cref="InstantiatedType" /> for this definition applied to <paramref name="arguments" />,
    ///     created on first ask and reused for every later ask with the same arguments. This is the only way to
    ///     build an instantiation, and it is what keeps the type graph finite.
    ///     <para>
    ///         A generic whose members name itself - <c>interface Bag&lt;T&gt; { merge: fn(other: Bag&lt;T&gt;):
    ///         Bag&lt;T&gt; }</c> - expands to a body that contains <c>Bag&lt;T'&gt;</c> again. Building a fresh
    ///         object there makes expansion an infinite unrolling: <c>Bag&lt;number&gt;</c> #1 holds #2 holds #3,
    ///         forever. Every cycle guard in the type system keys on reference identity, so none of them ever
    ///         sees the same pair twice and the walk runs until the stack does.
    ///     </para>
    ///     <para>
    ///         Reusing the instance collapses that unrolling into a back-edge: expanding <c>Bag&lt;number&gt;</c>
    ///         yields a body holding <em>that same</em> object. The graph becomes finite and cyclic - the shape
    ///         Roslyn and TypeScript both give constructed generics - <see cref="InstantiatedType.Expand" />'s
    ///         cache terminates the walk, and the reference-identity guards start working as designed.
    ///     </para>
    ///     <para>
    ///         Arguments are matched by <em>reference</em>, not by <see cref="Type.Equals(Type)" />, for two reasons.
    ///         Structural matching would fuse two interfaces that merely look alike into one instantiation, and
    ///         since intrinsic definitions are cached for the process (see <see cref="Intrinsic.Intrinsics" />) that fusion
    ///         would reach across compilations and hand one project's type to another. It also keeps the table
    ///         from outliving what it describes: bucketing on the first argument in a
    ///         <see cref="ConditionalWeakTable{TKey,TValue}" /> lets a compilation's entries die with the types they name,
    ///         which a strong dictionary hanging off a process-lifetime definition would not.
    ///     </para>
    /// </summary>
    public InstantiatedType Construct(List<Type> arguments)
    {
        if (arguments.Count == 0)
            return _uninstantiated ??= new InstantiatedType(this, arguments);

        var bucket = _instantiations.GetOrCreateValue(arguments[0]);
        lock (bucket)
        {
            foreach (var candidate in bucket)
                if (SameArguments(candidate.Arguments, arguments))
                    return candidate;

            // Published before it is handed back, so the substitution walk Expand() runs finds this entry
            // when it reaches the self-reference instead of starting another instantiation.
            var instantiated = new InstantiatedType(this, arguments);
            bucket.Add(instantiated);
            return instantiated;
        }
    }

    /// <summary>
    ///     Each parameter's own declared default (or <see cref="PrimitiveType.Unknown" /> where it has none) -
    ///     the argument list a bare, uninstantiated use of this definition instantiates against. Fixed for
    ///     the life of the definition, so it is computed once and reused rather than rebuilt on every bare
    ///     access.
    /// </summary>
    public List<Type> GetDefaultArguments() =>
        _defaultArguments ??= Parameters.ConvertAll(p => p.DefaultType ?? PrimitiveType.Unknown);

    private static bool SameArguments(List<Type> arguments, List<Type> otherArguments)
    {
        if (arguments.Count != otherArguments.Count)
            return false;

        for (var i = 0; i < arguments.Count; i++)
            if (!ReferenceEquals(arguments[i], otherArguments[i]))
                return false;

        return true;
    }

    public override bool Equals(Type? other) =>
        GuardedEquals(
            this,
            other,
            () => other is GenericType generic
                && Declaration.Id == generic.Declaration.Id
                && ListEquals(Parameters, generic.Parameters)
                && UnderlyingType.Equals(generic.UnderlyingType)
        );

    public override int GetHashCode() => HashCode.Combine(Declaration.Id, Parameters.Count);

    public override string ToString() => $"{Declaration.Name.Text}<{string.Join(", ", Parameters.ConvertAll(p => p.ToString()))}>";
}
