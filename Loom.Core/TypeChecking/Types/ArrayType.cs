namespace Loom.Core.TypeChecking.Types;

public sealed class ArrayType(Type elementType, bool isMutable)
    : ObjectType(new ObjectIndexer(isMutable, PrimitiveType.Number, elementType), [])
{
    public Type ElementType { get; } = elementType;
    public bool IsMutable { get; } = isMutable;

    private bool _membersBuilt;

    public override List<ObjectProperty> Properties
    {
        get
        {
            if (_membersBuilt)
                return base.Properties;

            _membersBuilt = true;
            AddProperties(BuildProperties(ElementType, IsMutable));
            return base.Properties;
        }
    }

    private static List<ObjectProperty> BuildProperties(Type elementType, bool isMutable)
    {
        var optionalElement = new OptionalType(elementType);
        var index = PrimitiveType.Number;
        var mapped = new TypeParameter("U");
        var projected = new TypeParameter("U");
        var accumulator = new TypeParameter("U");
        var predicate = new FunctionType([], [elementType, index], PrimitiveType.Bool);
        var properties = new List<ObjectProperty>
        {
            new(false, "length", PrimitiveType.Number),
            new(false, "join", new FunctionType([], [new OptionalType(PrimitiveType.String)], PrimitiveType.String)),
            new(false, "index_of", new FunctionType([], [elementType], new OptionalType(PrimitiveType.Number))),
            new(false, "has", new FunctionType([], [elementType], PrimitiveType.Bool)),
            new(
                false,
                "select",
                new FunctionType([mapped], [new FunctionType([], [elementType, index], mapped)], new ArrayType(mapped, false))
            ),
            new(
                false,
                "select_many",
                new FunctionType(
                    [projected],
                    [new FunctionType([], [elementType, index], new ArrayType(projected, false))],
                    new ArrayType(projected, false)
                )
            ),
            new(
                false,
                "where",
                new FunctionType([], [predicate], new ArrayType(elementType, false))
            ),
            new(
                false,
                "aggregate",
                new FunctionType(
                    [accumulator],
                    [accumulator, new FunctionType([], [accumulator, elementType, index], accumulator)],
                    accumulator
                )
            ),
            new(false, "any", new FunctionType([], [predicate], PrimitiveType.Bool)),
            new(false, "all", new FunctionType([], [predicate], PrimitiveType.Bool)),
            new(false, "count", new FunctionType([], [predicate], PrimitiveType.Number))
        };

        if (elementType is ArrayType nested)
            properties.Add(new ObjectProperty(false, "flatten", new FunctionType([], [], new ArrayType(nested.ElementType, false))));

        if (IntrinsicTypes.SetDefinition is { } setDefinition)
            properties.Add(new ObjectProperty(false, "to_set", new FunctionType([], [], setDefinition.Construct([elementType]))));

        if (!isMutable)
            return properties;

        properties.Add(new ObjectProperty(false, "push", new FunctionType([], [elementType], PrimitiveType.Void)));
        properties.Add(new ObjectProperty(false, "pop", new FunctionType([], [], optionalElement)));
        properties.Add(new ObjectProperty(false, "insert", new FunctionType([], [PrimitiveType.Number, elementType], PrimitiveType.Void)));
        properties.Add(new ObjectProperty(false, "remove", new FunctionType([], [PrimitiveType.Number], optionalElement)));

        return properties;
    }

    public override int GetHashCode() => HashCode.Combine(ElementType.GetHashCode(), IsMutable);
    public override bool Equals(Type? other) => other is ArrayType array && ElementType.Equals(array.ElementType) && IsMutable == array.IsMutable;

    public override bool IsAssignableTo(Type other)
    {
        if (other is ArrayType targetArray)
            return targetArray.IsMutable
                ? IsMutable && (IsNever(ElementType) || ElementType.Equals(targetArray.ElementType))
                : ElementType.IsAssignableTo(targetArray.ElementType);

        return base.IsAssignableTo(other);
    }

    public override string ToString() => $"{ParenthesizeIfNeeded(ElementType)}[{(IsMutable ? "mut" : "")}]";
}
