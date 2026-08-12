global using TypeParameterSubstitution = System.Collections.Generic.Dictionary<Loom.Core.TypeChecking.Types.TypeParameter, Loom.Core.TypeChecking.Types.Type>;
using System.Diagnostics.CodeAnalysis;
using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.TypeChecking.Types;
using FunctionType = Loom.Core.TypeChecking.Types.FunctionType;
using IndexedType = Loom.Core.TypeChecking.Types.IndexedType;
using PrimitiveType = Loom.Core.TypeChecking.Types.PrimitiveType;
using Type = Loom.Core.TypeChecking.Types.Type;
using TypeParameter = Loom.Core.TypeChecking.Types.TypeParameter;

namespace Loom.Core.TypeChecking;

public sealed partial class TypeChecker
{
    private TypeParameterSubstitution? ResolveExplicitInterfaceTypeArguments(InterfaceInvocation node, GenericType generic)
    {
        var arguments = node.TypeArguments!.ArgumentsList.ConvertAll(Visit);
        if (!CheckGenericArity(node.TypeArguments, generic.Parameters, arguments, $"Interface '{generic}'"))
            return null;

        var resolved = FillGenericArguments(generic.Parameters, arguments);
        var substitution = new TypeParameterSubstitution();
        for (var i = 0; i < generic.Parameters.Count; i++)
            substitution[generic.Parameters[i]] = resolved[i];

        return substitution;
    }

    private static List<Type> FillGenericArguments(List<TypeParameter> parameters, List<Type> given) =>
        parameters.Select((t, i) => i < given.Count ? given[i] : t.DefaultType ?? PrimitiveType.Unknown).ToList();

    private TypeParameterSubstitution? ResolveTypeArguments(
        Invocation invocation,
        FunctionType functionType,
        List<Type> argumentTypes,
        Type? expectedReturnType)
    {
        var substitution = new TypeParameterSubstitution();
        if (invocation.TypeArguments != null)
        {
            var explicitArguments = invocation.TypeArguments.ArgumentsList.ConvertAll(Visit);
            if (!CheckGenericArity(invocation, functionType.TypeParameters, explicitArguments, "Function"))
                return null;

            for (var i = 0; i < explicitArguments.Count; i++)
                substitution[functionType.TypeParameters[i]] = explicitArguments[i];
        }
        else
        {
            var inferred = TypeInferrer.InferFunctionTypeArguments(functionType, argumentTypes, expectedReturnType);
            foreach (var (tp, type) in inferred)
                substitution[tp] = type;
        }

        foreach (var tp in functionType.TypeParameters)
            if (substitution.TryGetValue(tp, out var substitutedType) && tp.Constraint != null)
                if (!CheckTypeParameterConstraints(invocation, substitutedType, tp))
                    return null;

        return substitution;
    }

    private Type InstantiateGenericType(Node node, TypeArguments? typeArguments, GenericType genericType)
    {
        var arguments = typeArguments?.ArgumentsList.ConvertAll(Visit) ?? [];
        if (!CheckGenericArity(typeArguments ?? node, genericType.Parameters, arguments, $"Type '{genericType}'"))
            return BindType(node, PrimitiveType.Never);

        var fullArguments = FillGenericArguments(genericType.Parameters, arguments);
        for (var i = 0; i < genericType.Parameters.Count; i++)
        {
            var parameter = genericType.Parameters[i];
            var argument = fullArguments[i];
            if (parameter.Constraint == null) continue;
            CheckTypeParameterConstraints(node, argument, parameter);
        }

        var instantiated = genericType.Construct(fullArguments);
        return BindType(node, instantiated);
    }

    private bool CheckTypeParameterConstraints(Node node, Type type, TypeParameter parameter)
    {
        if (parameter.Constraint == null) return true;
        if (type is TypeParameter otherParameter)
            type = otherParameter.Constraint ?? PrimitiveType.Unknown;

        if (type.IsAssignableTo(parameter.Constraint)) return true;

        _diagnostics.Error(
            node,
            InternalCodes.ConstraintViolation,
            $"Type '{type}' does not satisfy constraint '{parameter.Constraint}' for type parameter '{parameter.Name}'."
        );

        return false;
    }

    private bool CheckGenericArity(Node node, List<TypeParameter> parameters, List<Type> arguments, string genericKind)
    {
        var minimum = parameters.Count(p => p.DefaultType == null);
        var maximum = parameters.Count;
        var arityDisplay = minimum == maximum ? minimum.ToString() : $"{minimum}-{maximum}";
        if (arguments.Count >= minimum && arguments.Count <= maximum)
            return true;

        _diagnostics.Error(
            node,
            InternalCodes.GenericArity,
            $"{genericKind} expects {arityDisplay} type argument{(minimum != maximum || maximum != 1 ? "s" : "")}, but {arguments.Count} were provided."
        );

        return false;
    }

