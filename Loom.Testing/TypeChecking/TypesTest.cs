using Loom.Core.TypeChecking.Serialization;
using Loom.Core.TypeChecking.Types;
using ArrayType = Loom.Core.TypeChecking.Types.ArrayType;
using IntersectionType = Loom.Core.TypeChecking.Types.IntersectionType;
using LiteralType = Loom.Core.TypeChecking.Types.LiteralType;
using OptionalType = Loom.Core.TypeChecking.Types.OptionalType;
using PrimitiveType = Loom.Core.TypeChecking.Types.PrimitiveType;
using Type = Loom.Core.TypeChecking.Types.Type;
using TypeParameter = Loom.Core.TypeChecking.Types.TypeParameter;
using UnionType = Loom.Core.TypeChecking.Types.UnionType;

namespace Loom.Testing;

using static PrimitiveType;
using Loom.Core.TypeChecking.Solving;

[Collection("Assembly")]
public class TypesTest
{
    [Fact]
    public void ObjectType_GetTypeAtIndex_Property()
    {
        var obj = new ObjectType(
            null,
            [new ObjectProperty(true, "x", Number)]
        );

        var (body, error) = obj.GetTypeAtIndex(new LiteralType("x"));
        Assert.Equal("", error);

        var property = Assert.IsType<ObjectProperty>(body);
        Assert.True(property.IsMutable);
        Assert.Equal(Number, property.ValueType);
    }

    [Fact]
    public void ObjectType_GetTypeAtIndex_MissingProperty_NoIndexer()
    {
        var obj = ObjectType.Empty;
        var (body, error) = obj.GetTypeAtIndex(new LiteralType("x"));

        Assert.Null(body);
        Assert.Contains("Property 'x' does not exist", error);
    }

    [Fact]
    public void ObjectType_GetTypeAtIndex_MissingProperty_UsesIndexer()
    {
        var indexer = new ObjectIndexer(false, String, Number);
        var obj = new ObjectType(indexer, []);
        var (body, error) = obj.GetTypeAtIndex(new LiteralType("x"));

        Assert.Equal("", error);
        Assert.Same(indexer, body);
    }

    [Fact]
    public void ObjectType_GetTypeAtIndex_PropertyKeyUnion()
    {
        var obj = new ObjectType(
            null,
            [new ObjectProperty(false, "a", Number), new ObjectProperty(true, "b", String)]
        );

        var keyUnion = obj.PropertyKeyUnion();
        var (body, error) = obj.GetTypeAtIndex(keyUnion);
        Assert.Equal("", error);

        var indexer = Assert.IsType<ObjectIndexer>(body);
        Assert.True(indexer.IsMutable);
        Assert.Equal(keyUnion, indexer.KeyType);

        var expected = TypeSimplifier.Simplify(new UnionType([Number, String]));
        Assert.True(indexer.ValueType.Equals(expected));
    }

    [Fact]
    public void ObjectType_GetTypeAtIndex_WrongIndexerType()
    {
        var obj = new ObjectType(
            new ObjectIndexer(false, Number, String),
            []
        );

        var (body, error) = obj.GetTypeAtIndex(String);

        Assert.Null(body);
        Assert.Contains("Index is not of type", error);
    }

    [Fact]
    public void ObjectType_GetTypeAtIndex_UnconstrainedTypeParameter()
    {
        var obj = new ObjectType(
            new ObjectIndexer(false, Number, String),
            []
        );

        var parameter = new TypeParameter("T");
        var (body, error) = obj.GetTypeAtIndex(parameter);

        Assert.Null(body);
        Assert.Contains("unconstrained", error);
    }

    [Fact]
    public void ObjectType_GetTypeAtIndex_ConstrainedTypeParameter()
    {
        var indexer = new ObjectIndexer(false, Number, String);
        var obj = new ObjectType(indexer, []);
        var parameter = new TypeParameter("T", Number);
        var (body, error) = obj.GetTypeAtIndex(parameter);

        Assert.Equal("", error);
        Assert.Same(indexer, body);
    }

    [Fact]
    public void ObjectType_GetTypeAtIndex_UsesSelfInErrorMessage()
    {
        var interfaceType = new InterfaceType("Foo", [], ObjectType.Empty);
        var (body, error) = interfaceType.GetTypeAtIndex(new LiteralType("missing"));

        Assert.Null(body);
        Assert.Contains("Foo", error);
    }

    [Fact]
    public void ObjectType_GetProperty()
    {
        var property = new ObjectProperty(false, "name", String);

        var obj = new ObjectType(null, [property]);

        Assert.Same(property, obj.GetProperty("name"));
        Assert.Null(obj.GetProperty("missing"));
    }

    [Fact]
    public void InterfaceType_GetProperty_FromOwnObject()
    {
        var property = new ObjectProperty(false, "id", Number);
        var iface = new InterfaceType(
            "I",
            [],
            new ObjectType(null, [property])
        );

        Assert.Same(property, iface.GetProperty("id"));
    }

    [Fact]
    public void InterfaceType_GetProperty_FromConstraint()
    {
        var property = new ObjectProperty(false, "id", Number);

        var baseInterface = new InterfaceType(
            "Base",
            [],
            new ObjectType(null, [property])
        );

        var derived = new InterfaceType(
            "Derived",
            [baseInterface],
            ObjectType.Empty
        );

        Assert.Same(property, derived.GetProperty("id"));
    }

    [Fact]
    public void InterfaceType_Indexer_FromConstraint()
    {
        var indexer = new ObjectIndexer(false, String, Number);

        var baseInterface = new InterfaceType(
            "Base",
            [],
            new ObjectType(indexer, [])
        );

        var derived = new InterfaceType(
            "Derived",
            [baseInterface],
            ObjectType.Empty
        );

        Assert.Same(indexer, derived.Indexer);
    }

    [Fact]
    public void InterfaceType_Indexer_PrefersOwnIndexer()
    {
        var baseIndexer = new ObjectIndexer(false, String, Number);
        var ownIndexer = new ObjectIndexer(false, Number, String);
        var baseInterface = new InterfaceType(
            "Base",
            [],
            new ObjectType(baseIndexer, [])
        );

        var derived = new InterfaceType(
            "Derived",
            [baseInterface],
            new ObjectType(ownIndexer, [])
        );

        Assert.Same(ownIndexer, derived.Indexer);
    }

    [Fact]
    public void InterfaceType_AssignabilityType_NoConstraints()
    {
        var obj = new ObjectType(null, []);
        var iface = new InterfaceType("I", [], obj);
        Assert.Same(obj, iface.AssignabilityType);
    }

    [Fact]
    public void InterfaceType_AssignabilityType_WithConstraints()
    {
        var baseInterface = new InterfaceType(
            "Base",
            [],
            new ObjectType(null, [new ObjectProperty(false, "id", Number)])
        );

        var derived = new InterfaceType(
            "Derived",
            [baseInterface],
            ObjectType.Empty
        );

        Assert.IsType<IntersectionType>(derived.AssignabilityType);
    }

    [Fact]
    public void ObjectType_PropertyUnion_EmptyObject()
    {
        var empty = ObjectType.Empty;
        Assert.Equal(Never, empty.PropertyUnion());
    }

    [Fact]
    public void ObjectType_ValueUnion_EmptyObject()
    {
        var empty = ObjectType.Empty;
        Assert.Equal(Never, empty.ValueUnion());
    }

    [Fact]
    public void ObjectType_PropertyUnion_OnlyProperties()
    {
        var obj = new ObjectType(null, [new ObjectProperty(false, "x", Number), new ObjectProperty(false, "y", String), new ObjectProperty(false, "z", Bool)]);

        var propertyUnion = obj.PropertyUnion();
        var expected = new UnionType([Number, String, Bool]);
        expected = Assert.IsType<UnionType>(TypeSimplifier.Simplify(expected));
        Assert.True(propertyUnion.Equals(expected));
    }

    [Fact]
    public void ObjectType_ValueUnion_OnlyProperties()
    {
        var obj = new ObjectType(null, [new ObjectProperty(false, "x", Number), new ObjectProperty(false, "y", String)]);

        var propertyUnion = obj.PropertyUnion();
        var valueUnion = obj.ValueUnion();
        Assert.True(propertyUnion.Equals(valueUnion));
    }

    [Fact]
    public void ObjectType_PropertyUnion_OnlyIndexer()
    {
        var obj = new ObjectType(
            new ObjectIndexer(false, String, Number),
            []
        );

        Assert.Equal(Never, obj.PropertyUnion());
    }

    [Fact]
    public void ObjectType_ValueUnion_OnlyIndexer()
    {
        var obj = new ObjectType(
            new ObjectIndexer(false, String, Number),
            []
        );

        Assert.Equal(Number, obj.ValueUnion());
    }

    [Fact]
    public void ObjectType_PropertyUnion_WithIndexerAndProperties()
    {
        var obj = new ObjectType(
            new ObjectIndexer(false, Number, Bool),
            [new ObjectProperty(false, "a", String), new ObjectProperty(false, "b", Number)]
        );

        var propertyUnion = obj.PropertyUnion();
        var expected = new UnionType([String, Number]);
        expected = Assert.IsType<UnionType>(TypeSimplifier.Simplify(expected));
        Assert.True(propertyUnion.Equals(expected));
    }

    [Fact]
    public void ObjectType_ValueUnion_WithIndexerAndProperties()
    {
        var obj = new ObjectType(
            new ObjectIndexer(false, Number, Bool),
            [new ObjectProperty(false, "a", String), new ObjectProperty(false, "b", Number)]
        );

        var valueUnion = obj.ValueUnion();
        var expected = new UnionType([Bool, String, Number]);
        expected = Assert.IsType<UnionType>(TypeSimplifier.Simplify(expected));
        Assert.True(valueUnion.Equals(expected));
    }

