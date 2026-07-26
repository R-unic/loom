using Loom.Core.Parsing.AST;

namespace Loom.Core.Resolving;

public sealed class InterfaceSymbol(InterfaceDeclaration declaration, string name, bool isSealed, List<InterfaceSymbol>? constraints)
    : Symbol(declaration, SymbolKind.Interface, name)
{
    public bool IsSealed { get; } = isSealed;
    public IReadOnlyList<InterfaceSymbol>? Constraints { get; } = constraints;
    public List<PropertySymbol> Properties { get; } = [];
    public List<TraitSymbol> Implements { get; } = [];
    public List<Implement> Implementations { get; } = [];

    public PropertySymbol? GetPropertyAtPath(IReadOnlyList<string> path)
    {
        if (path.Count == 0)
            return null;

        var firstName = path[0];
        var property = Properties.FirstOrDefault(p => p.Name == firstName)
            ?? GetConstraintProperties().FirstOrDefault(p => p.Name == firstName);

        return property is { PointsTo: { } pointsTo } && path.Count > 1
            ? pointsTo.GetPropertyAtPath(path.Skip(1).ToArray())
            : property;
    }

    public override string ToString() =>
        $"InterfaceSymbol({Name}, IsSealed: {IsSealed}, Properties: [{string.Join(", ", Properties.Select(s => s.Name))}] Implements: [{string.Join(", ", Implements.Select(s => s.Name))}], Constraints: [{string.Join(", ", Constraints?.Select(s => s.Name) ?? [])}])";

    private PropertySymbol[] GetConstraintProperties() => Constraints?.SelectMany(c => c.Properties.Concat(c.GetConstraintProperties())).ToArray() ?? [];
}