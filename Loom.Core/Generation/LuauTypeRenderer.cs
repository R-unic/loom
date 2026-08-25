using Loom.Core.TypeChecking;
using Loom.Core.TypeChecking.Types;
using Loom.Luau;
using Loom.Luau.AST;
using FunctionType = Loom.Core.TypeChecking.Types.FunctionType;
using IntersectionType = Loom.Core.TypeChecking.Types.IntersectionType;
using OptionalType = Loom.Core.TypeChecking.Types.OptionalType;
using PrimitiveType = Loom.Core.TypeChecking.Types.PrimitiveType;
using TupleType = Loom.Core.TypeChecking.Types.TupleType;
using TypeParameter = Loom.Core.TypeChecking.Types.TypeParameter;
using Type = Loom.Core.TypeChecking.Types.Type;
using UnionType = Loom.Core.TypeChecking.Types.UnionType;
using Loom.Core.TypeChecking.Solving;

namespace Loom.Core.Generation;

/// <summary>
///     Writes a checked <see cref="Type" /> back out as Luau, for the places where what was written is not
///     what should be emitted - a conditional or mapped type whose answer Loom worked out and Luau has no
///     way to express the question of.
/// </summary>
/// <remarks>
///     The generator otherwise walks the syntax rather than the types, which is what keeps a name emitted
///     as the name it was written under. This is the exception, so it is deliberately narrow: it answers
///     null rather than guessing wherever a type has no Luau counterpart, and the caller falls back to
///     <c>unknown</c> and says so.
/// </remarks>
internal static class LuauTypeRenderer
{
    /// <summary>The Luau form of <paramref name="type" />, or null where there is none.</summary>
    /// <remarks>
    ///     Simplified on the way in, and again wherever an instantiation is expanded below. Substitution
    ///     rebuilds what it walks through <see cref="TypeSolver.Transform" />, which has no OptionalType case
    ///     of its own, so an answer of <c>T?</c> arrives as the bare union it is made of and would otherwise
    ///     emit as <c>number | nil</c> rather than <c>number?</c>.
    /// </remarks>
    public static LuauType? Render(Type type, IReadOnlySet<string> runtimeTypeNames) =>
        Render(TypeSimplifier.Simplify(type), runtimeTypeNames, new HashSet<Type>(ReferenceEqualityComparer.Instance));

    private static LuauType? Render(Type type, IReadOnlySet<string> runtimeTypeNames, HashSet<Type> visiting)
    {
        // A self-referential interface reached through this would render forever; it is only ever reached
        // by name anyway, which the InterfaceType case below does without descending.
        if (!visiting.Add(type))
            return null;

        try
        {
            return RenderCore(type, runtimeTypeNames, visiting);
        }
        finally
        {
            visiting.Remove(type);
        }
    }

    private static LuauType? RenderCore(Type type, IReadOnlySet<string> runtimeTypeNames, HashSet<Type> visiting)
    {
        return type switch
        {
            LiteralType literal => literal.Value switch
            {
                string s => new StringLiteralType(s),
                bool b => new BooleanLiteralType(b),
                long or int or double => Luau.AST.PrimitiveType.Number,
                _ => Luau.AST.PrimitiveType.Nil
            },
            SizedNumberType => Luau.AST.PrimitiveType.Number,
            SizedStringType => Luau.AST.PrimitiveType.String,
            PrimitiveType primitive => new Luau.AST.PrimitiveType(LuauOperatorMap.PrimitiveTypeKind(primitive.Kind)),
            OptionalType optional => renderChild(optional.NonNullableType) is { } inner ? new Luau.AST.OptionalType(inner) : null,
            ArrayType array => renderChild(array.ElementType) is { } element ? TableType.Array(element) : null,
            TupleType tuple => RenderAll(tuple.ElementTypes, renderChild) is { } elements ? TableType.Array(new Luau.AST.UnionType(elements)) : null,
            UnionType union => RenderAll(union.Types, renderChild) is { } members ? new Luau.AST.UnionType(members) : null,
            IntersectionType intersection => RenderAll(intersection.Types, renderChild) is { } parts ? new Luau.AST.IntersectionType(parts) : null,
            FunctionType function => RenderFunction(function, renderChild),
            InstantiatedType instantiated => RenderInstantiated(instantiated, runtimeTypeNames, renderChild),
            InterfaceType interfaceType => Named(interfaceType.Name, null, runtimeTypeNames),
            TypeParameter parameter => new TypeName(parameter.Name),
            ObjectType objectType => RenderObject(objectType, renderChild),
            _ => null
        };

        LuauType? renderChild(Type child) => Render(child, runtimeTypeNames, visiting);
    }

    private static TableType? RenderObject(ObjectType objectType, Func<Type, LuauType?> renderChild)
    {
        TableTypeIndexer? indexer = null;
        if (objectType.Indexer != null)
        {
            var keyType = renderChild(objectType.Indexer.KeyType);
            var valueType = renderChild(objectType.Indexer.ValueType);
            if (keyType == null || valueType == null)
                return null;

            indexer = new TableTypeIndexer(objectType.Indexer.IsMutable ? null : LuauVisibility.Read, keyType, valueType);
        }

        var properties = new List<TableTypeProperty>();
        foreach (var property in objectType.Properties)
        {
            if (renderChild(property.ValueType) is not { } valueType)
                return null;

            properties.Add(new TableTypeProperty(property.IsMutable ? null : LuauVisibility.Read, property.Name, valueType));
        }

        return new TableType(indexer, properties);
    }

    private static Luau.AST.FunctionType? RenderFunction(FunctionType function, Func<Type, LuauType?> renderChild)
    {
        if (RenderAll(function.ParameterTypes, renderChild) is not { } parameterTypes || renderChild(function.ReturnType) is not { } returnType)
            return null;

        if (function.HasRestParameter && parameterTypes is [.., TableType { Indexer.KeyType: null } rest])
            parameterTypes = [..parameterTypes[..^1], new VariadicTypePack(rest.Indexer.ValueType)];

        return new Luau.AST.FunctionType(null, parameterTypes, returnType);
    }

    private static LuauType? RenderInstantiated(InstantiatedType instantiated, IReadOnlySet<string> runtimeTypeNames, Func<Type, LuauType?> renderChild)
    {
        // A generic standing for a computation rather than a shape - 'ReturnType<T>' - has no Luau alias to
        // name, so what it works out to is emitted in its place.
        if (instantiated.GenericType.UnderlyingType is ConditionalType or MappedType)
            return renderChild(TypeSimplifier.Simplify(instantiated.Expand()));

        return RenderAll(instantiated.Arguments, renderChild) is { } arguments
            ? Named(instantiated.GenericType.Declaration.Name.Text, arguments, runtimeTypeNames)
            : null;
    }

    private static LuauType Named(string name, List<LuauType>? arguments, IReadOnlySet<string> runtimeTypeNames)
    {
        var typeName = new TypeName(name, arguments);
        return runtimeTypeNames.Contains(name) ? LuauFactory.QualifyRuntimeType(typeName) : typeName;
    }

    private static List<LuauType>? RenderAll(List<Type> types, Func<Type, LuauType?> renderChild)
    {
        var rendered = new List<LuauType>(types.Count);
        foreach (var type in types)
        {
            if (renderChild(type) is not { } luauType)
                return null;

            rendered.Add(luauType);
        }

        return rendered;
    }
}
