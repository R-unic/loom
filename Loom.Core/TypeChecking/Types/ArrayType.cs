namespace Loom.Core.TypeChecking.Types;

public sealed class ArrayType(Type elementType, bool isMutable)
    : ObjectType(new ObjectIndexer(isMutable, PrimitiveType.Number, elementType), BuildProperties(elementType, isMutable))
{
    public Type ElementType { get; } = elementType;
    public bool IsMutable { get; } = isMutable;

    private static List<ObjectProperty> BuildProperties(Type elementType, bool isMutable)
    {
        var optionalElement = new OptionalType(elementType);
        var properties = new List<ObjectProperty>
        {
            new(false, "length", PrimitiveType.Number),
            new(false, "join", new FunctionType([], [new OptionalType(PrimitiveType.String)], PrimitiveType.String)),
            new(false, "index_of", new FunctionType([], [elementType], new OptionalType(PrimitiveType.Number))),
            new(false, "has", new FunctionType([], [elementType], PrimitiveType.Bool))
        };

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
        if (base.IsAssignableTo(other))
            return true;

        if (other is not ArrayType targetArray)
            return false;

        if (!IsMutable && !targetArray.IsMutable)
            return ElementType.IsAssignableTo(targetArray.ElementType);

        var validMutability = IsMutable || !targetArray.IsMutable;
        return validMutability && (IsNever(ElementType) || ElementType.Equals(targetArray.ElementType));
    }

    public override string ToString() => $"{ParenthesizeIfNeeded(ElementType)}[{(IsMutable ? "mut" : "")}]";
}