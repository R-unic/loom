using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving.Symbols;
using Loom.Core.TypeChecking.Serialization;
using Attribute = Loom.Core.Parsing.AST.Attribute;

namespace Loom.Core.TypeChecking;

using Type = Types.Type;

public sealed partial class TypeChecker
{
    /// <summary>Property-level serialization attributes, in the order they are reported.</summary>
    private static readonly string[] _serializationPropertyAttributes =
        ["number_range", "number_step", "cframe_type"];

    /// <summary>
    ///     Interfaces are checked after <c>SolveConstraints</c> rather than inline, so a serializable type
    ///     may reference one declared further down the file without its properties being half-resolved.
    /// </summary>
    private readonly List<InterfaceDeclaration> _interfaceDeclarations = [];

    private void CheckSerializableInterfaces()
    {
        var builder = new SerializationSchemaBuilder(_semanticModel, _diagnostics);
        foreach (var interfaceDeclaration in _interfaceDeclarations)
        {
            if (_semanticModel.GetDeclarationSymbol(interfaceDeclaration, SymbolKind.Interface) is not InterfaceSymbol interfaceSymbol)
                continue;

            var isSerializable = TryGetInterfaceAttribute(interfaceDeclaration, "serializable", out _);
            if (!CheckSerializationAttributes(interfaceDeclaration, isSerializable))
                continue;

            if (!isSerializable)
                continue;

            if (builder.Build(interfaceSymbol) is { } schema)
                _semanticModel.SerializationSchemas[interfaceSymbol] = schema;
        }

        BuildForeignSchemas(builder);
    }

    /// <summary>
    ///     Schemas are per-file, so an interface another module declared has none here and would look
    ///     unmarked. An import binding and a re-export alike carry the exporting module's own symbol
    ///     instance, so the schema can simply be rebuilt - the generator then skips emitting it and
    ///     references the declaring module's codec instead.
    /// </summary>
    private void BuildForeignSchemas(SerializationSchemaBuilder builder)
    {
        var foreignSymbols = _semanticModel.ImportBindings
            .Select(binding => binding.Symbol)
            .Concat(_semanticModel.Exports.Where(export => export.IsReExport).Select(export => export.Symbol));

        foreach (var symbol in foreignSymbols)
        {
            if (symbol is not InterfaceSymbol interfaceSymbol || _semanticModel.SerializationSchemas.ContainsKey(interfaceSymbol))
                continue;

            if (interfaceSymbol.Declaration is not InterfaceDeclaration declaration || !TryGetInterfaceAttribute(declaration, "serializable", out _))
                continue;

            if (builder.Build(interfaceSymbol) is { } schema)
                _semanticModel.SerializationSchemas[interfaceSymbol] = schema;
        }
    }

    /// <summary>
    ///     Enforces the attribute matrix: which attributes require which others, which cannot appear
    ///     together, and which property types each one accepts. Returns false when the interface is too
    ///     malformed to build a schema from.
    /// </summary>
    private bool CheckSerializationAttributes(InterfaceDeclaration interfaceDeclaration, bool isSerializable)
    {
        var name = interfaceDeclaration.Name.Text;
        var valid = true;

        if (TryGetInterfaceAttribute(interfaceDeclaration, "packed", out var packedAttribute) && !isSerializable)
        {
            _diagnostics.Error(
                packedAttribute,
                InternalCodes.MissingRequiredAttribute,
                $"'packed' requires interface '{name}' to also have the 'serializable' attribute.",
                "'packed' only changes how a serializable type is encoded."
            );

            valid = false;
        }

        foreach (var property in interfaceDeclaration.Body?.Members.OfType<PropertyDeclaration>() ?? [])
            valid &= CheckPropertySerializationAttributes(property, name, isSerializable);

        return valid;
    }

