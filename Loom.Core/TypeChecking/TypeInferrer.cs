using Loom.Core.Parsing.AST;
using Loom.Core.TypeChecking.Types;
using ArrayType = Loom.Core.TypeChecking.Types.ArrayType;
using FunctionType = Loom.Core.TypeChecking.Types.FunctionType;
using IntersectionType = Loom.Core.TypeChecking.Types.IntersectionType;
using OptionalType = Loom.Core.TypeChecking.Types.OptionalType;
using PrimitiveType = Loom.Core.TypeChecking.Types.PrimitiveType;
using Type = Loom.Core.TypeChecking.Types.Type;
using TypeParameter = Loom.Core.TypeChecking.Types.TypeParameter;
using UnionType = Loom.Core.TypeChecking.Types.UnionType;

namespace Loom.Core.TypeChecking;

public sealed class TypeInferrer(Func<Node, Type> getType)
{
    public TypeParameterSubstitution InferInterfaceTypeArguments(InterfaceInvocation node, GenericType generic, InterfaceType underlying)
    {
        var objectType = underlying.ObjectType;
        var pairs = new List<(Type parameterType, Type argumentType)>();
        foreach (var initializer in node.Body.Initializers)
        {
            switch (initializer)
            {
                case PropertyInitializer propInit:
                {
                    var prop = objectType.GetProperty(propInit.Name.Text);
                    if (prop == null) continue;
                    var argType = getType(propInit.Expression);
                    pairs.Add((prop.ValueType, argType));
                    break;
                }
                case ShorthandPropertyInitializer shorthandPropInit:
                {
                    var prop = objectType.GetProperty(shorthandPropInit.Identifier.Name.Text);
                    if (prop == null) continue;
                    var argType = getType(shorthandPropInit.Identifier);
                    pairs.Add((prop.ValueType, argType));
                    break;
                }
                case IndexInitializer when objectType.Indexer == null: continue;
                case IndexInitializer idxInit:
                {
                    var keyArg = getType(idxInit.IndexExpression);
                    var valueArg = getType(idxInit.Expression);
                    pairs.Add((objectType.Indexer.KeyType, keyArg));
                    pairs.Add((objectType.Indexer.ValueType, valueArg));
                    break;
                }
            }
        }

        var inferred = new TypeParameterSubstitution();
        var visited = new HashSet<(Type, Type)>();
        foreach (var (parameterType, argumentType) in pairs)
            TryInferTypes(parameterType, argumentType, inferred, visited);

        var substitution = new TypeParameterSubstitution();
        foreach (var typeParameter in generic.Parameters)
        {
            substitution[typeParameter] = inferred.TryGetValue(typeParameter, out var inferredType)
                ? inferredType
                : typeParameter.DefaultType ?? PrimitiveType.Unknown;
        }

        return substitution;
    }

    public static TypeParameterSubstitution InferFunctionTypeArguments(
        FunctionType functionType,
        List<Type> argumentTypes,
        Type? contextualType = null)
    {
        var inferred = new TypeParameterSubstitution();
        var visited = new HashSet<(Type, Type)>();
        if (contextualType != null)
            TryInferTypes(functionType.ReturnType, contextualType, inferred, visited);

        for (var i = 0; i < Math.Min(functionType.ParameterTypes.Count, argumentTypes.Count); i++)
            TryInferTypes(functionType.ParameterTypes[i], argumentTypes[i], inferred, visited);

        var substitution = new TypeParameterSubstitution();
        foreach (var typeParameter in functionType.TypeParameters)
        {
            substitution[typeParameter] = inferred.TryGetValue(typeParameter, out var inferredType)
                ? inferredType
                : typeParameter.DefaultType ?? PrimitiveType.Unknown;
        }

        return substitution;
    }

