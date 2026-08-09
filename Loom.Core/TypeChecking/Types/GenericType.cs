using Loom.Core.Parsing.AST;

namespace Loom.Core.TypeChecking.Types;

public sealed class GenericType(GenericNamedDeclaration declaration, List<TypeParameter> parameters, Type underlyingType) : Type
{
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