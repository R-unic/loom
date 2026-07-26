using Loom.Core.Parsing.AST;
using Loom.Core.Text;
using Loom.Core.TypeChecking;
using Loom.Core.TypeChecking.Types;
using ArrayType = Loom.Core.TypeChecking.Types.ArrayType;
using FunctionType = Loom.Core.TypeChecking.Types.FunctionType;
using IntersectionType = Loom.Core.TypeChecking.Types.IntersectionType;
using OptionalType = Loom.Core.TypeChecking.Types.OptionalType;
using PrimitiveType = Loom.Core.TypeChecking.Types.PrimitiveType;
using TypeParameter = Loom.Core.TypeChecking.Types.TypeParameter;
using UnionType = Loom.Core.TypeChecking.Types.UnionType;

namespace Loom.Testing;

using static PrimitiveType;

[Collection("Assembly")]
public class GenericTypesTest
{
    [Fact]
    public void GenericType_Equality_SameParameters()
    {
        var paramT = new TypeParameter("T");
        var paramU = new TypeParameter("U");
        var decl = new MockGenericNamedDeclaration("Record");
        var generic1 = new GenericType(decl, [paramT, paramU], ObjectType.Empty);
        var generic2 = new GenericType(decl, [paramT, paramU], ObjectType.Empty);
        Assert.True(generic1.Equals(generic1));
        Assert.True(generic1.Equals(generic2));
        Assert.True(generic2.Equals(generic1));
    }

    [Fact]
    public void GenericType_Equality_DifferentParameters()
    {
        var paramT = new TypeParameter("T");
        var paramU = new TypeParameter("U");
        var paramV = new TypeParameter("V");
        var decl = new MockGenericNamedDeclaration("Record");
        var generic1 = new GenericType(decl, [paramT, paramU], ObjectType.Empty);
        var generic2 = new GenericType(decl, [paramT, paramV], ObjectType.Empty);
        var generic3 = new GenericType(decl, [paramT], ObjectType.Empty);
        Assert.True(generic1.Equals(generic2));
        Assert.False(generic1.Equals(generic3));
    }

    [Fact]
    public void GenericType_Equality_DifferentDeclarations()
    {
        var paramT = new TypeParameter("T");
        var decl1 = new MockGenericNamedDeclaration("Record");
        var decl2 = new MockGenericNamedDeclaration("Map");

        var generic1 = new GenericType(decl1, [paramT], ObjectType.Empty);
        var generic2 = new GenericType(decl2, [paramT], ObjectType.Empty);

        Assert.False(generic1.Equals(generic2));
    }

    [Fact]
    public void GenericType_ToString()
    {
        var paramT = new TypeParameter("T");
        var paramU = new TypeParameter("U");
        var decl = new MockGenericNamedDeclaration("Record");

        var generic = new GenericType(decl, [paramT, paramU], ObjectType.Empty);
        Assert.Equal("Record<T, U>", generic.ToString());
    }

    [Fact]
    public void GenericType_WithUnderlyingObjectType()
    {
        var paramK = new TypeParameter("K");
        var paramV = new TypeParameter("V");
        var decl = new MockGenericNamedDeclaration("Record");

        var underlying = new ObjectType(
            new ObjectIndexer(false, paramK, paramV),
            [new ObjectProperty(false, "size", Number)]
        );

        var generic = new GenericType(decl, [paramK, paramV], underlying);

        Assert.Equal("Record<K, V>", generic.ToString());
        Assert.Equal(2, generic.Parameters.Count);
        Assert.Equal(paramK, generic.Parameters[0]);
        Assert.Equal(paramV, generic.Parameters[1]);
        Assert.Equal(underlying, generic.UnderlyingType);
    }

    [Fact]
    public void InstantiatedType_Equality_SameArguments()
    {
        var paramT = new TypeParameter("T");
        var decl = new MockGenericNamedDeclaration("Record");
        var generic = new GenericType(decl, [paramT], ObjectType.Empty);

        var inst1 = new InstantiatedType(generic, [Number]);
        var inst2 = new InstantiatedType(generic, [Number]);

        Assert.True(inst1.Equals(inst2));
    }

    [Fact]
    public void InstantiatedType_Equality_DifferentArguments()
    {
        var paramT = new TypeParameter("T");
        var decl = new MockGenericNamedDeclaration("Record");
        var generic = new GenericType(decl, [paramT], ObjectType.Empty);

        var inst1 = new InstantiatedType(generic, [Number]);
        var inst2 = new InstantiatedType(generic, [String]);
        var inst3 = new InstantiatedType(generic, []);

        Assert.False(inst1.Equals(inst2));
        Assert.False(inst1.Equals(inst3));
    }