    private static bool TryInferTypes(Type parameterType, Type argumentType, TypeParameterSubstitution inferredTypes, HashSet<(Type, Type)> visitedPairs)
    {
        parameterType = ExpandAliases(parameterType);
        argumentType = ExpandAliases(argumentType);

        if (!visitedPairs.Add((parameterType, argumentType)))
            return true;

        if (TryMatchGenericTypes(parameterType, argumentType, inferredTypes, visitedPairs, out var genericResult))
            return genericResult;

        return (parameterType, argumentType) switch
        {
            (TypeParameter typeParameter, _) => BindTypeParameter(typeParameter, argumentType, inferredTypes),
            (ArrayType parameterArray, ArrayType argumentArray) => TryInferTypes(
                parameterArray.ElementType,
                argumentArray.ElementType,
                inferredTypes,
                visitedPairs
            ),
            (OptionalType parameterOptional, OptionalType argumentOptional) => TryInferTypes(
                parameterOptional.NonNullableType,
                argumentOptional.NonNullableType,
                inferredTypes,
                visitedPairs
            ),
            (OptionalType parameterOptional, _) => TryInferTypes(parameterOptional.NonNullableType, argumentType, inferredTypes, visitedPairs),
            (ObjectType parameterObject, ObjectType argumentObject) => MatchObjectTypes(parameterObject, argumentObject, inferredTypes, visitedPairs),
            (InterfaceType parameterInterface, InterfaceType argumentInterface) => TryInferTypes(
                parameterInterface.ObjectType,
                argumentInterface.ObjectType,
                inferredTypes,
                visitedPairs
            ),
            (ObjectType parameterObject, UnionType argumentUnion) => TryInferFromObjectUnion(parameterObject, argumentUnion, inferredTypes, visitedPairs),
            (InterfaceType parameterInterface, UnionType argumentUnion) => TryInferFromObjectUnion(
                parameterInterface.ObjectType,
                argumentUnion,
                inferredTypes,
                visitedPairs
            ),
            (FunctionType parameterFunction, FunctionType argumentFunction) => MatchFunctionTypes(
                parameterFunction,
                argumentFunction,
                inferredTypes,
                visitedPairs
            ),
            (UnionType parameterUnion, UnionType argumentUnion) when parameterUnion.Types.Count == argumentUnion.Types.Count => MatchUnionTypes(
                parameterUnion,
                argumentUnion,
                inferredTypes,
                visitedPairs
            ),
            (UnionType parameterUnion, _) => TryInferFromUnion(parameterUnion, argumentType, inferredTypes),
            (IntersectionType parameterIntersection, IntersectionType argumentIntersection) when parameterIntersection.Types.Count
                == argumentIntersection.Types.Count => MatchIntersectionTypes(parameterIntersection, argumentIntersection, inferredTypes, visitedPairs),
            (IntersectionType parameterIntersection, _) => TryInferFromIntersection(parameterIntersection, argumentType, inferredTypes),
            _ => parameterType.Equals(argumentType) || argumentType.IsAssignableTo(parameterType)
        };
    }

    private static bool TryInferFromUnion(UnionType union, Type argumentType, TypeParameterSubstitution inferredTypes)
    {
        if (argumentType is UnionType)
            return false;

        var typeParams = union.Types.OfType<TypeParameter>().ToList();
        return typeParams.Count == 1 && BindTypeParameter(typeParams[0], argumentType, inferredTypes);
    }

    /// <summary>
    /// Handles unifying a generic object/interface shape (e.g. an array element type `Entry&lt;K, V&gt;`)
    /// against a union of structurally compatible object/interface arguments (e.g. the inferred element
    /// type of `[new Entry {...}, new Entry {...}]`), which none of the other cases above cover since
    /// they either require both sides to be unions of equal length or the parameter side to be the union.
    /// Distributes property-by-property: a readonly property is covariant, so its type parameter is
    /// unified against the union of that property's value type across every member; a mutable property is
    /// invariant, so every member must already agree on its exact value type before unification proceeds.
    /// </summary>
    private static bool TryInferFromObjectUnion(
        ObjectType parameterObject,
        UnionType argumentUnion,
        TypeParameterSubstitution inferredTypes,
        HashSet<(Type, Type)> visitedPairs)
    {
        var memberObjects = new List<ObjectType>();
        foreach (var member in argumentUnion.Types)
        {
            switch (member)
            {
                case ObjectType objectType:
                    memberObjects.Add(objectType);
                    break;
                case InterfaceType interfaceType:
                    memberObjects.Add(interfaceType.ObjectType);
                    break;
                default:
                    return false;
            }
        }

        foreach (var property in parameterObject.Properties)
        {
            var memberValueTypes = new List<Type>();
            foreach (var member in memberObjects)
            {
                var memberProperty = member.GetProperty(property.Name);
                if (memberProperty == null)
                    return false;

                memberValueTypes.Add(memberProperty.ValueType);
            }

            if (property.IsMutable)
            {
                if (memberValueTypes.Distinct().Count() > 1)
                    return false;

                if (!TryInferTypes(property.ValueType, memberValueTypes[0], inferredTypes, visitedPairs))
                    return false;

                continue;
            }

            var combinedValueType = TypeSimplifier.Simplify(new UnionType(memberValueTypes));
            if (!TryInferTypes(property.ValueType, combinedValueType, inferredTypes, visitedPairs))
                return false;
        }

        return true;
    }

