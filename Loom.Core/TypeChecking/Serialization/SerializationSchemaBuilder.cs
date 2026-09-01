using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Core.Resolving.Symbols;

// ReSharper disable InvalidXmlDocComment

namespace Loom.Core.TypeChecking.Serialization;

using Type = Types.Type;

/// <summary>
///     Turns a <c>[serializable]</c> interface into its <see cref="SerializationSchema" />, reporting a
///     diagnostic for anything it cannot encode. Nested serializable interfaces are flattened into the
///     parent's field list so the whole type shares one header; recursion through a nested type is
///     rejected rather than flattened forever.
/// </summary>
internal sealed class SerializationSchemaBuilder(SemanticModel semanticModel, DiagnosticBag diagnostics)
{
    private const NumberType DefaultNumberType = NumberType.F32;
    private const NumberType DefaultLengthType = NumberType.U32;

    private readonly HashSet<InterfaceSymbol> _flattening = [];

    /// <summary>Attribute-supplied overrides for one property, resolved once and threaded through nested types.</summary>
    private readonly record struct FieldOptions(
        (double Minimum, double Maximum)? Range,
        double? Step,
        CFrameEncoding? CFrameEncoding
    );

    public SerializationSchema? Build(InterfaceSymbol interfaceSymbol)
    {
        var isPacked = HasIntrinsicAttribute(interfaceSymbol, "packed");
        var fields = new List<SerializationField>();
        return !TryFlatten(interfaceSymbol, ResolveInterfaceType(interfaceSymbol), "", isPacked, fields)
            ? null
            : new SerializationSchema(interfaceSymbol, fields);
    }

    /// <summary>
    ///     Appends <paramref name="interfaceSymbol" />'s encodable properties to <paramref name="fields" />,
    ///     prefixing each path with <paramref name="pathPrefix" />. Returns false once a field has failed to
    ///     encode, so the caller abandons the schema rather than emitting a half-built one.
    /// </summary>
    /// <param name="interfaceType">
    ///     The instantiated type, whose properties carry substituted type arguments. Reading a property's
    ///     type off its declaration instead would yield the uninstantiated parameter - a discriminant
    ///     declared as <c>kind: Kind</c> on a generic base would come back as <c>Kind</c> rather than the
    ///     literal the variant pinned it to.
    /// </param>
    private bool TryFlatten(
        InterfaceSymbol interfaceSymbol,
        Types.InterfaceType? interfaceType,
        string pathPrefix,
        bool isPacked,
        List<SerializationField> fields)
    {
        if (!_flattening.Add(interfaceSymbol))
        {
            diagnostics.Error(
                interfaceSymbol.Declaration,
                InternalCodes.RecursiveSerializableType,
                $"Serializable interface '{interfaceSymbol.Name}' contains itself through '{pathPrefix.TrimEnd('.')}'.",
                "recursive types cannot be flattened into a fixed schema."
            );

            return false;
        }

        try
        {
            var succeeded = true;
            if (interfaceType?.Indexer != null)
            {
                diagnostics.Error(
                    interfaceSymbol.Declaration,
                    InternalCodes.NotSerializable,
                    $"Serializable interface '{interfaceSymbol.Name}' is itself an indexer, so it has no properties to serialize.",
                    $"hold the map in a property instead - 'interface {interfaceSymbol.Name} {{ entries: Record<K, V> }}' encodes as pairs."
                );

                return false;
            }

            foreach (var property in interfaceSymbol.FullProperties)
            {
                if (property.HasIntrinsicAttribute("ignore_serialization"))
                    continue;

                var path = pathPrefix + property.Name;
                if (GetPropertyType(property, interfaceType) is not { } propertyType)
                    continue;

                var options = ReadFieldOptions(property);
                if (TryBuildField(path, propertyType, options, isPacked, property.Declaration) is { } serializationField)
                    fields.Add(serializationField);
                else
                    succeeded = false;
            }

            return succeeded;
        }
        finally
        {
            _flattening.Remove(interfaceSymbol);
        }
    }

