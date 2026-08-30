using Loom.Core.TypeChecking.Serialization;

namespace Loom.Testing.TypeChecking;

public partial class SerializationSchemaTest
{
    [Fact]
    public void FixedSizeSchema_HasConstantByteCount()
    {
        var schema = GetSchema(
            """
            [serializable] interface MyData {
                id: u8;
                position: Vector3<i16>;
            }
            """
        );

        Assert.True(schema.IsFixedSize);
        Assert.Equal(0, schema.HeaderBits);
        Assert.Equal(7, schema.FixedByteCount);
        Assert.False(schema.HasBlobs);
    }

    [Fact]
    public void Packed_TradesFixedSizeForSentinelBits()
    {
        var schema = GetSchema(
            """
            [serializable, packed] interface MyData {
                id: u8;
                position: Vector3<i16>;
            }
            """
        );

        Assert.Equal(3, schema.HeaderBits);
        Assert.False(schema.IsFixedSize);
    }

    [Fact]
    public void LiteralTypedProperty_CostsNothing()
    {
        var schema = GetSchema("[serializable] interface MyData { kind: \"spawn\" }");

        Assert.Equal(0, schema.HeaderBits);
        Assert.Equal(0, schema.FixedByteCount);
        Assert.True(schema.IsEmpty);
    }

    [Fact]
    public void BoolsPackIntoHeaderBits()
    {
        var schema = GetSchema("[serializable] interface MyData { a: bool; b: bool; c: bool }");

        Assert.Equal(3, schema.HeaderBits);
        Assert.Equal(1, schema.HeaderBytes);
        Assert.Equal(1, schema.FixedByteCount);
    }

    [Fact]
    public void OptionalAddsPresenceBit()
    {
        var schema = GetSchema(
            """
            [serializable] interface MyData {
                id: u8?;
            }
            """
        );

        Assert.Equal(1, schema.HeaderBits);
        Assert.False(schema.IsFixedSize);
    }

    [Fact]
    public void NumberDefaultsToF32()
    {
        var schema = GetSchema("[serializable] interface MyData { value: number }");

        var numberField = Assert.IsType<NumberField>(Assert.Single(schema.Fields));
        Assert.Equal(NumberType.F32, numberField.NumberType);
        Assert.Equal(4, schema.FixedByteCount);
    }

    [Theory]
    [InlineData("u8", NumberType.U8, 1)]
    [InlineData("i16", NumberType.I16, 2)]
    [InlineData("u32", NumberType.U32, 4)]
    [InlineData("f64", NumberType.F64, 8)]
    public void SizedType_SetsWidthWithNoAttribute(string sizedType, NumberType expected, int byteCount)
    {
        var schema = GetSchema($"[serializable] interface MyData {{ value: {sizedType} }}");

        var numberField = Assert.IsType<NumberField>(Assert.Single(schema.Fields));
        Assert.Equal(expected, numberField.NumberType);
        Assert.Equal(byteCount, schema.FixedByteCount);
    }

    [Fact]
    public void RangedNumberUsesExactBitWidth()
    {
        var schema = GetSchema(
            """
            [serializable] interface MyData {
                [number_range(0, 100)]
                health: number;
            }
            """
        );

        Assert.Equal(7, schema.HeaderBits);
        Assert.Equal(1, schema.FixedByteCount);
    }

    [Fact]
    public void QuantizeSetsGridSpacing()
    {
        var schema = GetSchema(
            """
            [serializable] interface MyData {
                [number_range(0, 1), number_step(0.01)]
                opacity: number;
            }
            """
        );

        Assert.Equal(7, schema.HeaderBits);
    }

    [Fact]
    public void BlobCostsNoBufferBytes()
    {
        var schema = GetSchema(
            """
            [serializable] interface MyData {
                kind: "spawn";
                target: Instance;
            }
            """
        );

        Assert.True(schema.HasBlobs);
        Assert.True(schema.IsEmpty);
    }

    [Fact]
    public void BlobCarriesInstanceClassForChecking()
    {
        var schema = GetSchema("[serializable] interface MyData { part: Part }");

        var blob = Assert.IsType<BlobField>(Assert.Single(schema.Fields));
        Assert.Equal("Instance", blob.TypeofCheck);
        Assert.Equal("Part", blob.InstanceClass);
    }

    [Fact]
    public void IgnoredPropertyIsAbsentFromSchema()
    {
        var schema = GetSchema(
            """
            [serializable] interface MyData {
                id: u8;
                [ignore_serialization]
                cached: string?;
            }
            """
        );

        Assert.Equal("id", Assert.Single(schema.Fields).Path);
    }