    [Fact]
    public void ObjectType_ValueUnion_DuplicateTypeSimplification()
    {
        var obj = new ObjectType(
            new ObjectIndexer(false, String, Number),
            [new ObjectProperty(false, "x", Number)]
        );

        var valueUnion = obj.ValueUnion();
        Assert.Equal(Number, valueUnion);
    }

    [Fact]
    public void ObjectType_PropertyKeyUnion_EmptyObject()
    {
        var empty = ObjectType.Empty;
        var propertyKeyUnion = empty.PropertyKeyUnion();
        Assert.Equal(Never, propertyKeyUnion);
    }

    [Fact]
    public void ObjectType_PropertyKeyUnion_WithProperties()
    {
        var obj = new ObjectType(null, [new ObjectProperty(false, "x", Number), new ObjectProperty(false, "y", String), new ObjectProperty(false, "z", Bool)]);

        var propertyKeyUnion = obj.PropertyKeyUnion();
        var expected = new UnionType([new LiteralType("x"), new LiteralType("y"), new LiteralType("z")]);

        expected = Assert.IsType<UnionType>(TypeSimplifier.Simplify(expected));
        Assert.True(propertyKeyUnion.Equals(expected));
    }

    [Fact]
    public void ObjectType_KeyUnion_NoIndexer_OnlyProperties()
    {
        var obj = new ObjectType(null, [new ObjectProperty(false, "a", Number), new ObjectProperty(false, "b", String)]);

        var keyUnion = obj.KeyUnion();
        var expected = new UnionType([new LiteralType("a"), new LiteralType("b")]);
        expected = Assert.IsType<UnionType>(TypeSimplifier.Simplify(expected));
        Assert.True(keyUnion.Equals(expected));
    }

    [Fact]
    public void ObjectType_KeyUnion_IndexerOnly_NoProperties()
    {
        var obj = new ObjectType(
            new ObjectIndexer(false, String, Number),
            []
        );

        var keyUnion = obj.KeyUnion();
        Assert.Equal(String, keyUnion);
    }

    [Fact]
    public void ObjectType_KeyUnion_IndexerAndProperties()
    {
        var obj = new ObjectType(
            new ObjectIndexer(false, Number, Bool),
            [new ObjectProperty(false, "x", String), new ObjectProperty(false, "y", Number)]
        );

        var keyUnion = obj.KeyUnion();
        var expected = new UnionType([Number, new LiteralType("x"), new LiteralType("y")]);
        expected = (UnionType)TypeSimplifier.Simplify(expected);
        Assert.True(keyUnion.Equals(expected));
    }

    [Fact]
    public void ObjectType_KeyUnion_DuplicateKeys_Simplified()
    {
        var obj = new ObjectType(
            new ObjectIndexer(false, new LiteralType("x"), Number),
            [new ObjectProperty(false, "x", String)]
        );

        var keyUnion = obj.KeyUnion();
        var expected = new LiteralType("x");
        Assert.Equal(expected, keyUnion);
    }

    [Fact]
    public void InterfaceType_Assignability_Self()
    {
        var interfaceA = new InterfaceType("A", [], ObjectType.Empty);
        Assert.True(interfaceA.IsAssignableTo(interfaceA));
    }

    [Fact]
    public void InterfaceType_Assignability_WithSingleConstraint()
    {
        var objectB = new ObjectType(null, [new ObjectProperty(false, "id", Number)]);
        var interfaceB = new InterfaceType("B", [], objectB);
        var interfaceA = new InterfaceType("A", [interfaceB], new ObjectType(null, [new ObjectProperty(false, "name", String)]));
        Assert.True(interfaceA.IsAssignableTo(interfaceB));
        Assert.False(interfaceB.IsAssignableTo(interfaceA));
    }

    [Fact]
    public void InterfaceType_Assignability_WithMultipleConstraints()
    {
        var objectB = new ObjectType(null, [new ObjectProperty(false, "id", Number)]);
        var interfaceB = new InterfaceType("B", [], objectB);
        var objectC = new ObjectType(null, [new ObjectProperty(false, "name", String)]);
        var interfaceC = new InterfaceType("C", [], objectC);
        var interfaceA = new InterfaceType("A", [interfaceB, interfaceC], ObjectType.Empty);
        Assert.True(interfaceA.IsAssignableTo(interfaceB));
        Assert.True(interfaceA.IsAssignableTo(interfaceC));
        Assert.False(interfaceB.IsAssignableTo(interfaceA));
        Assert.False(interfaceC.IsAssignableTo(interfaceA));
    }

    [Fact]
    public void InterfaceType_Assignability_TransitiveConstraints()
    {
        var objectC = new ObjectType(null, [new ObjectProperty(false, "c", Number)]);
        var interfaceC = new InterfaceType("C", [], objectC);
        var objectB = new ObjectType(null, [new ObjectProperty(false, "b", Number)]);
        var interfaceB = new InterfaceType("B", [interfaceC], objectB);
        var objectA = new ObjectType(null, [new ObjectProperty(false, "a", Number)]);
        var interfaceA = new InterfaceType("A", [interfaceB], objectA);
        Assert.True(interfaceA.IsAssignableTo(interfaceB));
        Assert.True(interfaceA.IsAssignableTo(interfaceC));
        Assert.False(interfaceC.IsAssignableTo(interfaceA));
    }

    [Fact]
    public void InterfaceType_Assignability_WithObjectTypeStructure()
    {
        var objectType = new ObjectType(null, [new ObjectProperty(false, "x", Number), new ObjectProperty(false, "y", Number)]);
        var interfacePoint = new InterfaceType("Point", [], objectType);
        var interfaceCoord = new InterfaceType("Coord", [], objectType);
        Assert.True(interfacePoint.IsAssignableTo(interfaceCoord));
        Assert.True(interfaceCoord.IsAssignableTo(interfacePoint));

        var object3D = new ObjectType(
            null,
            [new ObjectProperty(false, "x", Number), new ObjectProperty(false, "y", Number), new ObjectProperty(false, "z", Number)]
        );

        var interfacePoint3D = new InterfaceType("Point3D", [], object3D);
        Assert.True(interfacePoint3D.IsAssignableTo(interfacePoint));
        Assert.False(interfacePoint.IsAssignableTo(interfacePoint3D));
    }

    [Fact]
    public void InterfaceType_Assignability_WithConstraintsAndObjectType()
    {
        var baseObj = new ObjectType(null, [new ObjectProperty(false, "id", Number)]);
        var interfaceBase = new InterfaceType("Base", [], baseObj);
        var derivedObj = new ObjectType(null, [new ObjectProperty(false, "name", String)]);
        var interfaceDerived = new InterfaceType("Derived", [interfaceBase], derivedObj);
        Assert.True(interfaceDerived.IsAssignableTo(interfaceBase));
        Assert.False(interfaceBase.IsAssignableTo(interfaceDerived));
    }

    [Fact]
    public void InterfaceType_Assignability_WithCovariantProperties()
    {
        var baseProp = new ObjectType(null, [new ObjectProperty(false, "value", Number)]);
        var interfaceBase = new InterfaceType("Base", [], baseProp);
        var derivedProp = new ObjectType(null, [new ObjectProperty(false, "value", new LiteralType(42))]);
        var interfaceDerived = new InterfaceType("Derived", [interfaceBase], derivedProp);
        Assert.True(interfaceDerived.IsAssignableTo(interfaceBase));
    }

    [Fact]
    public void ObjectType_Assignability_EmptyObject()
    {
        var empty = ObjectType.Empty;
        var withProps = new ObjectType(null, [new ObjectProperty(false, "x", Number)]);
        Assert.False(empty.IsAssignableTo(withProps));
        Assert.True(withProps.IsAssignableTo(empty));
    }

    [Fact]
    public void ObjectType_Assignability_ExtraProperties()
    {
        var point = new ObjectType(null, [new ObjectProperty(false, "x", Number), new ObjectProperty(false, "y", Number)]);
        var point3D = new ObjectType(null, [new ObjectProperty(false, "x", Number), new ObjectProperty(false, "y", Number), new ObjectProperty(false, "z", Number)]);
        var justX = new ObjectType(null, [new ObjectProperty(false, "x", Number)]);
        Assert.True(point3D.IsAssignableTo(point));
        Assert.True(point3D.IsAssignableTo(justX));
        Assert.False(point.IsAssignableTo(point3D));
        Assert.False(justX.IsAssignableTo(point));
    }

    [Fact]
    public void ObjectType_Assignability_PropertyTypeCovariance()
    {
        var numberProp = new ObjectType(null, [new ObjectProperty(false, "value", Number)]);
        var unknownProp = new ObjectType(null, [new ObjectProperty(false, "value", Unknown)]);
        var neverProp = new ObjectType(null, [new ObjectProperty(false, "value", Never)]);
        Assert.True(numberProp.IsAssignableTo(unknownProp));
        Assert.False(unknownProp.IsAssignableTo(numberProp));
        Assert.True(neverProp.IsAssignableTo(numberProp));
        Assert.False(numberProp.IsAssignableTo(neverProp));
    }

    /// <summary>
    ///     'mut' is a capability: giving it up is safe, gaining it is not. Reading it the other way round
    ///     let an immutable property stand in for a mutable one, which hands out write access the source
    ///     never granted.
    /// </summary>
    [Fact]
    public void ObjectType_Assignability_PropertyMutability()
    {
        var mutableProp = new ObjectType(null, [new ObjectProperty(true, "x", Number)]);
        var immutableProp = new ObjectType(null, [new ObjectProperty(false, "x", Number)]);
        Assert.True(mutableProp.IsAssignableTo(immutableProp));
        Assert.False(immutableProp.IsAssignableTo(mutableProp));
    }

