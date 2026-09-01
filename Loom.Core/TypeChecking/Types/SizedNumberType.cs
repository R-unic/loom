using Loom.Core.TypeChecking.Serialization;

namespace Loom.Core.TypeChecking.Types;

/// <summary>
///     A <c>number</c> pinned to a wire width - <c>u8</c>, <c>i16</c>, <c>f32</c>, and so on. Kept at
///     <see cref="PrimitiveTypeKind.Number" /> rather than a kind of its own, the same way
///     <see cref="LiteralType" /> stays whatever kind its value is, so every exhaustive switch over
///     <see cref="PrimitiveTypeKind" /> elsewhere in the type checker and generator needs no new case -
///     Luau has no integer types, so a sized number still renders as plain <c>number</c> in the output.
/// </summary>
/// <remarks>
///     Deliberately does not override <see cref="Type.IsAssignableTo" />: a sized number stays freely,
///     bidirectionally assignable to and from plain <c>number</c> and every other sized number, and
///     participates in arithmetic for free through the inherited <see cref="PrimitiveType.IsAssignableTo" />,
///     which only compares <see cref="PrimitiveType.Kind" />. The width was never a type-safety boundary
///     when it was an attribute - it only mattered to the serializer - so a sized type shouldn't invent
///     narrowing rules nobody asked for.
/// </remarks>
public sealed class SizedNumberType(NumberType numberType) : PrimitiveType(PrimitiveTypeKind.Number)
{
    public NumberType NumberType { get; } = numberType;

    public override int GetHashCode() => HashCode.Combine(typeof(SizedNumberType), Kind, NumberType);
    public override bool Equals(Type? other) => other is SizedNumberType sized && sized.NumberType == NumberType;
    public override string ToString() => NumberType.ToString().ToLower();
}
