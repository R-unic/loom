namespace Loom.Core.TypeChecking.Types;

/// <summary>
///     <c>keyof(T)</c> where <c>T</c> is not yet known — the counterpart to <see cref="IndexedType" /> for
///     the other half of <c>T[keyof(T)]</c>. Resolved once the generic it belongs to is instantiated.
/// </summary>
/// <remarks>
///     A type parameter is neither an object nor an interface and is not meant to be one yet, so answering
///     "cannot access keys" at the declaration would make every generic written over its own keys wrong
///     before it was ever used.
/// </remarks>
public sealed class KeyOfType(Type target) : Type
{
    public Type Target { get; } = target;

    public override int GetHashCode() => HashCode.Combine(typeof(KeyOfType), Target);
    public override bool Equals(Type? other) => other is KeyOfType keyOfType && Target.Equals(keyOfType.Target);
    public override string ToString() => $"keyof({Target})";
}