    [Fact]
    public void InstantiatedType_Equality_DifferentGenericTypes()
    {
        var paramT = new TypeParameter("T");
        var decl1 = new MockGenericNamedDeclaration("Record");
        var decl2 = new MockGenericNamedDeclaration("Map");

        var generic1 = new GenericType(decl1, [paramT], ObjectType.Empty);
        var generic2 = new GenericType(decl2, [paramT], ObjectType.Empty);

        var inst1 = new InstantiatedType(generic1, [Number]);
        var inst2 = new InstantiatedType(generic2, [Number]);

        Assert.False(inst1.Equals(inst2));
    }

    [Fact]
    public void InstantiatedType_ToString()
    {
        var paramT = new TypeParameter("T");
        var paramU = new TypeParameter("U");
        var decl = new MockGenericNamedDeclaration("Record");
        var generic = new GenericType(decl, [paramT, paramU], ObjectType.Empty);

        var inst = new InstantiatedType(generic, [Number, String]);
        Assert.Equal("Record<number, string>", inst.ToString());
    }

    [Fact]
    public void InstantiatedType_Expand_Simple()
    {
        var paramT = new TypeParameter("T");
        var decl = new MockGenericNamedDeclaration("Box");
        var underlying = new ObjectType(null, [new ObjectProperty(false, "value", paramT)]);
        var generic = new GenericType(decl, [paramT], underlying);

        var inst = new InstantiatedType(generic, [Number]);
        var expanded = inst.Expand();

        var expected = new ObjectType(null, [new ObjectProperty(false, "value", Number)]);
        Assert.True(expected.Equals(expanded), $"Expected '{expected}', got '{expanded}'");
    }

    [Fact]
    public void InstantiatedType_Expand_WithIndexer()
    {
        var paramK = new TypeParameter("K");
        var paramV = new TypeParameter("V");
        var decl = new MockGenericNamedDeclaration("Record");
        var underlying = new ObjectType(
            new ObjectIndexer(false, paramK, paramV),
            [new ObjectProperty(false, "size", Number)]
        );

        var generic = new GenericType(decl, [paramK, paramV], underlying);
        var inst = new InstantiatedType(generic, [String, Bool]);
        var expanded = inst.Expand();
        var expected = new ObjectType(
            new ObjectIndexer(false, String, Bool),
            [new ObjectProperty(false, "size", Number)]
        );

        Assert.True(expected.Equals(expanded), $"Expected '{expected}', got '{expanded}'");

        var indexer = Assert.IsType<ObjectIndexer>(((ObjectType)expanded).Indexer);
        Assert.Equal(String, indexer.KeyType);
        Assert.Equal(Bool, indexer.ValueType);
    }

    [Fact]
    public void InstantiatedType_Expand_WithConstraints()
    {
        var paramT = new TypeParameter("T", Number);
        var decl = new MockGenericNamedDeclaration("Box");
        var underlying = new ObjectType(null, [new ObjectProperty(false, "value", paramT)]);
        var generic = new GenericType(decl, [paramT], underlying);

        var inst = new InstantiatedType(generic, [Number]);
        var expanded = inst.Expand();

        var expected = new ObjectType(null, [new ObjectProperty(false, "value", Number)]);
        Assert.True(expected.Equals(expanded), $"Expected '{expected}', got '{expanded}'");
    }

    [Fact]
    public void InstantiatedType_IsAssignableTo_AfterExpansion()
    {
        var paramT = new TypeParameter("T");
        var decl = new MockGenericNamedDeclaration("Box");
        var underlying = new ObjectType(null, [new ObjectProperty(false, "value", paramT)]);
        var generic = new GenericType(decl, [paramT], underlying);
        var boxNumber = new InstantiatedType(generic, [Number]);
        var boxUnknown = new InstantiatedType(generic, [Unknown]);
        var boxString = new InstantiatedType(generic, [String]);
        Assert.True(
            boxNumber.Expand().Equals(new ObjectType(null, [new ObjectProperty(false, "value", Number)])),
            $"Expected '{{ value: number }}', got '{boxNumber.Expand()}'"
        );

        Assert.True(
            boxUnknown.Expand().Equals(new ObjectType(null, [new ObjectProperty(false, "value", Unknown)])),
            $"Expected '{{ value: unknown }}', got '{boxUnknown.Expand()}'"
        );

        Assert.True(boxNumber.IsAssignableTo(boxUnknown));
        Assert.False(boxUnknown.IsAssignableTo(boxNumber));
        Assert.False(boxNumber.IsAssignableTo(boxString));

        var boxNever = new InstantiatedType(generic, [Never]);
        Assert.True(boxNever.IsAssignableTo(boxNumber));
        Assert.False(boxNumber.IsAssignableTo(boxNever));
    }

