using Loom.Core.TypeChecking.Types;
using ArrayType = Loom.Core.TypeChecking.Types.ArrayType;
using FunctionType = Loom.Core.TypeChecking.Types.FunctionType;
using IntersectionType = Loom.Core.TypeChecking.Types.IntersectionType;
using OptionalType = Loom.Core.TypeChecking.Types.OptionalType;
using Type = Loom.Core.TypeChecking.Types.Type;
using UnionType = Loom.Core.TypeChecking.Types.UnionType;

namespace Loom.Core.TypeChecking;

/// <summary>
///     Infers each type parameter's variance from how it's used within a generic declaration's body.
///     Nested generic instantiations (e.g. a type parameter passed as an argument to another generic)
///     are currently treated as invariant occurrences rather than being recursively analyzed.
///     Subject to change.
/// </summary>
public static class VarianceInferrer
{
    public static GenericType ApplyInferredVariance(GenericType generic)
    {
        if (generic.Parameters.Count == 0)
            return generic;

        var positions = generic.Parameters.Distinct().ToDictionary(p => p, _ => (Variance?)null);
        Walk(generic.UnderlyingType, Variance.Covariant, positions, []);

        var newParameters = generic.Parameters.ConvertAll(p => p.WithVariance(positions[p] ?? Variance.Invariant));
        return new GenericType(generic.Declaration, newParameters, generic.UnderlyingType);
    }

    private static void Walk(Type type, Variance position, Dictionary<TypeParameter, Variance?> positions, HashSet<Type> visiting)
    {
        if (!visiting.Add(type)) return;

        try
        {
            switch (type)
            {
                case TypeParameter typeParameter when positions.ContainsKey(typeParameter):
                    Record(positions, typeParameter, position);
                    break;
                case ArrayType arrayType:
                    Walk(arrayType.ElementType, arrayType.IsMutable ? Variance.Invariant : position, positions, visiting);
                    break;
                case OptionalType optionalType:
                    Walk(optionalType.NonNullableType, position, positions, visiting);
                    break;
                case UnionType unionType:
                    foreach (var member in unionType.Types)
                        Walk(member, position, positions, visiting);

                    break;
                case IntersectionType intersectionType:
                    foreach (var member in intersectionType.Types)
                        Walk(member, position, positions, visiting);

                    break;
                case FunctionType functionType:
                    foreach (var parameterType in functionType.ParameterTypes)
                        Walk(parameterType, Flip(position), positions, visiting);

                    Walk(functionType.ReturnType, position, positions, visiting);
                    break;
                case InterfaceType interfaceType:
                    Walk(interfaceType.ObjectType, position, positions, visiting);
                    foreach (var constraint in interfaceType.Constraints)
                        Walk(constraint, position, positions, visiting);

                    break;
                case ObjectType objectType:
                    foreach (var property in objectType.Properties)
                        Walk(property.ValueType, property.IsMutable ? Variance.Invariant : position, positions, visiting);

                    if (objectType.Indexer != null)
                    {
                        Walk(objectType.Indexer.KeyType, Variance.Invariant, positions, visiting);
                        Walk(objectType.Indexer.ValueType, objectType.Indexer.IsMutable ? Variance.Invariant : position, positions, visiting);
                    }

                    break;
                case InstantiatedType instantiatedType:
                    foreach (var argument in instantiatedType.Arguments)
                        Walk(argument, Variance.Invariant, positions, visiting);

                    break;
            }
        }
        finally
        {
            visiting.Remove(type);
        }
    }

    private static void Record(Dictionary<TypeParameter, Variance?> positions, TypeParameter typeParameter, Variance position)
    {
        var existing = positions[typeParameter];
        positions[typeParameter] = existing == null || existing == position ? position : Variance.Invariant;
    }

    private static Variance Flip(Variance variance) =>
        variance switch
        {
            Variance.Covariant => Variance.Contravariant,
            Variance.Contravariant => Variance.Covariant,
            _ => Variance.Invariant
        };
}