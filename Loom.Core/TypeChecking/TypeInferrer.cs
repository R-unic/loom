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
    public TypeParameterSubstitution InferInterfaceTypeArguments(InterfaceInvocation node, GenericType generic, InterfaceType underlying, Type? expected = null)
    {
        var objectType = underlying.ObjectType;
        var pairs = new List<(Type parameterType, Type argumentType)>();
        foreach (var initializer in node.Body.Initializers)
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

        var inferred = new TypeParameterSubstitution();
        var visited = new HashSet<(Type, Type)>();

        // Seed from the surrounding expected type (e.g. `let b: Box<number> = new Box { value: [] }`)
        // before the bottom-up pass below, so an initializer value that's contextually ambiguous on its
        // own (like an empty array literal) doesn't lock a type parameter to a useless inferred type
        // (e.g. `never`) that the bottom-up pass would otherwise refuse to overwrite once bound.
        if (expected is InstantiatedType instantiatedExpected && instantiatedExpected.GenericType.Equals(generic))
            for (var i = 0; i < generic.Parameters.Count && i < instantiatedExpected.Arguments.Count; i++)
                inferred[generic.Parameters[i]] = instantiatedExpected.Arguments[i];

        foreach (var (parameterType, argumentType) in pairs)
            TryInferTypes(parameterType, argumentType, inferred, visited);

        var substitution = new TypeParameterSubstitution();
        foreach (var typeParameter in generic.Parameters)
            substitution[typeParameter] = inferred.TryGetValue(typeParameter, out var inferredType)
                ? inferredType
                : typeParameter.DefaultType ?? PrimitiveType.Unknown;

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

        // Every argument against the parameter it actually binds to, which past the fixed parameters is the
        // rest parameter's element type rather than the rest parameter itself. Walking the two lists straight
        // down left `fn make<T>(..values: T[])` called as `make(1, 2)` inferring nothing at all: it compared
        // 'T[]' against '1', which matches no inference rule, and 'T' fell back to 'unknown'.
        for (var i = 0; i < argumentTypes.Count; i++)
            if (functionType.ParameterTypeAt(i) is { } parameterType)
                TryInferTypes(parameterType, argumentTypes[i], inferred, visited);

        var substitution = new TypeParameterSubstitution();
        foreach (var typeParameter in functionType.TypeParameters)
            substitution[typeParameter] = inferred.TryGetValue(typeParameter, out var inferredType)
                ? inferredType
                : typeParameter.DefaultType ?? PrimitiveType.Unknown;

        return substitution;
    }

    private static bool TryInferTypes(Type parameterType, Type argumentType, TypeParameterSubstitution inferredTypes, HashSet<(Type, Type)> visitedPairs)
    {
        parameterType = ExpandAliases(parameterType);
        argumentType = ExpandAliases(argumentType);

        if (!visitedPairs.Add((parameterType, argumentType)))
            return true;

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
    ///     Handles unifying a generic object/interface shape (e.g. an array element type `Entry&lt;K, V&gt;`)
    ///     against a union of structurally compatible object/interface arguments (e.g. the inferred element
    ///     type of `[new Entry {...}, new Entry {...}]`), which none of the other cases above cover since
    ///     they either require both sides to be unions of equal length or the parameter side to be the union.
    ///     Distributes property-by-property: a readonly property is covariant, so its type parameter is
    ///     unified against the union of that property's value type across every member; a mutable property is
    ///     invariant, so every member must already agree on its exact value type before unification proceeds.
    /// </summary>
    private static bool TryInferFromObjectUnion(
        ObjectType parameterObject,
        UnionType argumentUnion,
        TypeParameterSubstitution inferredTypes,
        HashSet<(Type, Type)> visitedPairs)
    {
        var memberObjects = new List<ObjectType>();
        foreach (var member in argumentUnion.Types)
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
            return TryInferTypes(parameterObject.Indexer.KeyType, argumentObject.Indexer.KeyType, inferredTypes, visitedPairs)
                && TryInferTypes(parameterObject.Indexer.ValueType, argumentObject.Indexer.ValueType, inferredTypes, visitedPairs);

        return parameterObject.Indexer == null;
    }

    private static bool MatchFunctionTypes(
        FunctionType parameterFunction,
        FunctionType argumentFunction,
        TypeParameterSubstitution inferredTypes,
        HashSet<(Type, Type)> visitedPairs)
    {
        if (argumentFunction.ParameterTypes.Count > parameterFunction.ParameterTypes.Count)
            return false;

        var shared = argumentFunction.ParameterTypes.Count;
        if (argumentFunction.TypeParameters.Count <= 0 || argumentFunction.TypeParameters.Count == parameterFunction.TypeParameters.Count)
            return !parameterFunction.ParameterTypes
                    .Take(shared)
                    .Where((t, index) => !TryInferTypes(t, argumentFunction.ParameterTypes[index], inferredTypes, visitedPairs))
                    .Any()
                && TryInferTypes(parameterFunction.ReturnType, argumentFunction.ReturnType, inferredTypes, visitedPairs);

        var resolvedParameterTypes = parameterFunction.ParameterTypes.ConvertAll(t => Substitute(t, inferredTypes));
        var argumentSubstitution = InferFunctionTypeArguments(argumentFunction, resolvedParameterTypes);
        var substitutedFunction = new FunctionType(
            [],
            argumentFunction.ParameterTypes.ConvertAll(t => Substitute(t, argumentSubstitution)),
            Substitute(argumentFunction.ReturnType, argumentSubstitution),
            argumentFunction.HasRestParameter
        );

        return !parameterFunction.ParameterTypes
                .Take(shared)
                .Where((t, index) => !TryInferTypes(t, substitutedFunction.ParameterTypes[index], inferredTypes, visitedPairs))
                .Any()
            && TryInferTypes(parameterFunction.ReturnType, substitutedFunction.ReturnType, inferredTypes, visitedPairs);
    }

    private static Type Substitute(Type type, TypeParameterSubstitution substitution) =>
        type switch
        {
            TypeParameter typeParameter => substitution.GetValueOrDefault(typeParameter, type),
            OptionalType optionalType => new OptionalType(Substitute(optionalType.NonNullableType, substitution)),
            _ => TypeSolver.Transform(type, t => Substitute(t, substitution), simplify: false)
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

    private static Type ExpandAliases(Type type) =>
        TypeSolver.Transform(
            type,
            candidateType => candidateType is InstantiatedType { GenericType.Declaration: TypeAlias or InterfaceDeclaration } instantiated
                ? instantiated.Expand()
                : candidateType
        );
}