    [Fact]
    public void ObjectType_Assignability_MissingProperty()
    {
        var withName = new ObjectType(null, [new ObjectProperty(false, "name", String)]);
        var withNameAndAge = new ObjectType(null, [new ObjectProperty(false, "name", String), new ObjectProperty(false, "age", Number)]);
        Assert.True(withNameAndAge.IsAssignableTo(withName));
        Assert.False(withName.IsAssignableTo(withNameAndAge));
    }

    [Fact]
    public void ObjectType_Assignability_DifferentPropertyNames()
    {
        var point = new ObjectType(null, [new ObjectProperty(false, "x", Number), new ObjectProperty(false, "y", Number)]);
        var differentNames = new ObjectType(null, [new ObjectProperty(false, "a", Number), new ObjectProperty(false, "b", Number)]);
        Assert.False(point.IsAssignableTo(differentNames));
        Assert.False(differentNames.IsAssignableTo(point));
    }

    [Fact]
    public void ObjectType_Assignability_WithIndexer_MutableTarget()
    {
        var withIndexer = new ObjectType(
            new ObjectIndexer(true, String, Number),
            [new ObjectProperty(false, "name", String)]
        );

        var withoutIndexer = new ObjectType(null, [new ObjectProperty(false, "name", String)]);

        Assert.False(withoutIndexer.IsAssignableTo(withIndexer));
        Assert.True(withIndexer.IsAssignableTo(withoutIndexer));
    }

    [Fact]
    public void ObjectType_Assignability_WithIndexer_ImmutableBoth()
    {
        var indexer1 = new ObjectType(
            new ObjectIndexer(false, String, Number),
            []
        );

        var indexer2 = new ObjectType(
            new ObjectIndexer(false, String, Number),
            []
        );

        var indexerDiffKey = new ObjectType(
            new ObjectIndexer(false, Number, Number),
            []
        );

        var indexerDiffValue = new ObjectType(
            new ObjectIndexer(false, String, String),
            []
        );

        Assert.True(indexer1.IsAssignableTo(indexer2));
        Assert.False(indexer1.IsAssignableTo(indexerDiffKey));
        Assert.False(indexer1.IsAssignableTo(indexerDiffValue));
    }

    [Fact]
    public void ObjectType_Assignability_WithIndexer_MutableVsImmutable()
    {
        var mutableIndexer = new ObjectType(
            new ObjectIndexer(true, String, Number),
            []
        );

        var immutableIndexer = new ObjectType(
            new ObjectIndexer(false, String, Number),
            []
        );

        Assert.True(mutableIndexer.IsAssignableTo(immutableIndexer));
        Assert.False(immutableIndexer.IsAssignableTo(mutableIndexer));
    }

    [Fact]
    public void ObjectType_Assignability_ComplexNested()
    {
        var innerNumber = new ObjectType(null, [new ObjectProperty(false, "value", Number)]);
        var innerUnknown = new ObjectType(null, [new ObjectProperty(false, "value", Unknown)]);
        var outerNumber = new ObjectType(null, [new ObjectProperty(false, "data", innerNumber)]);
        var outerUnknown = new ObjectType(null, [new ObjectProperty(false, "data", innerUnknown)]);
        Assert.True(outerNumber.IsAssignableTo(outerUnknown));
        Assert.False(outerUnknown.IsAssignableTo(outerNumber));
    }

    [Fact]
    public void ObjectType_Assignability_WithOptionalProperties()
    {
        var required = new ObjectType(null, [new ObjectProperty(false, "name", String)]);
        var optional = new ObjectType(null, [new ObjectProperty(false, "name", new OptionalType(String))]);
        Assert.True(required.IsAssignableTo(optional));
        Assert.False(optional.IsAssignableTo(required));
    }

    [Fact]
    public void ObjectType_Assignability_WithNeverAndUnknown()
    {
        var neverObj = new ObjectType(null, [new ObjectProperty(false, "prop", Never)]);
        var numberObj = new ObjectType(null, [new ObjectProperty(false, "prop", Number)]);
        Assert.True(neverObj.IsAssignableTo(numberObj));
        Assert.False(numberObj.IsAssignableTo(neverObj));

        var unknownObj = new ObjectType(null, [new ObjectProperty(false, "prop", Unknown)]);
        Assert.True(numberObj.IsAssignableTo(unknownObj));
        Assert.False(unknownObj.IsAssignableTo(numberObj));
    }

    /// <summary>
    ///     A mutable property is invariant, like a mutable array's elements. Letting it vary is unsound in
    ///     either direction: through a target typed <c>mut set: fn(string): void</c> you can store a
    ///     string-taking function, and the source - still believing the slot holds <c>fn(unknown): void</c> -
    ///     will then call it with anything.
    /// </summary>
    [Fact]
    public void ObjectType_Assignability_MutableProperty_IsInvariant()
    {
        var animalWriter = new ObjectType(null, [new ObjectProperty(true, "set", new FunctionType([], [Unknown], Void))]);
        var catWriter = new ObjectType(null, [new ObjectProperty(true, "set", new FunctionType([], [String], Void))]);
        Assert.False(animalWriter.IsAssignableTo(catWriter));
        Assert.False(catWriter.IsAssignableTo(animalWriter));
    }

    /// <summary>An immutable property can only be read through, so it varies with its type.</summary>
    [Fact]
    public void ObjectType_Assignability_ImmutableProperty_IsCovariant()
    {
        var cat = new ObjectType(null, [new ObjectProperty(false, "name", String)]);
        var animal = new ObjectType(null, [new ObjectProperty(false, "name", Unknown)]);
        Assert.True(cat.IsAssignableTo(animal));
        Assert.False(animal.IsAssignableTo(cat));
    }

    /// <summary>
    ///     Dropping 'mut' also opens the value type up, since the target can only be read through. Built here
    ///     rather than from source, where a mutable member's declared type is widened ('mut [string]: 1' is
    ///     declared as 'number') - the narrow case reaches assignability through inference and substitution.
    /// </summary>
    [Fact]
    public void ObjectType_Assignability_MutableIndexerToImmutable_IsCovariant()
    {
        var source = new ObjectType(new ObjectIndexer(true, String, new LiteralType(1L)), []);
        var target = new ObjectType(new ObjectIndexer(false, String, Number), []);
        Assert.True(source.IsAssignableTo(target));
        Assert.False(target.IsAssignableTo(source));
    }

    [Fact]
    public void ArrayType_Assignability_CovariantImmutable()
    {
        var immutBases = new ArrayType(Unknown, false);
        var immutSubtypes = new ArrayType(String, false);
        var immutNumbers = new ArrayType(Number, false);
        Assert.True(immutSubtypes.IsAssignableTo(immutBases));
        Assert.True(immutNumbers.IsAssignableTo(immutBases));
        Assert.False(immutSubtypes.IsAssignableTo(immutNumbers));
        Assert.True(immutSubtypes.IsAssignableTo(immutSubtypes));
    }

    [Fact]
    public void ArrayType_Assignability_ContravariantMutable()
    {
        var immutBase = new ArrayType(Number, true);
        var immutSubtype = new ArrayType(Never, true);
        Assert.True(immutSubtype.IsAssignableTo(immutBase));
        Assert.False(immutBase.IsAssignableTo(immutSubtype));
    }

    [Fact]
    public void ArrayType_Assignability_InvariantMutable()
    {
        var mutNumbers = new ArrayType(Number, true);
        var mutStrings = new ArrayType(String, true);
        var mutUnknown = new ArrayType(Unknown, true);
        Assert.True(mutNumbers.IsAssignableTo(mutNumbers));
        Assert.True(mutStrings.IsAssignableTo(mutStrings));
        Assert.False(mutStrings.IsAssignableTo(mutUnknown));
        Assert.False(mutNumbers.IsAssignableTo(mutUnknown));
        Assert.False(mutStrings.IsAssignableTo(mutNumbers));

        var mutStrings2 = new ArrayType(String, true);
        Assert.True(mutStrings.IsAssignableTo(mutStrings2));
    }

    [Fact]
    public void ArrayType_Assignability_CrossMutability()
    {
        var immutNumbers = new ArrayType(Number, false);
        var mutNumbers = new ArrayType(Number, true);
        Assert.False(immutNumbers.IsAssignableTo(mutNumbers));
        Assert.True(mutNumbers.IsAssignableTo(immutNumbers));

        var immutStrings = new ArrayType(String, false);
        var mutStrings = new ArrayType(String, true);
        Assert.False(immutStrings.IsAssignableTo(mutStrings));
        Assert.True(mutStrings.IsAssignableTo(immutStrings));

        Assert.True(mutNumbers.IsAssignableTo(new ArrayType(Unknown, false)));
    }

    [Fact]
    public void ArrayType_Assignability_WithOptionalElements()
    {
        var optionalNumber = new OptionalType(Number);
        var requiredNumber = Number;
        var arrayOfOptional = new ArrayType(optionalNumber, false);
        var arrayOfRequired = new ArrayType(requiredNumber, false);
        Assert.True(arrayOfRequired.IsAssignableTo(arrayOfOptional));
        Assert.False(arrayOfOptional.IsAssignableTo(arrayOfRequired));

        var mutArrayOfOptional = new ArrayType(optionalNumber, true);
        var mutArrayOfRequired = new ArrayType(requiredNumber, true);
        Assert.False(mutArrayOfRequired.IsAssignableTo(mutArrayOfOptional));
        Assert.False(mutArrayOfOptional.IsAssignableTo(mutArrayOfRequired));
    }