    private static bool TryInferFromIntersection(IntersectionType union, Type argumentType, TypeParameterSubstitution inferredTypes)
    {
        if (argumentType is IntersectionType)
            return false;

        var typeParams = union.Types.OfType<TypeParameter>().ToList();
        return typeParams.Count == 1 && BindTypeParameter(typeParams[0], argumentType, inferredTypes);
    }

    private static bool BindTypeParameter(TypeParameter typeParameter, Type argumentType, TypeParameterSubstitution inferredTypes)
    {
        if (inferredTypes.TryGetValue(typeParameter, out var existingType))
        {
            if (existingType.Equals(argumentType))
                return true;

            var widenedExisting = existingType.Widen();
            var widenedArgument = argumentType.Widen();
            if (!widenedExisting.Equals(widenedArgument))
                return true;

            inferredTypes[typeParameter] = widenedExisting;
            return true;
        }

        inferredTypes[typeParameter] = argumentType;
        return true;
    }

    private static bool MatchObjectTypes(
        ObjectType parameterObject,
        ObjectType argumentObject,
        TypeParameterSubstitution inferredTypes,
        HashSet<(Type, Type)> visitedPairs)
    {
        foreach (var parameterProperty in parameterObject.Properties)
        {
            var argumentProperty = argumentObject.GetProperty(parameterProperty.Name);
            if (argumentProperty == null)
                return false;

            if (!TryInferTypes(parameterProperty.ValueType, argumentProperty.ValueType, inferredTypes, visitedPairs))
                return false;
        }

        if (parameterObject.Indexer != null && argumentObject.Indexer != null)
        {
            return TryInferTypes(parameterObject.Indexer.KeyType, argumentObject.Indexer.KeyType, inferredTypes, visitedPairs)
                && TryInferTypes(parameterObject.Indexer.ValueType, argumentObject.Indexer.ValueType, inferredTypes, visitedPairs);
        }

        return parameterObject.Indexer == null;
    }

    private static bool MatchFunctionTypes(
        FunctionType parameterFunction,
        FunctionType argumentFunction,
        TypeParameterSubstitution inferredTypes,
        HashSet<(Type, Type)> visitedPairs)
    {
        if (parameterFunction.ParameterTypes.Count != argumentFunction.ParameterTypes.Count)
            return false;

        // The argument is itself generic with its own type parameters (distinct from
        // parameterFunction's, e.g. passing `id<T>(n: T): T` where a plain `fn(e): U` is
        // expected) - matching its raw, uninstantiated shape positionally would pollute
        // inferredTypes with bindings to the argument's own free type parameters instead of
        // real information. Specialize it first using whatever's already been inferred for
        // parameterFunction's own type parameters so far, then match against that instead.
        if (argumentFunction.TypeParameters.Count > 0 && argumentFunction.TypeParameters.Count != parameterFunction.TypeParameters.Count)
        {
            var resolvedParameterTypes = parameterFunction.ParameterTypes.ConvertAll(t => Substitute(t, inferredTypes));
            var argumentSubstitution = InferFunctionTypeArguments(argumentFunction, resolvedParameterTypes);
            argumentFunction = new FunctionType(
                [],
                argumentFunction.ParameterTypes.ConvertAll(t => Substitute(t, argumentSubstitution)),
                Substitute(argumentFunction.ReturnType, argumentSubstitution),
                argumentFunction.HasRestParameter
            );
        }

        return !parameterFunction.ParameterTypes.Where((t, index) => !TryInferTypes(t, argumentFunction.ParameterTypes[index], inferredTypes, visitedPairs)).Any()
            && TryInferTypes(parameterFunction.ReturnType, argumentFunction.ReturnType, inferredTypes, visitedPairs);
    }