    private SerializationField? TryBuildField(string path, Type type, FieldOptions options, bool isPacked, Node reportNode) =>
        type switch
        {
            Types.LiteralType literal => new ConstantField(path, type, literal.Value),
            Types.OptionalType optional => TryBuildField(path, optional.NonNullableType, options, isPacked, reportNode) is { } inner
                ? new OptionalField(path, type, inner)
                : null,
            Types.PrimitiveType { Kind: Types.PrimitiveTypeKind.Bool } => new BoolField(path, type),
            Types.PrimitiveType { Kind: Types.PrimitiveTypeKind.Number } => BuildNumberField(path, type, options, reportNode),
            Types.PrimitiveType { Kind: Types.PrimitiveTypeKind.String } => new StringField(
                path,
                type,
                type is Types.SizedStringType sized ? sized.LengthType : DefaultLengthType
            ),

            Types.PrimitiveType { Kind: Types.PrimitiveTypeKind.Unknown } => new BlobField(path, type, null, null),
            Types.FunctionType => new BlobField(path, type, "function", null),
            Types.ArrayType array => TryBuildField(path + "[]", array.ElementType, options, false, reportNode) is { } element
                ? new ArrayField(path, type, DefaultLengthType, element)
                : null,

            Types.TupleType tuple => BuildTupleField(
                path,
                type,
                tuple,
                options,
                isPacked,
                reportNode
            ),
            Types.UnionType union => BuildUnionField(
                path,
                type,
                union,
                options,
                isPacked,
                reportNode
            ),
            Types.InstantiatedType instantiated => TryBuildInstantiatedField(path, instantiated, options, isPacked, reportNode),
            Types.InterfaceType interfaceType => BuildInterfaceField(path, interfaceType, options, isPacked, reportNode),
            _ => ReportNotSerializable(path, type, reportNode),
        };

    /// <summary>
    ///     Vector3/Vector2/CFrame's width and Array's length width are both phantom generic parameters
    ///     meaningful only here - <c>Expand()</c>ing first (as every other generic instantiation needs,
    ///     since it's the only way to see e.g. Record's indexer) would substitute the argument into
    ///     nothing, since none of these three interfaces' own members reference their type parameter, and
    ///     throw away the one piece of information this method exists to keep. Everything else - including
    ///     the other 7 Roblox datatypes, which aren't generic at all - still falls through to the ordinary
    ///     expand-then-read path below.
    /// </summary>
    private SerializationField? TryBuildInstantiatedField(string path, Types.InstantiatedType instantiated, FieldOptions options, bool isPacked, Node reportNode)
    {
        var declarationName = instantiated.GenericType.Declaration.Name.Text;
        switch (declarationName)
        {
            case "Vector3" or "Vector2" or "CFrame":
            {
                if (ArgumentByName(instantiated, "T", path, reportNode) is not { } typeArgument)
                    return null;

                if (ResolveSizedTypeArgument(typeArgument, path, "component", reportNode) is not { } width)
                    return null;

                if (declarationName == "CFrame")
                    return new CFrameField(path, instantiated, options.CFrameEncoding ?? CFrameEncoding.Compressed, width, isPacked);

                if (!RobloxDatatype.TryGet(declarationName, out var datatype))
                    return ReportNotSerializable(path, instantiated, reportNode);

                return new DatatypeField(path, instantiated, datatype, width, isPacked && datatype.Sentinels.Count > 0);
            }
            case "Array":
            {
                if (ArgumentByName(instantiated, "T", path, reportNode) is not { } elementType
                    || ArgumentByName(instantiated, "L", path, reportNode) is not { } lengthArgument)
                    return null;

                if (ResolveSizedTypeArgument(lengthArgument, path, "length", reportNode, requireUnsigned: true) is not { } lengthType)
                    return null;

                return TryBuildField(path + "[]", elementType, options, false, reportNode) is { } element
                    ? new ArrayField(path, instantiated, lengthType, element)
                    : null;
            }
            default:
                return TryBuildField(path, instantiated.Expand(), options, isPacked, reportNode);
        }
    }

