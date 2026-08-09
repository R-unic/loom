namespace Loom.Core.TypeChecking.Types;

public sealed class FunctionType(List<TypeParameter> typeParameters, List<Type> parameterTypes, Type returnType, bool hasRestParameter = false) : Type
{
    public List<TypeParameter> TypeParameters { get; } = typeParameters;
    public List<Type> ParameterTypes { get; } = parameterTypes;
    public bool HasRestParameter { get; } = hasRestParameter;
    public List<Type> RequiredParameterTypes { get; } = GetRequiredParameterTypes(parameterTypes, hasRestParameter);
    public Type ReturnType { get; } = returnType;

    private static List<Type> GetRequiredParameterTypes(List<Type> parameterTypes, bool hasRestParameter)
    {
        var fixedParameterTypes = hasRestParameter ? parameterTypes.Take(parameterTypes.Count - 1).ToList() : parameterTypes;
        var cutoffIndex = fixedParameterTypes.Count;
        for (var i = fixedParameterTypes.Count - 1; i >= 0; i--)
        {
            if (!IsNotOptional(fixedParameterTypes[i])) continue;

            cutoffIndex = i + 1;
            break;
        }

        return fixedParameterTypes.Take(cutoffIndex).ToList();
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(TypeParameters.Count);
        hash.Add(GetTypeListHash(TypeParameters));
        hash.Add(ParameterTypes.Count);
        hash.Add(GetTypeListHash(ParameterTypes));
        hash.Add(HasRestParameter);
        hash.Add(ReturnType);
        return hash.ToHashCode();
    }

    public override bool Equals(Type? other) =>
        other is FunctionType functionType
        && HasRestParameter == functionType.HasRestParameter
        && ListEquals(TypeParameters, functionType.TypeParameters)
        && ListEquals(RequiredParameterTypes, functionType.RequiredParameterTypes)
        && ReturnType.Equals(functionType.ReturnType);

    public override bool IsAssignableTo(Type other)
    {
        if (base.IsAssignableTo(other))
            return true;

        if (other is not FunctionType functionType
            || ParameterTypes.Count > functionType.ParameterTypes.Count
            || TypeParameters.Count != functionType.TypeParameters.Count
            || HasRestParameter != functionType.HasRestParameter)
            return false;

        if (TypeParameters
            .Where((t, i) => functionType.TypeParameters[i].Constraint is { } constraint
                && !(t.Constraint ?? PrimitiveType.Never).IsAssignableTo(constraint)
            )
            .Any())
            return false;

        return !ParameterTypes.Where((t, i) => !functionType.ParameterTypes[i].IsAssignableTo(t)).Any()
            && ReturnType.IsAssignableTo(functionType.ReturnType);
    }

    public override string ToString()
    {
        var parameters = ParameterTypes.Select((t, i) => HasRestParameter && i == ParameterTypes.Count - 1 ? $"..{t}" : t.ToString());
        return $"fn{(TypeParameters.Count != 0 ? $"<{string.Join(", ", TypeParameters)}>" : "")}({string.Join(", ", parameters)}): {ReturnType}";
    }
}