    private static Type Substitute(Type type, TypeParameterSubstitution substitution) =>
        type switch
        {
            TypeParameter typeParameter => substitution.TryGetValue(typeParameter, out var substituted) ? substituted : type,
            ArrayType arrayType => new ArrayType(Substitute(arrayType.ElementType, substitution), arrayType.IsMutable),
            OptionalType optionalType => new OptionalType(Substitute(optionalType.NonNullableType, substitution)),
            UnionType unionType => new UnionType(unionType.Types.ConvertAll(t => Substitute(t, substitution))),
            IntersectionType intersectionType => new IntersectionType(intersectionType.Types.ConvertAll(t => Substitute(t, substitution))),
            FunctionType functionType => new FunctionType(
                functionType.TypeParameters,
                functionType.ParameterTypes.ConvertAll(t => Substitute(t, substitution)),
                Substitute(functionType.ReturnType, substitution),
                functionType.HasRestParameter
            ),
            _ => type
        };

    private static bool MatchUnionTypes(
        UnionType parameterUnion,
        UnionType argumentUnion,
        TypeParameterSubstitution inferredTypes,
        HashSet<(Type, Type)> visitedPairs) =>
        !parameterUnion.Types.Where((t, index) => !TryInferTypes(t, argumentUnion.Types[index], inferredTypes, visitedPairs)).Any();

    private static bool MatchIntersectionTypes(
        IntersectionType parameterIntersection,
        IntersectionType argumentIntersection,
        TypeParameterSubstitution inferredTypes,
        HashSet<(Type, Type)> visitedPairs) =>
        !parameterIntersection.Types.Where((t, index) => !TryInferTypes(t, argumentIntersection.Types[index], inferredTypes, visitedPairs)).Any();

    private static bool TryMatchGenericTypes(
        Type parameterType,
        Type argumentType,
        TypeParameterSubstitution inferredTypes,
        HashSet<(Type, Type)> visitedPairs,
        out bool result)
    {
        result = false;
        var parameterGenericInfo = GetGenericTypeAndArguments(parameterType);
        var argumentGenericInfo = GetGenericTypeAndArguments(argumentType);
        if (parameterGenericInfo.Generic == null || argumentGenericInfo.Generic == null)
            return false;

        if (!parameterGenericInfo.Generic.Declaration.Equals(argumentGenericInfo.Generic.Declaration))
            return false;

        switch (parameterGenericInfo.Arguments.Count)
        {
            case > 0 when argumentGenericInfo.Arguments.Count > 0:
            {
                for (var index = 0; index < Math.Min(parameterGenericInfo.Arguments.Count, argumentGenericInfo.Arguments.Count); index++)
                {
                    if (!TryInferTypes(parameterGenericInfo.Arguments[index], argumentGenericInfo.Arguments[index], inferredTypes, visitedPairs))
                        return false;
                }

                break;
            }
            case > 0 when argumentGenericInfo.Arguments.Count == 0:
            {
                for (var index = 0; index < Math.Min(parameterGenericInfo.Arguments.Count, argumentGenericInfo.Generic.Parameters.Count); index++)
                {
                    if (!TryInferTypes(argumentGenericInfo.Generic.Parameters[index], parameterGenericInfo.Arguments[index], inferredTypes, visitedPairs))
                        return false;
                }

                break;
            }
            case 0 when argumentGenericInfo.Arguments.Count > 0:
            {
                for (var index = 0; index < Math.Min(parameterGenericInfo.Generic.Parameters.Count, argumentGenericInfo.Arguments.Count); index++)
                {
                    if (!TryInferTypes(parameterGenericInfo.Generic.Parameters[index], argumentGenericInfo.Arguments[index], inferredTypes, visitedPairs))
                        return false;
                }

                break;
            }
        }

        result = true;
        return true;
    }

    private static Type ExpandAliases(Type type) =>
        TypeSolver.Transform(
            type,
            candidateType => candidateType is InstantiatedType { GenericType.Declaration: TypeAlias or InterfaceDeclaration } instantiated
                ? instantiated.Expand()
                : candidateType
        );

    private static (GenericType? Generic, List<Type> Arguments) GetGenericTypeAndArguments(Type type) =>
        type switch
        {
            InstantiatedType instantiated => (instantiated.GenericType, instantiated.Arguments),
            GenericType generic => (generic, []),
            _ => (null, [])
        };
}