    private ObjectType SubstituteObjectType(Node failNode, ObjectType objectType, TypeParameterSubstitution substitution)
    {
        var newProperties = objectType.Properties.ConvertAll(property => new ObjectProperty(
                property.IsMutable,
                property.Name,
                SubstituteTypeParameters(failNode, property.ValueType, substitution)
            )
        );

        ObjectIndexer? newIndexer = null;
        if (objectType.Indexer != null)
            newIndexer = new ObjectIndexer(
                objectType.Indexer.IsMutable,
                SubstituteTypeParameters(failNode, objectType.Indexer.KeyType, substitution),
                SubstituteTypeParameters(failNode, objectType.Indexer.ValueType, substitution)
            );

        return new ObjectType(newIndexer, newProperties);
    }

    private Type SubstituteIndexedType(Node failNode, TypeParameterSubstitution substitution, IndexedType indexedType, Dictionary<Type, Type> cache)
    {
        var target = SubstituteTypeParameters(failNode, indexedType.Target, substitution, cache);
        var index = SubstituteTypeParameters(failNode, indexedType.Index, substitution, cache);
        return GetTypeAtIndex(failNode, target, index);
    }

    private List<Type> SubstituteTypeParameters(Node failNode, List<Type> types, TypeParameterSubstitution substitution) =>
        types.ConvertAll(t => SubstituteTypeParameters(failNode, t, substitution));

    private Type SubstituteTypeParameters(Node failNode, Type type, TypeParameterSubstitution substitution) =>
        SubstituteTypeParameters(
            failNode,
            type,
            substitution,
            new Dictionary<Type, Type>()
        );

    private Type SubstituteTypeParameters(Node failNode, Type type, TypeParameterSubstitution substitution, Dictionary<Type, Type> cache)
    {
        if (cache.TryGetValue(type, out var cached))
            return cached;

        cache[type] = type;
        var substitutedType = TrySubstituteTypeParameter(type, substitution, out var substituted)
            ? substituted
            : type is IndexedType indexedType
                ? SubstituteIndexedType(failNode, substitution, indexedType, cache)
                : TypeSolver.Transform(
                    type,
                    t => t switch
                    {
                        _ when TrySubstituteTypeParameter(t, substitution, out var substituted2) => substituted2,
                        _ => SubstituteTypeParameters(failNode, t, substitution, cache)
                    }
                );

        cache[type] = substitutedType;
        return substitutedType;
    }

    private static bool TrySubstituteTypeParameter(Type type, TypeParameterSubstitution substitution, [MaybeNullWhen(false)] out Type substituted)
    {
        substituted = null;
        return type is TypeParameter tp && substitution.TryGetValue(tp, out substituted);
    }

    /// <summary>
    ///     A generic-valued argument (e.g. passing `id` where `id&lt;T&gt;(n: T): T`) is otherwise
    ///     compared structurally against its expected type with no attempt to specialize it first,
    ///     so a type-parameter-count mismatch (the argument has its own free type parameters, the
    ///     expected shape has none) fails immediately even when the expected shape fully determines
    ///     what the argument's type parameters should be. Infer and substitute them here so `id`
    ///     becomes e.g. `fn(number): number` before the normal assignability/unification check runs.
    /// </summary>
    private bool TryInstantiateGenericFunctionArgument(Node failNode, Type actual, Type expected, [NotNullWhen(true)] out Type? instantiated)
    {
        instantiated = null;
        if (actual is not FunctionType { TypeParameters.Count: > 0 } genericFunction
            || expected is not FunctionType expectedFunction
            || genericFunction.TypeParameters.Count == expectedFunction.TypeParameters.Count)
            return false;

        var substitution = TypeInferrer.InferFunctionTypeArguments(genericFunction, expectedFunction.ParameterTypes);
        foreach (var typeParameter in genericFunction.TypeParameters)
            if (substitution.TryGetValue(typeParameter, out var substitutedType)
                && typeParameter.Constraint != null
                && !substitutedType.IsAssignableTo(typeParameter.Constraint))
                return false;

        var substitutedParameterTypes = SubstituteTypeParameters(failNode, genericFunction.ParameterTypes, substitution);
        var substitutedReturnType = SubstituteTypeParameters(failNode, genericFunction.ReturnType, substitution);
        instantiated = new FunctionType([], substitutedParameterTypes, substitutedReturnType, genericFunction.HasRestParameter, genericFunction.IsAsync);
        return true;
    }
}