    private bool CheckPropertySerializationAttributes(PropertyDeclaration property, string interfaceName, bool isSerializable)
    {
        var present = _serializationPropertyAttributes
            .Where(a => property.TryGetIntrinsicAttribute(_semanticModel, a, out _))
            .ToList();

        var isIgnored = property.TryGetIntrinsicAttribute(_semanticModel, "ignore_serialization", out var ignoreAttribute);
        if (present.Count == 0 && !isIgnored)
            return true;

        var propertyName = property.Name.Text;
        if (!isSerializable)
        {
            var reported = isIgnored ? "ignore_serialization" : present[0];
            _diagnostics.Error(
                property,
                InternalCodes.MissingRequiredAttribute,
                $"'{reported}' requires interface '{interfaceName}' to have the 'serializable' attribute.",
                $"add 'serializable' to '{interfaceName}', or remove the attribute from '{propertyName}'."
            );

            return false;
        }

        var valid = true;
        var propertyType = _semanticModel.GetType(property.ColonTypeClause.Type);

        if (isIgnored)
        {
            if (present.Count > 0)
            {
                _diagnostics.Error(
                    property,
                    InternalCodes.ConflictingAttributes,
                    $"'{propertyName}' is both ignored and annotated with '{present[0]}'.",
                    "an ignored property is not encoded, so it cannot carry encoding attributes."
                );

                valid = false;
            }

            // Deserialization has to produce a value satisfying the declared type, and Loom interfaces
            // have no property defaults to restore from - so the only sound target is an optional.
            if (propertyType is not Types.OptionalType)
            {
                _diagnostics.Error(
                    ignoreAttribute!.Attribute,
                    InternalCodes.InvalidAttributeTargetType,
                    $"'ignore_serialization' requires '{propertyName}' to be optional, since there is no default value to restore.",
                    $"declare it as '{propertyName}: {propertyType}?'."
                );

                valid = false;
            }

            return valid;
        }

        var hasNumberRange = present.Contains("number_range");
        var unwrapped = Unwrap(propertyType);
        var isSizedNumber = unwrapped is Types.SizedNumberType;

        // A sized type already pins the width itself, so the attribute has nothing left to set - this is
        // the one place 'number_range' is refused outright rather than just requiring a plain 'number'.
        if (isSizedNumber && hasNumberRange)
        {
            _diagnostics.Error(
                property,
                InternalCodes.ConflictingAttributes,
                $"'{propertyName}' is already '{unwrapped}', so 'number_range' has nothing left to set.",
                $"remove 'number_range', or declare '{propertyName}: number' to use a bounded range instead."
            );

            valid = false;
        }

        if (present.Contains("number_step") && !hasNumberRange)
        {
            _diagnostics.Error(
                property,
                InternalCodes.MissingRequiredAttribute,
                $"'number_step' on '{propertyName}' requires 'number_range'.",
                "without bounds there is no bit width to derive from a step."
            );

            valid = false;
        }

        if (hasNumberRange && !isSizedNumber && !HasNumericComponents(propertyType))
        {
            _diagnostics.Error(
                property,
                InternalCodes.InvalidAttributeTargetType,
                $"'number_range' requires '{propertyName}' to have numeric components, but it is '{propertyType}'."
            );

            valid = false;
        }

        if (present.Contains("cframe_type") && !IsNamedInterface(propertyType, "CFrame"))
        {
            _diagnostics.Error(
                property,
                InternalCodes.InvalidAttributeTargetType,
                $"'cframe_type' requires '{propertyName}' to be a CFrame, but it is '{propertyType}'."
            );

            valid = false;
        }

        return valid;
    }

    /// <summary>
    ///     Whether <c>[number_range]</c> has anything to apply to: a plain number, or a tuple whose
    ///     elements all do (the attribute distributes to every element that isn't already sized).
    /// </summary>
    private static bool HasNumericComponents(Type type) =>
        Unwrap(type) switch
        {
            Types.PrimitiveType { Kind: Types.PrimitiveTypeKind.Number } => true,
            // The attribute distributes to every element, so it applies as long as all of them are numeric.
            Types.TupleType tuple => tuple.ElementTypes.Count > 0 && tuple.ElementTypes.TrueForAll(HasNumericComponents),
            _ => false
        };

    private static bool IsNamedInterface(Type type, string name) => Unwrap(type) is Types.InterfaceType interfaceType && interfaceType.Name == name;

    /// <summary>
    ///     Looks through optionals, arrays, and generic instantiations, since an attribute on those applies
    ///     to the element - and Vector3/Vector2/CFrame are an <see cref="Types.InstantiatedType" /> now, not
    ///     a plain <see cref="Types.InterfaceType" />, so reaching their name at all requires expanding
    ///     first, same as every other reader of an instantiation elsewhere in the codebase already does.
    /// </summary>
    private static Type Unwrap(Type type) =>
        type switch
        {
            Types.OptionalType optional => Unwrap(optional.NonNullableType),
            Types.ArrayType array => Unwrap(array.ElementType),
            Types.InstantiatedType instantiated => Unwrap(instantiated.Expand()),
            _ => type
        };

    /// <summary>
    ///     Matches an intrinsic attribute by name. Symbol resolution is per-file, so an interface reached
    ///     through an import has attributes belonging to another file's tree that this model has no
    ///     reference entry for - the name is then all there is to go on.
    /// </summary>
    internal static bool IsIntrinsicAttributeNamed(Resolving.SemanticModel semanticModel, Attribute attribute, string name) =>
        semanticModel.GetSymbol(attribute.Expression) is { } symbol
            ? symbol is { IsIntrinsic: true } && symbol.Name == name
            : attribute.Expression is Identifier identifier && identifier.Name.Text == name;

    private bool TryGetInterfaceAttribute(InterfaceDeclaration interfaceDeclaration, string name, out Attribute attribute)
    {
        attribute = null!;
        if (interfaceDeclaration.Attributes == null)
            return false;

        var found = interfaceDeclaration.Attributes.AttributeList.Find(a => IsIntrinsicAttributeNamed(_semanticModel, a, name));

        if (found == null)
            return false;

        attribute = found;
        return true;
    }
}