    /// <summary>
    ///     Looks up a type argument by its declared parameter's name rather than position, so a future
    ///     reordering of Vector3/Vector2/CFrame/Array's own type parameters in <c>loom.loom</c> can't
    ///     silently pair the wrong argument with the wrong meaning here - a positional lookup would keep
    ///     compiling and just resolve the wrong width. A missing name means this code and the intrinsic
    ///     declaration have drifted apart, which is a compiler bug rather than something a user wrote.
    /// </summary>
    private Type? ArgumentByName(Types.InstantiatedType instantiated, string parameterName, string path, Node reportNode)
    {
        var index = instantiated.GenericType.Parameters.FindIndex(p => p.Name == parameterName);
        if (index >= 0)
            return instantiated.Arguments.ElementAtOrDefault(index) ?? instantiated.GenericType.Parameters[index].DefaultType;

        diagnostics.Error(
            reportNode,
            InternalCodes.NotSerializable,
            $"'{path}' references '{instantiated.GenericType.Declaration.Name.Text}', which has no type parameter named '{parameterName}' - this is a compiler bug."
        );

        return null;
    }

    private NumberType? ResolveSizedTypeArgument(Type argument, string path, string what, Node reportNode, bool requireUnsigned = false)
    {
        if (argument is not Types.SizedNumberType sized)
        {
            diagnostics.Error(reportNode, InternalCodes.InvalidTypeArguments, $"'{path}' needs a sized type for its {what} width, but got '{argument}'.");
            return null;
        }

        if (!requireUnsigned || sized.NumberType.IsUnsigned())
            return sized.NumberType;

        diagnostics.Error(
            reportNode,
            InternalCodes.InvalidTypeArguments,
            $"'{path}' needs an unsigned length type, but got '{sized.NumberType}'.",
            "lengths are never negative; use U8, U16, or U32."
        );

        return null;
    }

    private SerializationField? BuildNumberField(string path, Type type, FieldOptions options, Node reportNode)
    {
        if (type is Types.SizedNumberType sized)
            return new NumberField(path, type, sized.NumberType);

        if (options.Range is not { } range)
            return new NumberField(path, type, DefaultNumberType);

        var step = options.Step ?? 1;
        if (step <= 0)
        {
            diagnostics.Error(reportNode, InternalCodes.InvalidSerializationRange, $"'{path}' has a number_step of {step}; the step must be positive.");
            return null;
        }

        if (range.Maximum <= range.Minimum)
        {
            diagnostics.Error(
                reportNode,
                InternalCodes.InvalidSerializationRange,
                $"'{path}' has an empty number range [{range.Minimum}, {range.Maximum}]."
            );

            return null;
        }

        // writebits tops out at 32, and the grid index is what goes through it. A wider range would
        // otherwise clamp and silently truncate every value past the cut.
        var bits = BitWidth.ForStateCount(Math.Floor((range.Maximum - range.Minimum) / step) + 1);
        if (bits > BitWidth.MaximumBits)
        {
            diagnostics.Error(
                reportNode,
                InternalCodes.InvalidSerializationRange,
                $"'{path}' needs more than {BitWidth.MaximumBits} bits for range [{range.Minimum}, {range.Maximum}] with a step of {step}.",
                "narrow the range, widen the step, or use a sized type (e.g. 'u32') for a byte-aligned width."
            );

            return null;
        }

        return new RangedNumberField(path, type, range.Minimum, range.Maximum, step);
    }

    private TupleField? BuildTupleField(string path, Type type, Types.TupleType tuple, FieldOptions options, bool isPacked, Node reportNode)
    {
        var elements = new List<SerializationField>();
        for (var index = 0; index < tuple.ElementTypes.Count; index++)
        {
            if (TryBuildField($"{path}[{index + 1}]", tuple.ElementTypes[index], options, isPacked, reportNode) is not { } element)
                return null;

            elements.Add(element);
        }

        return new TupleField(path, type, elements);
    }