    [Fact]
    public void ArrayType_Assignability_NestedArrays()
    {
        var arrayOfNumberArray = new ArrayType(new ArrayType(Number, false), false);
        var arrayOfStringArray = new ArrayType(new ArrayType(String, false), false);
        var arrayOfUnknownArray = new ArrayType(new ArrayType(Unknown, false), false);
        Assert.True(arrayOfStringArray.IsAssignableTo(arrayOfUnknownArray));
        Assert.False(arrayOfNumberArray.IsAssignableTo(arrayOfStringArray));

        var mutOuterImmutInner = new ArrayType(new ArrayType(Number, false), true);
        var immutOuterMutInner = new ArrayType(new ArrayType(Number, true), false);
        Assert.False(mutOuterImmutInner.IsAssignableTo(new ArrayType(new ArrayType(Unknown, false), true)));
        Assert.False(immutOuterMutInner.IsAssignableTo(new ArrayType(new ArrayType(Unknown, true), false)));
    }

    [Fact]
    public void FunctionType_Assignability_SameSignature()
    {
        var fn1 = new FunctionType([], [Number], String);
        var fn2 = new FunctionType([], [Number], String);
        Assert.True(fn1.IsAssignableTo(fn2));
        Assert.True(fn2.IsAssignableTo(fn1));
    }

    [Fact]
    public void FunctionType_Assignability_CovariantReturn()
    {
        var fn1 = new FunctionType([], [Number], String);
        var fn2 = new FunctionType([], [Number], Unknown);
        Assert.True(fn1.IsAssignableTo(fn2));
        Assert.False(fn2.IsAssignableTo(fn1));
    }

    [Fact]
    public void FunctionType_Assignability_ContravariantParameters()
    {
        var fn1 = new FunctionType([], [Unknown], String);
        var fn2 = new FunctionType([], [String], String);
        Assert.True(fn1.IsAssignableTo(fn2));
        Assert.True(fn1.IsAssignableTo(fn2));
        Assert.False(fn2.IsAssignableTo(fn1));
    }

    [Fact]
    public void FunctionType_Assignability_ContravariantMultipleParameters()
    {
        var fnWide = new FunctionType([], [Unknown, Unknown], String);
        var fnNarrow = new FunctionType([], [String, Number], String);
        Assert.True(fnWide.IsAssignableTo(fnNarrow));
        Assert.False(fnNarrow.IsAssignableTo(fnWide));
    }

    [Fact]
    public void FunctionType_Assignability_WithTypeParameters()
    {
        var paramT = new TypeParameter("T");
        var paramU = new TypeParameter("U");
        var fn1 = new FunctionType([paramT], [paramT], paramT);
        var fn2 = new FunctionType([paramU], [paramU], paramU);
        Assert.True(fn1.IsAssignableTo(fn2));
        Assert.True(fn2.IsAssignableTo(fn1));
    }

    [Fact]
    public void FunctionType_Assignability_WithInstantiatedTypeArguments()
    {
        var param = new TypeParameter("T");
        var genericFn = new FunctionType([param], [param], param);
        var instantiatedFn = new FunctionType([], [Number], Number);
        Assert.False(genericFn.IsAssignableTo(instantiatedFn));
        Assert.False(instantiatedFn.IsAssignableTo(genericFn));
    }

    [Fact]
    public void FunctionType_Assignability_HigherOrder()
    {
        var innerWide = new FunctionType([], [Number], Unknown);
        var innerNarrow = new FunctionType([], [Number], String);
        var outerWide = new FunctionType([], [innerWide], Bool);
        var outerNarrow = new FunctionType([], [innerNarrow], Bool);
        Assert.False(innerWide.Equals(innerNarrow));
        Assert.False(outerWide.Equals(outerNarrow));
        Assert.True(outerWide.IsAssignableTo(outerNarrow));
        Assert.False(outerNarrow.IsAssignableTo(outerWide));
    }

    [Fact]
    public void FunctionType_Assignability_WithUnionParameter()
    {
        var withUnionParameter = new FunctionType([], [new UnionType([Number, String])], Bool);
        var withNumberParameter = new FunctionType([], [Number], Bool);
        Assert.True(withUnionParameter.IsAssignableTo(withNumberParameter));
        Assert.False(withNumberParameter.IsAssignableTo(withUnionParameter));
    }

    [Fact]
    public void FunctionType_Assignability_WithNeverIntersectionReturn()
    {
        var returnIntersection = new FunctionType([], [Number], new IntersectionType([String, Bool]));
        var returnString = new FunctionType([], [Number], String);
        Assert.True(returnIntersection.IsAssignableTo(returnString));
        Assert.False(returnString.IsAssignableTo(returnIntersection));
    }

    [Fact]
    public void Optional_Assignability()
    {
        var required = String;
        var optional = new OptionalType(required);
        Assert.False(optional.IsAssignableTo(required));
        Assert.True(required.IsAssignableTo(optional));
    }

    [Fact]
    public void Intersection_Assignability()
    {
        var union1 = new UnionType([Number, Bool]);
        var union2 = new UnionType([Bool, String]);
        var intersection = new IntersectionType([union1, union2]);
        Assert.False(None.IsAssignableTo(union1));
        Assert.False(None.IsAssignableTo(union2));
        Assert.False(None.IsAssignableTo(intersection));
        Assert.True(union1.IsAssignableTo(union2));
        Assert.True(union2.IsAssignableTo(union1));
        Assert.True(intersection.IsAssignableTo(union1));
        Assert.True(intersection.IsAssignableTo(union2));
        Assert.True(intersection.IsAssignableTo(intersection));
        Assert.True(Bool.IsAssignableTo(intersection));
        Assert.True(Bool.IsAssignableTo(union1));
        Assert.True(Bool.IsAssignableTo(union2));
        Assert.True(TypeSimplifier.Simplify(intersection).IsAssignableTo(Bool));
    }

    [Fact]
    public void Intersection_Literal_Assignability()
    {
        var union1 = new UnionType([Number, Bool]);
        var union2 = new UnionType([Bool, String]);
        var intersection = new IntersectionType([union1, union2]);
        var literal = new LiteralType(false);
        Assert.True(literal.IsAssignableTo(union1));
        Assert.True(literal.IsAssignableTo(union2));
        Assert.True(union1.IsAssignableTo(union2));
        Assert.True(union2.IsAssignableTo(union1));
        Assert.True(literal.IsAssignableTo(intersection));
        Assert.False(intersection.IsAssignableTo(literal));
        Assert.True(literal.IsAssignableTo(Bool));
        Assert.False(literal.IsAssignableTo(String));
        Assert.False(literal.IsAssignableTo(Number));
    }

    [Fact]
    public void UnionType_Assignability()
    {
        var union1 = new UnionType([Bool, Number]);
        var union2 = new UnionType([Bool, Number, String]);
        Assert.False(union1.IsAssignableTo(Bool));
        Assert.False(None.IsAssignableTo(union1));
        Assert.False(None.IsAssignableTo(union2));
        Assert.True(Bool.IsAssignableTo(union1));
        Assert.True(Bool.IsAssignableTo(union2));
        Assert.True(union2.IsAssignableTo(union1));
        Assert.True(union1.IsAssignableTo(union2));
    }

    [Fact]
    public void UnionType_Assignability_ToNonUnionTarget_RequiresEveryMemberAssignable()
    {
        var memberA = new ObjectType(null, [new ObjectProperty(false, "x", new LiteralType(1))]);
        var memberB = new ObjectType(null, [new ObjectProperty(false, "x", new LiteralType(2))]);
        var union = new UnionType([memberA, memberB]);
        var target = new ObjectType(null, [new ObjectProperty(false, "x", Number)]);

        Assert.True(union.IsAssignableTo(target));
        Assert.False(union.IsAssignableTo(memberA));
    }

    [Fact]
    public void UnionType_Literal_Assignability()
    {
        var union = new UnionType([Bool, Number]);
        var literal = new LiteralType(69.420);
        Assert.False(union.IsAssignableTo(literal));
        Assert.True(literal.IsAssignableTo(union));
        Assert.True(literal.IsAssignableTo(Number));
        Assert.False(literal.IsAssignableTo(Bool));
    }

    [Fact]
    public void Literal_Assignability()
    {
        var integer = new LiteralType(69);
        var number = new LiteralType(420.69);
        Assert.True(integer.IsAssignableTo(integer));
        Assert.True(number.IsAssignableTo(number));
        Assert.False(integer.IsAssignableTo(number));
        Assert.False(number.IsAssignableTo(integer));

        var widenedInteger = Assert.IsType<PrimitiveType>(integer.Widen());
        var widenedNumber = Assert.IsType<PrimitiveType>(number.Widen());
        Assert.Equal(PrimitiveTypeKind.Number, widenedInteger.Kind);
        Assert.Equal(PrimitiveTypeKind.Number, widenedNumber.Kind);
        Assert.True(widenedInteger.IsAssignableTo(widenedNumber));
        Assert.True(integer.IsAssignableTo(widenedInteger));
        Assert.True(integer.IsAssignableTo(widenedNumber));
        Assert.True(number.IsAssignableTo(widenedInteger));
        Assert.True(number.IsAssignableTo(widenedNumber));
        Assert.False(widenedInteger.IsAssignableTo(integer));
        Assert.False(widenedInteger.IsAssignableTo(number));
        Assert.False(widenedNumber.IsAssignableTo(integer));
        Assert.False(widenedNumber.IsAssignableTo(number));
    }

    [Fact]
    public void SizedNumberType_Assignability()
    {
        var u8 = new SizedNumberType(NumberType.U8);
        var u16 = new SizedNumberType(NumberType.U16);

        // Deliberately no width safety: a sized number stays freely, bidirectionally assignable to
        // and from plain number and every other sized number, exactly like the number_type attribute
        // it replaces never enforced bounds either - the width only matters to the serializer.
        Assert.True(u8.IsAssignableTo(Number));
        Assert.True(Number.IsAssignableTo(u8));
        Assert.True(u8.IsAssignableTo(u16));
        Assert.True(u16.IsAssignableTo(u8));

        var literal = new LiteralType(5);
        Assert.True(literal.IsAssignableTo(u8));
    }

