using Loom.Core.Resolving;

namespace Loom.Core.FlowAnalysis;

public sealed record FlowAddress(Symbol? Symbol, FlowAddress? Parent, string? FieldName, object? ElementIndex)
{
    public bool Equals(FlowAddress? other) =>
        ReferenceEquals(this, other)
        || other != null
        && Equals(Symbol, other.Symbol)
        && Equals(Parent, other.Parent)
        && FieldName == other.FieldName
        && Equals(ElementIndex, other.ElementIndex);

    public static FlowAddress Variable(Symbol symbol) => new(symbol, null, null, null);
    public static FlowAddress Field(FlowAddress @base, string name) => new(null, @base, name, null);
    public static FlowAddress Element(FlowAddress @base, object constantIndex) => new(null, @base, null, constantIndex);

    public override int GetHashCode() => HashCode.Combine(Symbol, Parent, FieldName, ElementIndex);
}