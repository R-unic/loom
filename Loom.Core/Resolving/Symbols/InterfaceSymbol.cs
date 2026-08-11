using Loom.Core.Parsing.AST;

namespace Loom.Core.Resolving.Symbols;

public sealed class InterfaceSymbol(InterfaceDeclaration declaration, string name, bool isSealed, List<InterfaceSymbol>? constraints)
    : TypeSymbol(declaration, name)
{
    public override SymbolKind Kind => SymbolKind.Interface;
    public bool IsSealed { get; } = isSealed;
    public IReadOnlyList<InterfaceSymbol>? Constraints { get; } = constraints;
    public List<PropertySymbol> Properties { get; } = [];
    /// <summary> Current interface properties + all constraint properties </summary>
    public IReadOnlyList<PropertySymbol> FullProperties => field ??= Properties.Concat(GetFieldAndConstraintFields(i => i.Properties)).ToArray();
    public List<TraitSymbol> Implements { get; } = [];
    public IReadOnlyList<Implement> FullImplementations => field ??= Implementations.Concat(GetFieldAndConstraintFields(i => i.Implementations)).ToArray();
    public List<Implement> Implementations { get; } = [];

    /// <summary>Metamethod name (e.g. "__add") to property name, for own properties tagged with [luau_metamethod(...)].</summary>
    public IReadOnlyDictionary<string, string> Metamethods { get; } =
        MetamethodAttributes.Collect(declaration.Body?.Members.OfType<PropertyDeclaration>() ?? [], p => p.Name.Text, p => p.Attributes);

    public PropertySymbol? GetPropertyAtPath(IReadOnlyList<string> path) => GetPropertiesAtPath(path).FirstOrDefault();

    /// <summary>
    ///     Every declaration at <paramref name="path" />, in declaration order. More than one only for an
    ///     overload set, whose shapes the type checker merges into a single intersection-typed property - the
    ///     names its parameters were declared under survive only here.
    /// </summary>
    public IReadOnlyList<PropertySymbol> GetPropertiesAtPath(IReadOnlyList<string> path)
    {
        if (path.Count == 0)
            return [];

        var firstName = path[0];
        var properties = FullProperties.Where(p => p.Name == firstName).ToArray();
        return properties is [{ PointsTo: { } pointsTo }, ..] && path.Count > 1 ? pointsTo.GetPropertiesAtPath(path.Skip(1).ToArray()) : properties;
    }

    public override string ToString() =>
        $"InterfaceSymbol({Name}, IsSealed: {IsSealed}, Properties: [{string.Join(", ", Properties.Select(s => s.Name))}] Implements: [{string.Join(", ", Implements.Select(s => s.Name))}], Constraints: [{string.Join(", ", Constraints?.Select(s => s.Name) ?? [])}])";

    private T[] GetFieldAndConstraintFields<T>(Func<InterfaceSymbol, IReadOnlyList<T>> selector) =>
        Constraints?.SelectMany(c => selector(c).Concat(c.GetFieldAndConstraintFields(selector))).ToArray() ?? [];
}