    [Fact]
    public void SizedNumberType_Equality()
    {
        var u8 = new SizedNumberType(NumberType.U8);
        var otherU8 = new SizedNumberType(NumberType.U8);
        var u16 = new SizedNumberType(NumberType.U16);

        Assert.Equal(u8, otherU8);
        Assert.NotEqual<Type>(u8, u16);
        Assert.NotEqual<Type>(u8, Number);
        Assert.NotEqual<Type>(Number, u8);
    }

    [Fact]
    public void SizedNumberType_ToString()
    {
        Assert.Equal("u8", new SizedNumberType(NumberType.U8).ToString());
        Assert.Equal("f64", new SizedNumberType(NumberType.F64).ToString());
    }

    [Fact]
    public void SizedStringType_Assignability()
    {
        var u8 = new SizedStringType(NumberType.U8);

        // Same deliberate looseness as SizedNumberType: the length width only matters to the
        // serializer, so a sized string stays freely assignable to and from a plain string.
        Assert.True(u8.IsAssignableTo(String));
        Assert.True(String.IsAssignableTo(u8));

        var literal = new LiteralType("hi");
        Assert.True(literal.IsAssignableTo(u8));
    }

    [Fact]
    public void SizedStringType_Equality()
    {
        var u8 = new SizedStringType(NumberType.U8);
        var otherU8 = new SizedStringType(NumberType.U8);
        var u16 = new SizedStringType(NumberType.U16);

        Assert.Equal(u8, otherU8);
        Assert.NotEqual<Type>(u8, u16);
        Assert.NotEqual<Type>(u8, String);
        Assert.NotEqual<Type>(String, u8);
    }

    [Fact]
    public void SizedStringType_ToString()
    {
        Assert.Equal("string<u8>", new SizedStringType(NumberType.U8).ToString());
        Assert.Equal("string<u32>", new SizedStringType(NumberType.U32).ToString());
    }

    [Fact]
    public void Primitive_Assignability()
    {
        Assert.True(Bool.IsAssignableTo(Bool));
        Assert.False(Bool.IsAssignableTo(String));
        Assert.True(Number.IsAssignableTo(Number));
        Assert.True(String.IsAssignableTo(String));
        Assert.True(None.IsAssignableTo(None));
        Assert.True(Void.IsAssignableTo(Void));
        Assert.True(Void.IsAssignableTo(None));
        Assert.True(None.IsAssignableTo(Void));
        Assert.True(Never.IsAssignableTo(Number));
        Assert.False(Number.IsAssignableTo(Never));
        Assert.False(Unknown.IsAssignableTo(Number));
        Assert.True(Number.IsAssignableTo(Unknown));
        Assert.True(new OptionalType(Number).IsAssignableTo(Unknown));
    }

    [Fact]
    public void TypeVariable_Equality_SameId()
    {
        var a = new TypeVariable(1);
        var b = new TypeVariable(1);
        var c = new TypeVariable(2);

        Assert.True(a.Equals(a));
        Assert.True(a.Equals(b));
        Assert.True(b.Equals(a));
        Assert.False(a.Equals(c));
        Assert.False(b.Equals(c));
        Assert.False(c.Equals(a));
    }

    [Fact]
    public void TypeVariable_Equality_WithOtherTypes()
    {
        var tv = new TypeVariable(0);
        Assert.False(tv.Equals(Number));
        Assert.False(tv.Equals(new LiteralType(42)));
        Assert.False(tv.Equals(null));
    }

    [Fact]
    public void TypeVariable_AsTypeArgument_InFunctionType_DoesNotBreakEquality()
    {
        var fn1 = new FunctionType([new TypeParameter("T")], [Number], Bool);
        var fn2 = new FunctionType([new TypeParameter("T")], [Number], Bool);
        var fn3 = new FunctionType([new TypeParameter("U")], [String], Bool);

        Assert.True(fn1.Equals(fn2));
        Assert.False(fn1.Equals(fn3));
    }

    [Fact]
    public void TypeParameter_Equality_SameName()
    {
        var a = new TypeParameter("T");
        var b = new TypeParameter("T");
        var c = new TypeParameter("U");

        Assert.True(a.Equals(b));
        Assert.True(a.Equals(c));
    }

    [Fact]
    public void TypeParameter_Equality_WithConstraints()
    {
        var a = new TypeParameter("T", Number);
        var b = new TypeParameter("T", Number);
        var c = new TypeParameter("T", String);

        Assert.True(a.Equals(b));
        Assert.False(a.Equals(c));
    }

    [Fact]
    public void TypeParameter_Equality_WithDefaults()
    {
        var a = new TypeParameter("T", null, new LiteralType(0));
        var b = new TypeParameter("T", null, new LiteralType(0));
        var c = new TypeParameter("T", null, new LiteralType(1));

        Assert.True(a.Equals(b));
        Assert.False(a.Equals(c));
    }

    [Fact]
    public void TypeParameter_Equality_WithConstraintAndDefault()
    {
        var a = new TypeParameter("T", Number, new LiteralType(42));
        var b = new TypeParameter("T", Number, new LiteralType(42));
        var c = new TypeParameter("T", Number, new LiteralType(0));
        var d = new TypeParameter("T", String, new LiteralType(42));

        Assert.True(a.Equals(b));
        Assert.False(a.Equals(c));
        Assert.False(a.Equals(d));
    }

    [Fact]
    public void TypeParameter_Equality_IgnoresVariance()
    {
        var a = new TypeParameter("T", variance: Variance.Covariant);
        var b = new TypeParameter("T", variance: Variance.Contravariant);

        Assert.True(a.Equals(b));
    }

    [Fact]
    public void TypeParameter_ToString_DoesNotIncludeVariance()
    {
        var covariant = new TypeParameter("T", variance: Variance.Covariant);
        var invariant = new TypeParameter("T");

        Assert.Equal("T", covariant.ToString());
        Assert.Equal("T", invariant.ToString());
    }

    [Fact]
    public void TypeParameter_WithVariance_PreservesOtherFields()
    {
        var original = new TypeParameter("T", Number, new LiteralType(42));
        var updated = original.WithVariance(Variance.Covariant);

        Assert.Equal(Variance.Covariant, updated.Variance);
        Assert.Equal(original.Name, updated.Name);
        Assert.Equal(original.Constraint, updated.Constraint);
        Assert.Equal(original.DefaultType, updated.DefaultType);
    }

    [Fact]
    public void InterfaceType_Equality_DifferentNames()
    {
        var obj = new ObjectType(null, [new ObjectProperty(false, "x", Number)]);
        var interfaceA = new InterfaceType("A", [], obj);
        var interfaceB = new InterfaceType("B", [], obj);
        var interfaceC = new InterfaceType("C", [interfaceB], obj);
        Assert.True(interfaceA.Equals(interfaceA));
        Assert.True(interfaceA.Equals(interfaceB));
        Assert.True(interfaceB.Equals(interfaceA));
        Assert.True(interfaceC.Equals(interfaceC));
        Assert.False(interfaceB.Equals(interfaceC));
        Assert.False(interfaceA.Equals(interfaceC));
        Assert.False(interfaceC.Equals(interfaceA));
        Assert.False(interfaceC.Equals(interfaceB));
    }

    [Fact]
    public void ObjectType_Equality_EmptyObjects()
    {
        var empty1 = ObjectType.Empty;
        var empty2 = ObjectType.Empty;
        var emptyWithIndexer = new ObjectType(new ObjectIndexer(true, String, Number), []);

        Assert.True(empty1.Equals(empty1));
        Assert.True(empty1.Equals(empty2));
        Assert.False(empty1.Equals(emptyWithIndexer));
    }

    [Fact]
    public void ObjectType_Equality_WithProperties()
    {
        var obj1 = new ObjectType(null, [new ObjectProperty(false, "x", Number), new ObjectProperty(false, "y", Number)]);
        var obj2 = new ObjectType(null, [new ObjectProperty(false, "x", Number), new ObjectProperty(false, "y", Number)]);
        var obj3 = new ObjectType(null, [new ObjectProperty(false, "y", Number), new ObjectProperty(false, "x", Number)]);
        var obj4 = new ObjectType(
            null,
            [new ObjectProperty(false, "x", Number), new ObjectProperty(true, "y", Number)]
        );

        var obj5 = new ObjectType(
            null,
            [new ObjectProperty(false, "x", Number), new ObjectProperty(false, "y", String)]
        );

        var obj6 = new ObjectType(
            null,
            [new ObjectProperty(false, "x", Number)]
        );

        Assert.True(obj1.Equals(obj2));
        Assert.True(obj1.Equals(obj3));
        Assert.False(obj1.Equals(obj4));
        Assert.False(obj1.Equals(obj5));
        Assert.False(obj1.Equals(obj6));
    }

    [Fact]
    public void ObjectType_Equality_WithIndexer()
    {
        var obj1 = new ObjectType(new ObjectIndexer(true, String, Number), []);
        var obj2 = new ObjectType(new ObjectIndexer(true, String, Number), []);
        var obj3 = new ObjectType(new ObjectIndexer(false, String, Number), []);
        var obj4 = new ObjectType(new ObjectIndexer(true, Number, Number), []);
        var obj5 = new ObjectType(new ObjectIndexer(true, String, String), []);
        var obj6 = ObjectType.Empty;
        Assert.True(obj1.Equals(obj2));
        Assert.False(obj1.Equals(obj3));
        Assert.False(obj1.Equals(obj4));
        Assert.False(obj1.Equals(obj5));
        Assert.False(obj1.Equals(obj6));
    }