    /// <summary>
    ///     Reached for the 5 Roblox datatypes that never became generic (Color3, UDim, UDim2, NumberRange,
    ///     Rect) - Vector3/Vector2/CFrame's own width is a type argument now, resolved by
    ///     <see cref="TryBuildInstantiatedField" /> before this is ever reached, since a property
    ///     referencing any of the three is always an <see cref="Types.InstantiatedType" />, never a plain
    ///     <see cref="Types.InterfaceType" />. Vector2int16/Vector3int16 are rejected outright below,
    ///     rather than serialized at a fixed width.
    /// </summary>
    private SerializationField? BuildInterfaceField(string path, Types.InterfaceType interfaceType, FieldOptions options, bool isPacked, Node reportNode)
    {
        // Vector2<i16>/Vector3<i16> already say "two/three i16 components" with a configurable width, so
        // there is no reason to keep the fixed-width int16 datatypes serializable alongside them.
        if (interfaceType.Name is "Vector2int16" or "Vector3int16")
        {
            var replacement = interfaceType.Name == "Vector2int16" ? "Vector2" : "Vector3";
            diagnostics.Error(
                reportNode,
                InternalCodes.NotSerializable,
                $"'{path}' has type '{interfaceType.Name}', which cannot be serialized.",
                $"use '{replacement}<i16>' instead - its components are already i16, and the width is configurable."
            );

            return null;
        }

        if (RobloxDatatype.TryGet(interfaceType.Name, out var datatype))
            return new DatatypeField(path, interfaceType, datatype, DefaultNumberType, isPacked && datatype.Sentinels.Count > 0);

        // An indexer is a map: the interface has no named properties to flatten, only a key type and a
        // value type, so it encodes as pairs rather than as a struct.
        if (interfaceType.Indexer is { } indexer)
            return BuildMapField(path, interfaceType, indexer, options, reportNode);

        // Instances have no buffer representation, but their class is checkable on the way back.
        if (IsInstanceType(interfaceType))
            return new BlobField(path, interfaceType, "Instance", interfaceType.Name == "Instance" ? null : interfaceType.Name);

        if (FindInterfaceSymbol(interfaceType) is not { } nestedSymbol)
            return ReportNotSerializable(path, interfaceType, reportNode);

        if (!HasIntrinsicAttribute(nestedSymbol, "serializable"))
        {
            diagnostics.Error(
                reportNode,
                InternalCodes.NotSerializable,
                $"'{path}' has type '{interfaceType.Name}', which is not serializable.",
                $"add the 'serializable' attribute to interface '{interfaceType.Name}'."
            );

            return null;
        }

        var nestedFields = new List<SerializationField>();
        if (!TryFlatten(nestedSymbol, interfaceType, path + ".", isPacked, nestedFields))
            return null;

        // Flattened rather than nested: the whole type shares one header, so a nested struct's bools and
        // presence bits pack alongside the parent's instead of paying for a header of their own.
        return new TupleField(path, interfaceType, nestedFields);
    }

    private MapField? BuildMapField(
        string path,
        Types.InterfaceType interfaceType,
        Types.ObjectIndexer indexer,
        FieldOptions options,
        Node reportNode)
    {
        // Neither half takes sentinels: those resolve once per field before the allocation, which cannot
        // express a different choice per entry.
        if (TryBuildField($"{path}[k]", indexer.KeyType, options, false, reportNode) is not { } key)
            return null;

        if (TryBuildField($"{path}[v]", indexer.ValueType, options, false, reportNode) is not { } value)
            return null;

        // 'length_type' is a hard error at check time wherever it appears (see TypeChecker.Serialization.cs),
        // so a map's length prefix has no attribute-supplied override to read here, unlike a plain array.
        return new MapField(path, interfaceType, DefaultLengthType, key, value);
    }

