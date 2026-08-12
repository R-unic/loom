namespace Loom.Core.TypeChecking.Types;

public sealed class FunctionType(
    List<TypeParameter> typeParameters,
    List<Type> parameterTypes,
    Type returnType,
    bool hasRestParameter = false,
    bool isAsync = false
) : Type
{
    public List<TypeParameter> TypeParameters { get; } = typeParameters;
    public List<Type> ParameterTypes { get; } = parameterTypes;
    public bool HasRestParameter { get; } = hasRestParameter;
    public List<Type> RequiredParameterTypes { get; } = GetRequiredParameterTypes(parameterTypes, hasRestParameter);
    public Type ReturnType { get; } = returnType;

    /// <summary>
    ///     Whether calling this yields a <c>Future&lt;ReturnType&gt;</c> rather than the return type itself.
    ///     <para>
    ///         Part of the type, not an annotation on the declaration, because it changes what a call
    ///         evaluates to: handing a synchronous function to something expecting an asynchronous one would
    ///         give the call site a plain value where it is about to <c>await</c>, and handing an
    ///         asynchronous one the other way would give it a <c>Future</c> it never unwraps. So the two are
    ///         assignable in neither direction - see <see cref="IsAssignableTo" />.
    ///     </para>
    /// </summary>
    public bool IsAsync { get; } = isAsync;

    /// <summary>
    ///     The parameter type the argument at <paramref name="index" /> binds to, or null when there is none -
    ///     an argument past the end of a non-rest parameter list, or one past the end of a tuple rest.
    ///     <para>
    ///         Past the fixed parameters, a rest parameter answers for every remaining argument: an array rest
    ///         with its element type, a tuple rest with the type at that position. Both the type checker (to
    ///         decide what to check an argument against) and <see cref="TypeInferrer" /> (to decide what to
    ///         infer it against) have to agree on this, so it lives here rather than in either of them.
    ///     </para>
    /// </summary>
    public static Type? ParameterTypeAt(List<Type> parameterTypes, bool hasRestParameter, int index)
    {
        var fixedCount = hasRestParameter ? parameterTypes.Count - 1 : parameterTypes.Count;
        if (index < fixedCount)
            return index < parameterTypes.Count ? parameterTypes[index] : null;

        if (!hasRestParameter || parameterTypes.Count == 0)
            return null;

        return parameterTypes[^1] switch
        {
            TupleType restTuple => index - fixedCount < restTuple.ElementTypes.Count ? restTuple.ElementTypes[index - fixedCount] : null,
            ArrayType restArray => restArray.ElementType,
            _ => null
        };
    }

    /// <inheritdoc cref="ParameterTypeAt(List{Type}, bool, int)" />
    public Type? ParameterTypeAt(int index) => ParameterTypeAt(ParameterTypes, HasRestParameter, index);

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
        hash.Add(IsAsync);
        hash.Add(ReturnType);
        return hash.ToHashCode();
    }

    public override bool Equals(Type? other) =>
        other is FunctionType functionType
        && HasRestParameter == functionType.HasRestParameter
        && IsAsync == functionType.IsAsync
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
            || HasRestParameter != functionType.HasRestParameter
            || IsAsync != functionType.IsAsync)
            return false;

        for (var i = 0; i < TypeParameters.Count; i++)
            if (functionType.TypeParameters[i].Constraint is { } constraint
                && !(TypeParameters[i].Constraint ?? PrimitiveType.Never).IsAssignableTo(constraint))
                return false;

        // parameters are contravariant: what the target may be called with has to be something this one accepts
        for (var i = 0; i < ParameterTypes.Count; i++)
            if (!functionType.ParameterTypes[i].IsAssignableTo(ParameterTypes[i]))
                return false;

        return ReturnType.IsAssignableTo(functionType.ReturnType);
    }

    public override string ToString()
    {
        var parameters = ParameterTypes.Select((t, i) => HasRestParameter && i == ParameterTypes.Count - 1 ? $"..{t}" : t.ToString());
        return $"{(IsAsync ? "async " : "")}fn{(TypeParameters.Count != 0 ? $"<{string.Join(", ", TypeParameters)}>" : "")}({string.Join(", ", parameters)}): {ReturnType}";
    }
}