    [Fact]
    public void InstantiatedType_IsAssignableTo_Interface()
    {
        var paramT = new TypeParameter("T");
        var decl = new MockGenericNamedDeclaration("Box");
        var underlying = new ObjectType(null, [new ObjectProperty(false, "value", paramT)]);
        var generic = new GenericType(decl, [paramT], underlying);
        var boxNumber = new InstantiatedType(generic, [Number]);
        var expected = new ObjectType(null, [new ObjectProperty(false, "value", Number)]);
        Assert.True(boxNumber.IsAssignableTo(expected));
        Assert.True(expected.IsAssignableTo(boxNumber));
    }

    [Fact]
    public void InstantiatedType_Expand_WithMultipleTypeParameters()
    {
        var paramT = new TypeParameter("T");
        var paramU = new TypeParameter("U");
        var decl = new MockGenericNamedDeclaration("Pair");

        var underlying = new ObjectType(null, [new ObjectProperty(false, "first", paramT), new ObjectProperty(false, "second", paramU)]);

        var generic = new GenericType(decl, [paramT, paramU], underlying);
        var inst = new InstantiatedType(generic, [Number, String]);
        var expanded = inst.Expand();
        var expected = new ObjectType(null, [new ObjectProperty(false, "first", Number), new ObjectProperty(false, "second", String)]);
        Assert.True(expected.Equals(expanded), $"Expected '{expected}', got '{expanded}'");
    }

    [Fact]
    public void InstantiatedType_Expand_WithFunctionType()
    {
        var paramT = new TypeParameter("T");
        var paramU = new TypeParameter("U");
        var decl = new MockGenericNamedDeclaration("Mapper");

        var fnType = new FunctionType([paramT], [paramT], paramU);
        var underlying = new ObjectType(null, [new ObjectProperty(false, "map", fnType)]);

        var generic = new GenericType(decl, [paramT, paramU], underlying);
        var inst = new InstantiatedType(generic, [Number, String]);
        var expanded = inst.Expand();

        var expectedFn = new FunctionType([], [Number], String);
        var expected = new ObjectType(null, [new ObjectProperty(false, "map", expectedFn)]);
        Assert.True(expected.Equals(expanded), $"Expected '{expected}', got '{expanded}'");
    }

    [Fact]
    public void InstantiatedType_Expand_WithUnionAndIntersection()
    {
        var paramT = new TypeParameter("T");
        var decl = new MockGenericNamedDeclaration("Container");
        var union = new UnionType([paramT, Number]);
        var intersection = new IntersectionType([paramT, String]);
        var underlying = new ObjectType(null, [new ObjectProperty(false, "union", union), new ObjectProperty(false, "intersection", intersection)]);
        var generic = new GenericType(decl, [paramT], underlying);
        var inst = new InstantiatedType(generic, [Bool]);
        var expanded = TypeSimplifier.Simplify(inst.Expand());
        var expectedUnion = new UnionType([Bool, Number]);
        var expectedIntersection = new IntersectionType([Bool, String]);
        var expected = new ObjectType(null, [new ObjectProperty(false, "union", expectedUnion), new ObjectProperty(false, "intersection", expectedIntersection)]);
        Assert.True(expected.Equals(expanded), $"Expected '{expected}', got '{expanded}'");
    }

    [Fact]
    public void InstantiatedType_Expand_WithArrayType()
    {
        var paramT = new TypeParameter("T");
        var decl = new MockGenericNamedDeclaration("Container");

        var arrayType = new ArrayType(paramT, false);
        var underlying = new ObjectType(null, [new ObjectProperty(false, "items", arrayType)]);

        var generic = new GenericType(decl, [paramT], underlying);
        var inst = new InstantiatedType(generic, [Number]);
        var expanded = inst.Expand();

        var expectedArray = new ArrayType(Number, false);
        var expected = new ObjectType(null, [new ObjectProperty(false, "items", expectedArray)]);
        Assert.True(expected.Equals(expanded), $"Expected '{expected}', got '{expanded}'");
    }

    [Fact]
    public void InstantiatedType_Expand_WithOptionalType()
    {
        var paramT = new TypeParameter("T");
        var decl = new MockGenericNamedDeclaration("Container");
        var optionalType = new OptionalType(paramT);
        var underlying = new ObjectType(null, [new ObjectProperty(false, "value", optionalType)]);
        var generic = new GenericType(decl, [paramT], underlying);
        var inst = new InstantiatedType(generic, [String]);
        var expanded = inst.Expand();
        var expectedOptional = new UnionType([String, None]);
        var expected = new ObjectType(null, [new ObjectProperty(false, "value", expectedOptional)]);
        Assert.True(expected.Equals(expanded), $"Expected '{expected}', got '{expanded}'");
    }

