using Loom.Core.Parsing.AST;

namespace Loom.Core.TypeChecking.Types;

public sealed class GenericType(GenericNamedDeclaration declaration, List<TypeParameter> parameters, Type underlyingType) : Type
{
    public GenericNamedDeclaration Declaration { get; } = declaration;
    public List<TypeParameter> Parameters { get; } = parameters;
    public Type UnderlyingType { get; } = underlyingType;

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