    private SerializationField? BuildUnionField(string path, Type type, Types.UnionType union, FieldOptions options, bool isPacked, Node reportNode)
    {
        var members = union.Types;

        // A union carrying 'none' outside of OptionalType still means optional; peel it off and encode
        // the remainder behind a presence bit.
        var nonNullMembers = members.FindAll(m => m is not Types.PrimitiveType { Kind: Types.PrimitiveTypeKind.None } || m is Types.LiteralType);
        if (nonNullMembers.Count < members.Count)
        {
            var innerType = nonNullMembers.Count == 1 ? nonNullMembers[0] : new Types.UnionType(nonNullMembers);
            return TryBuildField(path, innerType, options, isPacked, reportNode) is { } inner
                ? new OptionalField(path, type, inner)
                : null;
        }

        if (members.Count == 0)
            return ReportNotSerializable(path, type, reportNode);

        // Every variant a literal: the tag is the entire value, so the payload is empty.
        if (members.TrueForAll(m => m is Types.LiteralType))
        {
            var literalVariants = members.ConvertAll(m => new SerializationVariant(((Types.LiteralType)m).Value, []));
            return new UnionField(path, type, UnionDiscrimination.LiteralValue, null, literalVariants);
        }

        if (TryBuildDiscriminatedUnion(path, type, members, isPacked) is { } discriminated)
            return discriminated;

        if (TryBuildRuntimeKindUnion(
                path,
                type,
                members,
                options,
                isPacked,
                reportNode
            ) is { } byKind)
            return byKind;

        diagnostics.Error(
            reportNode,
            InternalCodes.AmbiguousSerializableUnion,
            $"'{path}' has type '{type}', whose variants cannot be told apart at runtime.",
            "give every variant a shared literal-typed field to discriminate on."
        );

        return null;
    }

    /// <summary>
    ///     Interfaces sharing a literal-typed property with a distinct value per variant. The discriminant
    ///     costs nothing: it is recoverable from the tag, so the tag <em>is</em> the field.
    /// </summary>
    private UnionField? TryBuildDiscriminatedUnion(string path, Type type, List<Type> members, bool isPacked)
    {
        if (!members.TrueForAll(m => m is Types.InterfaceType))
            return null;

        var interfaceTypes = members.ConvertAll(m => (Types.InterfaceType)m);
        var candidateNames = interfaceTypes[0]
            .Properties
            .FindAll(p => p.ValueType is Types.LiteralType)
            .ConvertAll(p => p.Name);

        foreach (var candidate in candidateNames)
        {
            var discriminants = new List<object?>();
            foreach (var interfaceType in interfaceTypes)
            {
                if (interfaceType.Properties.Find(p => p.Name == candidate) is not { ValueType: Types.LiteralType literal })
                {
                    discriminants.Clear();
                    break;
                }

                discriminants.Add(literal.Value);
            }

            if (discriminants.Count != interfaceTypes.Count || discriminants.Distinct().Count() != discriminants.Count)
                continue;

            var variants = new List<SerializationVariant>();
            for (var index = 0; index < interfaceTypes.Count; index++)
            {
                if (FindInterfaceSymbol(interfaceTypes[index]) is not { } variantSymbol || !HasIntrinsicAttribute(variantSymbol, "serializable"))
                    return null;

                var variantFields = new List<SerializationField>();
                if (!TryFlatten(variantSymbol, interfaceTypes[index], $"{path}.", isPacked, variantFields))
                    return null;

                // Drop the discriminant itself - the tag already carries it.
                variantFields.RemoveAll(f => f.Path == $"{path}.{candidate}");
                variants.Add(new SerializationVariant(discriminants[index], variantFields));
            }

            return new UnionField(path, type, UnionDiscrimination.Discriminant, candidate, variants);
        }

        return null;
    }

    /// <summary>Variants with distinct <c>typeof</c> results, which is a sound test for primitives.</summary>
    private UnionField? TryBuildRuntimeKindUnion(string path, Type type, List<Type> members, FieldOptions options, bool isPacked, Node reportNode)
    {
        var kinds = new HashSet<string>();
        var variants = new List<SerializationVariant>();
        foreach (var member in members)
        {
            if (RuntimeKindOf(member) is not { } kind || !kinds.Add(kind))
                return null;

            if (TryBuildField(path, member, options, isPacked, reportNode) is not { } memberField)
                return null;

            variants.Add(new SerializationVariant(kind, [memberField]));
        }

        return new UnionField(path, type, UnionDiscrimination.RuntimeKind, null, variants);
    }