    [Fact]
    public void NestedSerializableIsFlattenedIntoParentHeader()
    {
        var schema = GetSchema(
            """
            [serializable] interface Inner { a: bool; b: bool }
            [serializable] interface MyData { flag: bool; inner: Inner }
            """
        );

        Assert.Equal(3, schema.HeaderBits);
        Assert.Equal(1, schema.FixedByteCount);
    }

    [Fact]
    public void LiteralUnionEncodesAsTagWithNoPayload()
    {
        var schema = GetSchema("[serializable] interface MyData { color: \"red\" | \"green\" | \"blue\" }");

        var union = Assert.IsType<UnionField>(Assert.Single(schema.Fields));
        Assert.Equal(UnionDiscrimination.LiteralValue, union.Discrimination);
        Assert.Equal(2, union.TagBits);
        Assert.Equal(0, schema.BodyBytes);
    }

    [Fact]
    public void DiscriminatedUnionDropsTheDiscriminantField()
    {
        var schema = GetSchema(
            """
            interface IAction<Kind: string> { kind: Kind }
            [serializable] interface LogOutAction: IAction<"LogOut">;
            [serializable] interface ClickAction: IAction<"Click"> {
                x: u8;
            }

            [serializable] interface MyData { action: LogOutAction | ClickAction }
            """
        );

        var union = Assert.IsType<UnionField>(Assert.Single(schema.Fields));
        Assert.Equal(UnionDiscrimination.Discriminant, union.Discrimination);
        Assert.Equal("kind", union.DiscriminantName);
        Assert.Equal(1, union.TagBits);

        Assert.Empty(union.Variants[0].Fields);
        Assert.Equal("action.x", Assert.Single(union.Variants[1].Fields).Path);
    }

    [Fact]
    public void TupleWritesNoLengthPrefix()
    {
        var schema = GetSchema(
            """
            [serializable] interface MyData {
                pair: (u8, u8);
            }
            """
        );

        Assert.True(schema.IsFixedSize);
        Assert.Equal(2, schema.FixedByteCount);
    }

    [Fact]
    public void MixedSizedTupleElement_DefaultsUnsizedSiblingToF32()
    {
        var schema = GetSchema(
            """
            [serializable] interface MyData {
                pair: (u8, number);
            }
            """
        );

        Assert.True(schema.IsFixedSize);
        Assert.Equal(5, schema.FixedByteCount);
    }

    [Fact]
    public void ArrayIsVariableSized()
    {
        var schema = GetSchema(
            """
            [serializable] interface MyData {
                ids: u8[];
            }
            """
        );

        Assert.False(schema.IsFixedSize);
        var array = Assert.IsType<ArrayField>(Assert.Single(schema.Fields));
        Assert.Equal(NumberType.U32, array.LengthType);
    }

    [Fact]
    public void ArrayAlias_ResolvesElementAndLengthArgumentsByName()
    {
        var schema = GetSchema(
            """
            [serializable] interface MyData {
                tags: Array<string, u8>;
            }
            """
        );

        var array = Assert.IsType<ArrayField>(Assert.Single(schema.Fields));
        Assert.Equal(NumberType.U8, array.LengthType);
        Assert.IsType<StringField>(array.Element);
    }

    [Fact]
    public void CFrameDefaultsToCompressed()
    {
        var schema = GetSchema(
            """
            [serializable] interface MyData {
                frame: CFrame<f32>;
            }
            """
        );

        var frame = Assert.IsType<CFrameField>(Assert.Single(schema.Fields));
        Assert.Equal(CFrameEncoding.Compressed, frame.Encoding);

        Assert.Equal(32, schema.HeaderBits);
        Assert.Equal(16, schema.FixedByteCount);
    }

    [Theory]
    [InlineData("position: Vector3;")]
    [InlineData("frame: CFrame;")]
    public void BareSizedTypeWithNoTypeArgument_DefaultsToF32(string property)
    {
        var schema = GetSchema(
            $$"""
            [serializable] interface MyData {
                {{property}}
            }
            """
        );

        var field = Assert.Single(schema.Fields);
        var numberType = field switch
        {
            DatatypeField datatype => datatype.NumberType,
            CFrameField cframe => cframe.NumberType,
            _ => throw new InvalidOperationException($"Unexpected field type: {field.GetType()}")
        };

        Assert.Equal(NumberType.F32, numberType);
    }

    [Fact]
    public void PreciseCFrameSpendsFourComponentsOnRotation()
    {
        var schema = GetSchema(
            """
            [serializable] interface MyData {
                [cframe_type(CFrameType::Precise)]
                frame: CFrame<f32>;
            }
            """
        );

        Assert.Equal(0, schema.HeaderBits);
        Assert.Equal(28, schema.FixedByteCount);
    }
}