    [Fact]
    public void ObjectType_Equality_WithPropertiesAndIndexer()
    {
        var obj1 = new ObjectType(
            new ObjectIndexer(true, String, Number),
            [new ObjectProperty(false, "name", String), new ObjectProperty(true, "age", Number)]
        );

        var obj2 = new ObjectType(
            new ObjectIndexer(true, String, Number),
            [new ObjectProperty(false, "name", String), new ObjectProperty(true, "age", Number)]
        );

        var obj3 = new ObjectType(
            new ObjectIndexer(true, String, Number),
            [new ObjectProperty(true, "age", Number), new ObjectProperty(false, "name", String)]
        );

        Assert.True(obj1.Equals(obj2));
        Assert.True(obj1.Equals(obj3));
    }

    [Fact]
    public void ArrayType_Equality()
    {
        var arr1 = new ArrayType(Number, true);
        var arr2 = new ArrayType(Number, true);
        var arr3 = new ArrayType(Number, false);
        var arr4 = new ArrayType(String, true);
        Assert.True(arr1.Equals(arr1));
        Assert.True(arr1.Equals(arr2));
        Assert.False(arr1.Equals(arr3));
        Assert.False(arr1.Equals(arr4));
        Assert.False(arr1.Equals(Number));

        var immut1 = new ArrayType(Bool, false);
        var immut2 = new ArrayType(Bool, false);
        var mutable = new ArrayType(Bool, true);
        Assert.True(immut1.Equals(immut2));
        Assert.False(immut1.Equals(mutable));
    }

    [Fact]
    public void FunctionType_Equality()
    {
        var param1 = new TypeParameter("T");
        var param2 = new TypeParameter("U");
        var fn1 = new FunctionType([param1], [Number], Bool);
        var fn2 = new FunctionType([param1], [Number], Bool);
        var fn3 = new FunctionType([param1], [String], Bool);
        var fn4 = new FunctionType([param2], [Number], Bool);
        var fn5 = new FunctionType([], [Number], Bool);

        Assert.True(fn1.Equals(fn2));
        Assert.False(fn1.Equals(fn3));
        Assert.True(fn1.Equals(fn4));
        Assert.False(fn1.Equals(fn5));
        Assert.False(fn1.Equals(Number));
    }

    [Fact]
    public void Optional_Equality()
    {
        var a = new OptionalType(String);
        var b = new OptionalType(new PrimitiveType(PrimitiveTypeKind.String));
        var c = new OptionalType(Number);
        var d = Number;
        Assert.True(a.Equals(a));
        Assert.True(a.Equals(b));
        Assert.False(a.Equals(c));
        Assert.False(b.Equals(c));
        Assert.False(d.Equals(a));
        Assert.False(d.Equals(b));
        Assert.False(d.Equals(c));
        Assert.False(a.Equals(d));
        Assert.False(b.Equals(d));
        Assert.False(c.Equals(d));
    }

    [Fact]
    public void Intersection_Equality()
    {
        var a = new IntersectionType([new PrimitiveType(PrimitiveTypeKind.Bool), String]);
        var b = new IntersectionType([Bool, new PrimitiveType(PrimitiveTypeKind.String)]);
        var c = new IntersectionType([Number, Bool]);
        var d = new IntersectionType([Bool, Number]);
        Assert.True(a.Equals(a));
        Assert.True(a.Equals(b));
        Assert.True(b.Equals(a));
        Assert.True(c.Equals(d));
        Assert.True(d.Equals(c));
        Assert.False(a.Equals(c));
        Assert.False(b.Equals(c));
    }

    [Fact]
    public void UnionType_Equality()
    {
        var a = new UnionType([new PrimitiveType(PrimitiveTypeKind.Bool), String]);
        var b = new UnionType([Bool, new PrimitiveType(PrimitiveTypeKind.String)]);
        var c = new UnionType([Number, Bool]);
        var d = new UnionType([Bool, Number]);
        Assert.True(a.Equals(a));
        Assert.True(a.Equals(b));
        Assert.True(b.Equals(a));
        Assert.True(c.Equals(d));
        Assert.True(d.Equals(c));
        Assert.False(a.Equals(c));
        Assert.False(b.Equals(c));
    }

    [Fact]
    public void UnionType_Equality_SubsetIsNotEqual()
    {
        var smaller = new UnionType([Number, String]);
        var larger = new UnionType([Number, String, Bool]);

        Assert.False(smaller.Equals(larger));
        Assert.False(larger.Equals(smaller));
        Assert.NotEqual(smaller.GetHashCode(), larger.GetHashCode());
    }

    [Fact]
    public void IntersectionType_Equality_SubsetIsNotEqual()
    {
        var smaller = new IntersectionType([Number, String]);
        var larger = new IntersectionType([Number, String, Bool]);

        Assert.False(smaller.Equals(larger));
        Assert.False(larger.Equals(smaller));
        Assert.NotEqual(smaller.GetHashCode(), larger.GetHashCode());
    }

    [Fact]
    public void Literal_Equality()
    {
        var a = new LiteralType(69);
        var b = new LiteralType(69);
        var c = new LiteralType(420);
        Assert.True(a.Equals(a));
        Assert.True(a.Equals(b));
        Assert.False(a.Equals(c));
        Assert.False(b.Equals(c));
    }

    [Fact]
    public void Primitive_Equality()
    {
        Assert.True(Bool.Equals(Bool));
        Assert.True(Bool.Equals(new PrimitiveType(PrimitiveTypeKind.Bool)));
        Assert.False(Bool.Equals(String));
    }