    private static string? RuntimeKindOf(Type type) =>
        type switch
        {
            Types.PrimitiveType { Kind: Types.PrimitiveTypeKind.Number } and not Types.LiteralType => "number",
            Types.PrimitiveType { Kind: Types.PrimitiveTypeKind.String } and not Types.LiteralType => "string",
            Types.PrimitiveType { Kind: Types.PrimitiveTypeKind.Bool } and not Types.LiteralType => "boolean",
            Types.FunctionType => "function",
            Types.InterfaceType interfaceType when IsInstanceType(interfaceType) => "Instance",
            Types.ArrayType or Types.TupleType or Types.InterfaceType => "table",
            _ => null
        };

    private SerializationField? ReportNotSerializable(string path, Type type, Node reportNode)
    {
        diagnostics.Error(reportNode, InternalCodes.NotSerializable, $"'{path}' has type '{type}', which cannot be serialized.");
        return null;
    }

    private static bool IsInstanceType(Types.InterfaceType interfaceType) => interfaceType.Name == "Instance" || interfaceType.Constraints.Any(IsInstanceType);

    /// <summary>
    ///     Resolves an interface by name, preferring a declared one over an intrinsic. Plenty of Roblox
    ///     classes share obvious names - <c>Path</c>, <c>Player</c>, <c>Camera</c> - and a user type that
    ///     shadows one would otherwise resolve to the intrinsic and look unserializable.
    /// </summary>
    private InterfaceSymbol? FindInterfaceSymbol(Types.InterfaceType interfaceType)
    {
        var matches = semanticModel.DeclaredSymbols
            .OfType<InterfaceSymbol>()
            .Where(s => s.Name == interfaceType.Name)
            .ToList();

        return matches.Find(s => !s.IsIntrinsic) ?? matches.FirstOrDefault();
    }

    private FieldOptions ReadFieldOptions(PropertySymbol property) =>
        new(
            ReadRangeArgument(property),
            ReadNumberArgument(property, "number_step", 0),
            ReadEnumArgument<CFrameEncoding>(property, "cframe_type", 0)
        );

    private (double Minimum, double Maximum)? ReadRangeArgument(PropertySymbol property) =>
        ReadNumberArgument(property, "number_range", 0) is { } minimum && ReadNumberArgument(property, "number_range", 1) is { } maximum
            ? (minimum, maximum)
            : null;

    private TEnum? ReadEnumArgument<TEnum>(PropertySymbol property, string attributeName, int argumentIndex)
        where TEnum : struct, Enum =>
        ReadNumberArgument(property, attributeName, argumentIndex) is { } value ? (TEnum)Enum.ToObject(typeof(TEnum), (int)value) : null;

    private double? ReadNumberArgument(PropertySymbol property, string attributeName, int argumentIndex)
    {
        if (!property.TryGetIntrinsicAttribute(attributeName, out var attribute))
            return null;

        var arguments = attribute.Attribute.Arguments.ArgumentList;
        if (argumentIndex >= arguments.Count)
            return null;

        return semanticModel.GetConstantValue(arguments[argumentIndex]) switch
        {
            double d => d,
            long l => l,
            int i => i,
            _ => null
        };
    }

    private Type? GetPropertyType(PropertySymbol property, Types.InterfaceType? interfaceType)
    {
        if (interfaceType?.Properties.Find(p => p.Name == property.Name) is { } resolved)
            return resolved.ValueType;

        return property.Declaration is PropertyDeclaration { ColonTypeClause: { } clause } ? semanticModel.GetType(clause.Type) : null;
    }

    private Types.InterfaceType? ResolveInterfaceType(InterfaceSymbol interfaceSymbol) =>
        semanticModel.GetType(interfaceSymbol.Declaration) switch
        {
            Types.InterfaceType interfaceType => interfaceType,
            Types.GenericType { UnderlyingType: Types.InterfaceType underlying } => underlying,
            _ => null
        };

    private bool HasIntrinsicAttribute(InterfaceSymbol interfaceSymbol, string name) =>
        interfaceSymbol.Declaration is IWithAttributes { Attributes: { } attributes }
        && attributes.AttributeList.Exists(a => TypeChecker.IsIntrinsicAttributeNamed(semanticModel, a, name));
}