    [Fact]
    public void GenericInterface_WithIndexer_ExpansionPreservesIndexerMutability()
    {
        var paramK = new TypeParameter("K");
        var paramV = new TypeParameter("V");
        var decl = new MockGenericNamedDeclaration("Record");

        var underlying = new ObjectType(
            new ObjectIndexer(true, paramK, paramV),
            [new ObjectProperty(false, "size", Number)]
        );

        var generic = new GenericType(decl, [paramK, paramV], underlying);
        var inst = new InstantiatedType(generic, [String, Bool]);
        var expanded = inst.Expand();

        var objectType = Assert.IsType<ObjectType>(expanded);
        var indexer = Assert.IsType<ObjectIndexer>(objectType.Indexer);

        Assert.True(indexer.IsMutable);
        Assert.Equal(String, indexer.KeyType);
        Assert.Equal(Bool, indexer.ValueType);
    }

    [Fact]
    public void GenericInterface_WithProperties_ExpansionPreservesMutability()
    {
        var paramT = new TypeParameter("T");
        var decl = new MockGenericNamedDeclaration("Container");

        var underlying = new ObjectType(null, [new ObjectProperty(true, "counter", Number), new ObjectProperty(false, "value", paramT)]);

        var generic = new GenericType(decl, [paramT], underlying);
        var inst = new InstantiatedType(generic, [String]);
        var expanded = inst.Expand();

        var objectType = Assert.IsType<ObjectType>(expanded);
        Assert.Equal(2, objectType.Properties.Count);

        var prop1 = objectType.Properties[0];
        Assert.Equal("counter", prop1.Name);
        Assert.True(prop1.IsMutable);
        Assert.Equal(Number, prop1.ValueType);

        var prop2 = objectType.Properties[1];
        Assert.Equal("value", prop2.Name);
        Assert.False(prop2.IsMutable);
        Assert.Equal(String, prop2.ValueType);
    }

    [Fact]
    public void GenericType_WithDefaultTypeParameter()
    {
        var paramT = new TypeParameter("T", null, Number);
        var decl = new MockGenericNamedDeclaration("Container");
        var underlying = new ObjectType(null, [new ObjectProperty(false, "value", paramT)]);

        var generic = new GenericType(decl, [paramT], underlying);
        Assert.Equal(Number, paramT.DefaultType);
        Assert.Single(generic.Parameters);

        var inst1 = new InstantiatedType(generic, [String]);
        var expanded1 = inst1.Expand();
        var expected1 = new ObjectType(null, [new ObjectProperty(false, "value", String)]);
        Assert.True(expected1.Equals(expanded1), $"Expected '${expected1}', got ${expanded1}");

        var inst2 = new InstantiatedType(generic, []);
        var expanded2 = inst2.Expand();
        var expected2 = new ObjectType(null, [new ObjectProperty(false, "value", Number)]);
        Assert.True(expected2.Equals(expanded2), $"Expected '${expected2}', got ${expanded2}");
    }

    [Fact]
    public void GenericType_WithConstraint_SubstitutionValidates()
    {
        var paramT = new TypeParameter("T", Number);
        var decl = new MockGenericNamedDeclaration("Container");
        var underlying = new ObjectType(null, [new ObjectProperty(false, "value", paramT)]);
        var generic = new GenericType(decl, [paramT], underlying);
        Assert.Equal(Number, paramT.Constraint);
        Assert.NotNull(generic.Parameters[0].Constraint);
        Assert.Equal(Number, generic.Parameters[0].Constraint);
    }

    [Fact]
    public void InstantiatedType_GetTypeAtIndex_UsesExpandedType()
    {
        var paramK = new TypeParameter("K");
        var paramV = new TypeParameter("V");
        var decl = new MockGenericNamedDeclaration("Record");
        var underlying = new ObjectType(
            new ObjectIndexer(false, paramK, paramV),
            [new ObjectProperty(false, "size", Number)]
        );

        var generic = new GenericType(decl, [paramK, paramV], underlying);
        var inst = new InstantiatedType(generic, [String, Bool]);
        var expanded = inst.Expand();
        var objectType = Assert.IsType<ObjectType>(expanded);
        var indexer = Assert.IsType<ObjectIndexer>(objectType.Indexer);
        Assert.Equal(String, indexer.KeyType);
        Assert.Equal(Bool, indexer.ValueType);
    }

    private class MockGenericNamedDeclaration(string name)
        : GenericNamedDeclaration(
            [],
            new Token(SyntaxKind.TypeKeyword, LocationSpan.Empty(), "type"),
            new Token(SyntaxKind.Identifier, LocationSpan.Empty(), name),
            new TypeParameters(new Token(SyntaxKind.LArrow, LocationSpan.Empty(), "<"), new Token(SyntaxKind.LArrow, LocationSpan.Empty(), ">"), [])
        )
    {
        public override T Accept<T>(Visitor<T> visitor) => default!;
    }
}