    [Fact]
    public void TypePredicate_Equality()
    {
        var a = new TypePredicateType(0, Number);
        var b = new TypePredicateType(0, Number);
        var c = new TypePredicateType(1, Number);
        var d = new TypePredicateType(0, String);
        Assert.True(a.Equals(a));
        Assert.True(a.Equals(b));
        Assert.False(a.Equals(c));
        Assert.False(a.Equals(d));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void TypePredicate_DoesNotEqualPlainBool()
    {
        var predicate = new TypePredicateType(0, Number);
        Assert.False(Bool.Equals(predicate));
        Assert.False(predicate.Equals(Bool));
    }

    [Fact]
    public void TypePredicate_IsAssignableToBool()
    {
        var predicate = new TypePredicateType(0, Number);
        Assert.True(predicate.IsAssignableTo(Bool));
    }

    [Fact]
    public void TypePredicate_ToString_RendersAsBool() => Assert.Equal("bool", new TypePredicateType(0, Number).ToString());

    [Fact]
    public void IsNever_ReturnsTrueForNever() => Assert.True(Type.IsNever(Never));

    [Fact]
    public void IsNever_ReturnsFalseForOtherTypes()
    {
        Assert.False(Type.IsNever(Number));
        Assert.False(Type.IsNever(String));
        Assert.False(Type.IsNever(Bool));
        Assert.False(Type.IsNever(Void));
        Assert.False(Type.IsNever(None));
        Assert.False(Type.IsNever(Unknown));
        Assert.False(Type.IsNever(new OptionalType(Number)));
        Assert.False(Type.IsNever(new UnionType([Number, String])));
        Assert.False(Type.IsNever(new IntersectionType([Number, String])));
        Assert.False(Type.IsNever(new LiteralType(42)));
    }

    [Fact]
    public void IsNotNever_ReturnsFalseForNever() => Assert.False(Type.IsNotNever(Never));

    [Fact]
    public void IsNotNever_ReturnsTrueForOtherTypes()
    {
        Assert.True(Type.IsNotNever(Number));
        Assert.True(Type.IsNotNever(String));
        Assert.True(Type.IsNotNever(new OptionalType(Number)));
        Assert.True(Type.IsNotNever(new UnionType([Number, String])));
    }

    [Fact]
    public void IsNone_ReturnsTrueForNoneAndVoid()
    {
        Assert.True(Type.IsNone(None));
        Assert.True(Type.IsNone(Void));
    }

    [Fact]
    public void IsNone_ReturnsFalseForOtherTypes()
    {
        Assert.False(Type.IsNone(Number));
        Assert.False(Type.IsNone(String));
        Assert.False(Type.IsNone(Bool));
        Assert.False(Type.IsNone(Never));
        Assert.False(Type.IsNone(Unknown));
        Assert.False(Type.IsNone(new OptionalType(Number)));
        Assert.False(Type.IsNone(new UnionType([Number, String])));
    }

    [Fact]
    public void IsDefined_ReturnsFalseForNoneAndVoid()
    {
        Assert.False(Type.IsDefined(None));
        Assert.False(Type.IsDefined(Void));
    }

    [Fact]
    public void IsDefined_ReturnsTrueForOtherTypes()
    {
        Assert.True(Type.IsDefined(Number));
        Assert.True(Type.IsDefined(String));
        Assert.True(Type.IsDefined(Bool));
        Assert.True(Type.IsDefined(Never));
        Assert.True(Type.IsDefined(Unknown));
        Assert.True(Type.IsDefined(new OptionalType(Number)));
        Assert.True(Type.IsDefined(new UnionType([Number, String])));
    }

    [Fact]
    public void IsOptional_ReturnsTrueForOptionalType() => Assert.True(Type.IsOptional(new OptionalType(Number)));

    [Fact]
    public void IsOptional_ReturnsTrueForNoneAndVoid()
    {
        Assert.True(Type.IsOptional(None));
        Assert.True(Type.IsOptional(Void));
    }

    [Fact]
    public void IsOptional_ReturnsFalseForRequiredTypes()
    {
        Assert.False(Type.IsOptional(Number));
        Assert.False(Type.IsOptional(String));
        Assert.False(Type.IsOptional(Bool));
        Assert.False(Type.IsOptional(Never));
        Assert.False(Type.IsOptional(new LiteralType(42)));
        Assert.False(Type.IsOptional(new IntersectionType([Number, new OptionalType(String)])));
    }

    [Fact]
    public void IsOptional_ReturnsTrueForUnionContainingNoneOrVoidOrOptional()
    {
        var unionWithNone = new UnionType([Number, None]);
        var unionWithVoid = new UnionType([String, Void]);
        var optional = new OptionalType(Bool);
        var unionWithOptional = new UnionType([Number, optional]);
        var nestedUnion = new UnionType([new UnionType([Number, None]), String]);

        Assert.True(Type.IsOptional(unionWithNone));
        Assert.True(Type.IsOptional(unionWithVoid));
        Assert.True(Type.IsOptional(unionWithOptional));
        Assert.True(Type.IsOptional(nestedUnion));
    }

    [Fact]
    public void IsOptional_ReturnsFalseForUnionWithoutOptionalOrNone()
    {
        var union = new UnionType([Number, String]);
        Assert.False(Type.IsOptional(union));
    }

    [Fact]
    public void IsNotOptional_ReturnsFalseForOptionalNoneAndVoid()
    {
        Assert.False(Type.IsNotOptional(new OptionalType(Number)));
        Assert.False(Type.IsNotOptional(None));
        Assert.False(Type.IsNotOptional(Void));
        Assert.False(Type.IsNotOptional(new UnionType([Number, None])));
    }

    [Fact]
    public void IsNotOptional_ReturnsTrueForRequiredTypes()
    {
        Assert.True(Type.IsNotOptional(Number));
        Assert.True(Type.IsNotOptional(String));
        Assert.True(Type.IsNotOptional(Bool));
        Assert.True(Type.IsNotOptional(new LiteralType(42)));
    }

    [Fact]
    public void NonNullable_OnNoneOrVoid_ReturnsNever()
    {
        Assert.Same(Never, None.NonNullable());
        Assert.Same(Never, Void.NonNullable());
    }

    [Fact]
    public void NonNullable_OnOptionalType_ReturnsNonNullableType()
    {
        var optional = new OptionalType(Number);
        Assert.Same(Number, optional.NonNullable());
    }

    [Fact]
    public void NonNullable_OnOptionalOptionalType_ReturnsInnerType()
    {
        var innerOptional = new OptionalType(String);
        var outerOptional = new OptionalType(innerOptional);
        Assert.Same(String, outerOptional.NonNullable());
    }

    [Fact]
    public void NonNullable_OnRequiredType_ReturnsItself()
    {
        Assert.Same(Number, Number.NonNullable());
        Assert.Same(Never, Never.NonNullable());
        Assert.Same(Unknown, Unknown.NonNullable());

        var literal = new LiteralType(42);
        Assert.Same(literal, literal.NonNullable());

        var intersection = new IntersectionType([Number, String]);
        Assert.Same(intersection, intersection.NonNullable());
    }

    [Fact]
    public void NonNullable_OnUnionWithoutNone_ReturnsItself()
    {
        var union = new UnionType([Number, String]);
        Assert.Same(union, union.NonNullable());
    }

    [Fact]
    public void NonNullable_OnUnionWithNoneOrVoid_RemovesThem()
    {
        var unionWithNone = new UnionType([Number, None, String]);
        var expected1 = new UnionType([Number, String]);
        Assert.True(expected1.Equals(unionWithNone.NonNullable()));

        var unionWithVoid = new UnionType([Number, Void]);
        Assert.True(Number.Equals(unionWithVoid.NonNullable()));
    }

    [Fact]
    public void NonNullable_OnUnionWithOptional_RemovesOptionalWrapper()
    {
        var optional = new OptionalType(Bool);
        var union = new UnionType([Number, optional]);
        var expected = new UnionType([Number, Bool]);
        Assert.True(expected.Equals(union.NonNullable()));
    }

    [Fact]
    public void NonNullable_OnUnionWithOnlyNone_ReturnsNever()
    {
        var union = new UnionType([None]);
        Assert.Same(Never, union.NonNullable());
    }

    [Fact]
    public void NonNullable_OnUnionWithOnlyOptional_ReturnsUnderlyingType()
    {
        var optional = new OptionalType(String);
        var union = new UnionType([optional]);
        Assert.Same(String, union.NonNullable());
    }

    [Fact]
    public void NonNullable_OnComplexUnion_Simplifies()
    {
        var union = new UnionType([Number, new OptionalType(String), None, new UnionType([Bool, Void])]);
        var expected = new UnionType([Number, String, Bool]);
        Assert.True(expected.Equals(union.NonNullable()));
    }

    [Fact]
    public void IsOptional_And_NonNullable_WorkTogether()
    {
        var union = new UnionType([Number, None]);
        Assert.True(Type.IsOptional(union));

        var nonNullable = union.NonNullable();
        Assert.False(Type.IsOptional(nonNullable));
        Assert.Same(Number, nonNullable);
    }

    [Fact]
    public void TypeVariable_ToString()
    {
        Assert.Equal("T0", new TypeVariable(0).ToString());
        Assert.Equal("T1", new TypeVariable(1).ToString());
        Assert.Equal("T42", new TypeVariable(42).ToString());
    }

    [Fact]
    public void TypeVariable_GetHashCode_EqualForSameId() => Assert.Equal(new TypeVariable(7).GetHashCode(), new TypeVariable(7).GetHashCode());

    [Fact]
    public void TupleMarkerType_EqualsOtherTupleMarkerType() => Assert.True(new TupleMarkerType().Equals(new TupleMarkerType()));

    [Fact]
    public void TupleMarkerType_DoesNotEqualOtherTypes() => Assert.False(new TupleMarkerType().Equals(Number));

    [Fact]
    public void TupleMarkerType_IsAssignableToOtherTupleMarkerType() => Assert.True(new TupleMarkerType().IsAssignableTo(new TupleMarkerType()));

    [Fact]
    public void TupleMarkerType_ToString() => Assert.Equal("Tuple", new TupleMarkerType().ToString());

    [Fact]
    public void PrimitiveType_ToString()
    {
        Assert.Equal("number", Number.ToString());
        Assert.Equal("string", String.ToString());
        Assert.Equal("bool", Bool.ToString());
        Assert.Equal("void", Void.ToString());
        Assert.Equal("none", None.ToString());
        Assert.Equal("never", Never.ToString());
        Assert.Equal("unknown", Unknown.ToString());
    }

    [Fact]
    public void OptionalType_ToString()
    {
        var optionalNumber = new OptionalType(Number);
        Assert.Equal("number?", optionalNumber.ToString());

        var optionalString = new OptionalType(String);
        Assert.Equal("string?", optionalString.ToString());

        var optionalOptional = new OptionalType(new OptionalType(Number));
        Assert.Equal("number??", optionalOptional.ToString());

        var optionalArray = new OptionalType(new ArrayType(Number, false));
        Assert.Equal("number[]?", optionalArray.ToString());
    }

    [Fact]
    public void ArrayType_ToString()
    {
        var mutArray = new ArrayType(Number, true);
        Assert.Equal("number[mut]", mutArray.ToString());

        var immutArray = new ArrayType(String, false);
        Assert.Equal("string[]", immutArray.ToString());

        var nestedMutArray = new ArrayType(new ArrayType(Bool, true), false);
        Assert.Equal("bool[mut][]", nestedMutArray.ToString());

        var optionalArray = new ArrayType(new OptionalType(Number), true);
        Assert.Equal("number?[mut]", optionalArray.ToString());

        var arrayOfOptional = new ArrayType(new OptionalType(Number), false);
        Assert.Equal("number?[]", arrayOfOptional.ToString());
    }

    [Fact]
    public void FunctionType_ToString()
    {
        var fn1 = new FunctionType([], [Number], String);
        Assert.Equal("fn(number): string", fn1.ToString());

        var fn2 = new FunctionType([], [Number, String, Bool], Void);
        Assert.Equal("fn(number, string, bool): void", fn2.ToString());

        var fn3 = new FunctionType([], [], Number);
        Assert.Equal("fn(): number", fn3.ToString());

        var paramT = new TypeParameter("T");
        var paramU = new TypeParameter("U");
        var genericFn = new FunctionType([paramT, paramU], [paramT], paramU);
        Assert.Equal("fn<T, U>(T): U", genericFn.ToString());

        var innerFn = new FunctionType([], [Number], String);
        var outerFn = new FunctionType([], [innerFn], Bool);
        Assert.Equal("fn(fn(number): string): bool", outerFn.ToString());

        var complexFn = new FunctionType([], [new ArrayType(Number, true), new OptionalType(String)], new UnionType([Number, String]));
        Assert.Equal("fn(number[mut], string?): number | string", complexFn.ToString());
    }

    [Fact]
    public void TypeParameter_ToString()
    {
        var param = new TypeParameter("T");
        Assert.Equal("T", param.ToString());

        var paramWithConstraint = new TypeParameter("T", new PrimitiveType(PrimitiveTypeKind.Number));
        Assert.Equal("T: number", paramWithConstraint.ToString());

        var paramWithConstraintAndDefault = new TypeParameter("T", new PrimitiveType(PrimitiveTypeKind.Number), new LiteralType(69));
        Assert.Equal("T: number = 69", paramWithConstraintAndDefault.ToString());
    }

    [Fact]
    public void UnionType_ToString()
    {
        var empty = new UnionType([Number]);
        Assert.Equal("number", empty.ToString());

        var union1 = new UnionType([Number, String]);
        Assert.Equal("number | string", union1.ToString());

        var union2 = new UnionType([Number, String, Bool]);
        Assert.Equal("number | string | bool", union2.ToString());

        var union3 = new UnionType([new ArrayType(Number, true), new OptionalType(String), new FunctionType([], [], Void)]);
        Assert.Equal("number[mut] | string? | (fn(): void)", union3.ToString());

        var union4 = new UnionType([Number]);
        Assert.Equal("number", union4.ToString());
    }

    [Fact]
    public void IntersectionType_ToString()
    {
        var intersection1 = new IntersectionType([Number, String]);
        Assert.Equal("number & string", intersection1.ToString());

        var intersection2 = new IntersectionType([Number, String, Bool]);
        Assert.Equal("number & string & bool", intersection2.ToString());

        var intersection3 = new IntersectionType([new ArrayType(Number, true), new OptionalType(String), new FunctionType([], [], Void)]);
        Assert.Equal("number[mut] & string? & (fn(): void)", intersection3.ToString());

        var intersection4 = new IntersectionType([Number]);
        Assert.Equal("number", intersection4.ToString());
    }

    [Fact]
    public void LiteralType_ToString()
    {
        var intLiteral = new LiteralType(42);
        Assert.Equal("42", intLiteral.ToString());

        var floatLiteral = new LiteralType(3.14);
        Assert.Equal("3.14", floatLiteral.ToString());

        var stringLiteral = new LiteralType("hello");
        Assert.Equal("\"hello\"", stringLiteral.ToString());

        var boolLiteral = new LiteralType(true);
        Assert.Equal("true", boolLiteral.ToString());

        var nullLiteral = new LiteralType(null);
        Assert.Equal("none", nullLiteral.ToString());
    }

    [Fact]
    public void ObjectType_ToString_Empty() => Assert.Equal("object", ObjectType.Empty.ToString());

    [Fact]
    public void ObjectType_ToString_WithProperties()
    {
        var obj1 = new ObjectType(null, [new ObjectProperty(false, "x", Number), new ObjectProperty(false, "y", Number)]);
        Assert.Equal("{ x: number, y: number }", obj1.ToString());

        var obj2 = new ObjectType(null, [new ObjectProperty(true, "counter", Number), new ObjectProperty(false, "name", String)]);
        Assert.Equal("{ mut counter: number, name: string }", obj2.ToString());

        var obj3 = new ObjectType(null, [new ObjectProperty(false, "single", Number)]);
        Assert.Equal("{ single: number }", obj3.ToString());
    }

    [Fact]
    public void ObjectType_ToString_WithIndexer()
    {
        var obj1 = new ObjectType(new ObjectIndexer(true, String, Number), []);
        Assert.Equal("{ mut [string]: number }", obj1.ToString());

        var obj2 = new ObjectType(new ObjectIndexer(false, Number, String), []);
        Assert.Equal("{ [number]: string }", obj2.ToString());

        var obj3 = new ObjectType(new ObjectIndexer(true, String, new ArrayType(Number, true)), []);
        Assert.Equal("{ mut [string]: number[mut] }", obj3.ToString());
    }

    [Fact]
    public void ObjectType_ToString_WithPropertiesAndIndexer()
    {
        var obj = new ObjectType(
            new ObjectIndexer(true, String, Number),
            [new ObjectProperty(false, "name", String), new ObjectProperty(true, "age", Number)]
        );

        Assert.Equal("{ mut [string]: number, name: string, mut age: number }", obj.ToString());

        var obj2 = new ObjectType(
            new ObjectIndexer(false, Number, Bool),
            [new ObjectProperty(false, "id", Number)]
        );

        Assert.Equal("{ [number]: bool, id: number }", obj2.ToString());
    }

    [Fact]
    public void ObjectType_ToString_Nested()
    {
        var inner = new ObjectType(null, [new ObjectProperty(false, "value", Number)]);
        var outer = new ObjectType(null, [new ObjectProperty(false, "data", inner)]);
        Assert.Equal("{ data: { value: number } }", outer.ToString());
    }

    [Fact]
    public void ObjectType_ToString_ComplexNested()
    {
        var inner = new ObjectType(
            new ObjectIndexer(false, String, Number),
            [new ObjectProperty(false, "name", String)]
        );

        var outer = new ObjectType(
            new ObjectIndexer(true, Number, inner),
            [new ObjectProperty(false, "items", new ArrayType(inner, true))]
        );

        Assert.Equal("{ mut [number]: { [string]: number, name: string }, items: { [string]: number, name: string }[mut] }", outer.ToString());
    }

    [Fact]
    public void ObjectType_ToString_OrderIndependent()
    {
        var obj1 = new ObjectType(null, [new ObjectProperty(false, "a", Number), new ObjectProperty(false, "b", String)]);
        var obj2 = new ObjectType(null, [new ObjectProperty(false, "b", String), new ObjectProperty(false, "a", Number)]);
        var repr1 = obj1.ToString();
        var repr2 = obj2.ToString();
        Assert.Contains("a: number", repr1);
        Assert.Contains("b: string", repr1);
        Assert.Contains("a: number", repr2);
        Assert.Contains("b: string", repr2);
    }

    [Fact]
    public void InterfaceType_ToString()
    {
        var type = new InterfaceType("Box", [], ObjectType.Empty);
        Assert.Equal("Box", type.ToString());
    }

    [Fact]
    public void ComplexNestedType_ToString()
    {
        var innerObj = new ObjectType(null, [new ObjectProperty(false, "x", Number), new ObjectProperty(false, "y", Number)]);
        var fnType = new FunctionType([], [Number, Number], innerObj);
        var optionalFn = new OptionalType(fnType);
        var arrayOfOptionalFn = new ArrayType(optionalFn, true);

        Assert.Equal("(fn(number, number): { x: number, y: number })?[mut]", arrayOfOptionalFn.ToString());

        var union = new UnionType(
            [
                new ArrayType(Number, false),
                new OptionalType(String),
                new ObjectType(null, [new ObjectProperty(false, "tag", String), new ObjectProperty(false, "value", Number)])
            ]
        );

        Assert.Equal("number[] | string? | { tag: string, value: number }", union.ToString());
    }

    [Fact]
    public void ObjectType_AddProperties_InvalidatesPropertyCache()
    {
        var obj = new ObjectType(null, []);
        Assert.Null(obj.GetProperty("id"));

        var property = new ObjectProperty(false, "id", Number);
        obj.AddProperties([property]);

        Assert.Same(property, obj.GetProperty("id"));
    }

    [Fact]
    public void ObjectType_AddProperties_InvalidatesEqualsAndHashCode()
    {
        var a = new ObjectType(null, []);
        var b = new ObjectType(null, []);
        Assert.True(a.Equals(b));
        var hashBefore = a.GetHashCode();

        var property = new ObjectProperty(false, "id", Number);
        a.AddProperties([property]);

        Assert.False(a.Equals(b));
        Assert.NotEqual(hashBefore, a.GetHashCode());

        b.AddProperties([property]);
        Assert.True(a.Equals(b));
    }

    [Fact]
    public void ObjectType_AddProperties_InvalidatesIsAssignableTo()
    {
        var wide = new ObjectType(null, []);
        var narrow = new ObjectType(null, [new ObjectProperty(false, "id", Number)]);

        Assert.False(wide.IsAssignableTo(narrow));

        wide.AddProperties([new ObjectProperty(false, "id", Number)]);

        Assert.True(wide.IsAssignableTo(narrow));
    }

    [Fact]
    public void InterfaceType_GetProperty_ReflectsOwnObjectTypeGrowthAfterCaching()
    {
        var objectType = new ObjectType(null, []);
        var iface = new InterfaceType("I", [], objectType);

        Assert.Null(iface.GetProperty("id"));

        var property = new ObjectProperty(false, "id", Number);
        objectType.AddProperties([property]);

        Assert.Same(property, iface.GetProperty("id"));
    }

    [Fact]
    public void InterfaceType_Properties_ReflectsConstraintGrowthAfterCaching()
    {
        var baseObjectType = new ObjectType(null, []);
        var baseInterface = new InterfaceType("Base", [], baseObjectType);
        var derived = new InterfaceType("Derived", [baseInterface], new ObjectType(null, []));

        Assert.Empty(derived.Properties);
        Assert.Null(derived.GetProperty("id"));

        var property = new ObjectProperty(false, "id", Number);
        baseObjectType.AddProperties([property]);

        Assert.Contains(property, derived.Properties);
        Assert.Same(property, derived.GetProperty("id"));
    }

    [Fact]
    public void TupleType_Equality_ByArityAndElements()
    {
        var tuple1 = new TupleType([String, Number]);
        var tuple2 = new TupleType([String, Number]);
        var tuple3 = new TupleType([Number, String]);
        var tuple4 = new TupleType([String]);

        Assert.True(tuple1.Equals(tuple2));
        Assert.False(tuple1.Equals(tuple3));
        Assert.False(tuple1.Equals(tuple4));
        Assert.False(tuple1.Equals(new ArrayType(String, false)));
    }

    [Fact]
    public void TupleType_AssignableToTuple_WhenArityAndElementsMatch()
    {
        var source = new TupleType([new LiteralType("abc"), new LiteralType(1L)]);
        var target = new TupleType([String, Number]);
        Assert.True(source.IsAssignableTo(target));
        Assert.False(target.IsAssignableTo(source));
    }

    [Fact]
    public void TupleType_NotAssignable_OnArityMismatch()
    {
        var source = new TupleType([String, Number]);
        var target = new TupleType([String, Number, Bool]);
        Assert.False(source.IsAssignableTo(target));
    }

    [Fact]
    public void TupleType_AssignableToArray_WhenAllElementsAssignable()
    {
        var tuple = new TupleType([String, String]);
        Assert.True(tuple.IsAssignableTo(new ArrayType(String, false)));
        Assert.False(tuple.IsAssignableTo(new ArrayType(Number, false)));
    }

    [Fact]
    public void TupleType_NotAssignableToMutableArray()
    {
        var tuple = new TupleType([String, String]);
        Assert.False(tuple.IsAssignableTo(new ArrayType(String, true)));
    }

    [Fact]
    public void TupleType_LengthProperty_IsLiteralArityType()
    {
        var tuple = new TupleType([String, Number, Bool]);
        var length = tuple.GetProperty("length");
        Assert.NotNull(length);
        Assert.Equal(new LiteralType(3L), length.ValueType);
    }

    [Fact]
    public void TupleType_ToString()
    {
        var tuple = new TupleType([String, Number]);
        Assert.Equal("(string, number)", tuple.ToString());
    }
}
