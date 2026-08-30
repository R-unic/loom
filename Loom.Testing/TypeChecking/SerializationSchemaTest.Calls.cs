using Loom.Core.Diagnostics;


namespace Loom.Testing.TypeChecking;

public partial class SerializationSchemaTest
{
    [Fact]
    public void ThrowsFor_SerializeBinary_OnNonSerializableInterface()
    {
        var diagnostics = Utility.GetGeneratorDiagnostics(
            """
            interface Plain { id: number }
            let value = new Plain { id: 1 };
            let payload = serialize_binary(value);
            """,
            true
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.NotSerializable,
            "Interface 'Plain' is not serializable.",
            "add the 'serializable' attribute to 'Plain'."
        );
    }

    [Fact]
    public void ThrowsFor_DeserializeBinary_OnNonSerializableInterface()
    {
        var diagnostics = Utility.GetGeneratorDiagnostics(
            """
            interface Plain { id: number }
            let payload = none as never as Serialized;
            let restored = deserialize_binary::<Plain>(payload);
            """,
            true
        );

        Utility.AssertDiagnostic(diagnostics, InternalCodes.NotSerializable, "Interface 'Plain' is not serializable.");
    }
    [Fact]
    public void SerializesInterface_ImportedFromAnotherModule() =>
        Utility.WithTempProject(
            [
                ("packets.loom",
                    """
                    [serializable]
                    export interface MyData {
                        id: u8;
                    }
                    """),
                ("main.server.loom",
                    """
                    import { MyData } from "./packets";
                    let payload = serialize_binary(new MyData { id: 3 });
                    let restored = deserialize_binary::<MyData>(payload);
                    """)
            ],
            (_, result) =>
            {
                Utility.AssertNoErrors(result.Diagnostics);

                var declaring = result.Files.Single(f => f.SourceFile.Name.Contains("packets")).RenderedLuau;
                var consumer = result.Files.Single(f => f.SourceFile.Name.Contains("main")).RenderedLuau;

                Assert.Contains("MyData_serializer = MyData_serializer", declaring);
                Assert.Contains("const MyData_serializer = packets.MyData_serializer", consumer);
                Assert.Contains("MyData_serializer.serialize(", consumer);
                Assert.Contains("MyData_serializer.deserialize(", consumer);
                Assert.DoesNotContain("MyData_serialize_binary(", consumer);
            }
        );

    [Fact]
    public void SerializerOf_MergesEveryConstraintsIndexer_NotJustTheFirst()
    {
        var luau = Utility.GetLuauAST(
                """
                enum Message { ShootGun, Reload }

                [serializable] interface ShootGunPacket { velocity: u8 }
                [serializable] interface ReloadPacket { ammo: u8 }

                declare interface ShootGunEntry { [Message["ShootGun"]]: ShootGunPacket; }
                declare interface ReloadEntry { [Message["Reload"]]: ReloadPacket; }
                declare interface MessageData: ShootGunEntry, ReloadEntry;

                fn get_serializer<K: Message>(message: K): Serializer<MessageData[K]>
                    -> serializer_of::<MessageData, K>(message)
                """,
                true
            )
            .Render();

        Assert.Contains("MessageData_serializer_map = { [0] = ShootGunPacket_serializer, [1] = ReloadPacket_serializer }", luau);
    }

    [Fact]
    public void ArrayOfStrings_MeasuresByWalkingTheValue()
    {
        var luau = Utility.GetLuauAST(
                """
                [serializable] interface Names { values: string[] }

                let payload = serialize_binary(none as never as Names);
                let restored = deserialize_binary::<Names>(payload);
                """,
                true
            )
            .Render();

        Assert.Contains("local size = 0", luau);
        Assert.Contains("const values_element = value.values[i]", luau);
        Assert.Contains("size += 4 + #values_element", luau);
        Assert.Contains("buffer_create(size)", luau);

        Assert.Contains("values_element", luau);
        Assert.DoesNotContain("values[]_", luau);
    }

    [Theory]
    [InlineData("values: bool[]")]
    [InlineData("value: bool[]?")]
    public void ArrayOfBitFields_PacksIntoASharedBlock(string property)
    {
        var luau = Utility.GetLuauAST(
                $$"""
                [serializable] interface Probe {{{property}}}

                let payload = serialize_binary(none as never as Probe);
                let restored = deserialize_binary::<Probe>(payload);
                """,
                true
            )
            .Render();

        Assert.Contains("_bits = offset * 8", luau);
        Assert.Contains("+ 7) // 8", luau);
    }

    [Fact]
    public void ArrayOfNumbers_EmitsLengthPrefixedLoop()
    {
        var luau = Utility.GetLuauAST(
                """
                [serializable] interface Scores {
                    values: u8[];
                }

                let payload = serialize_binary(none as never as Scores);
                let restored = deserialize_binary::<Scores>(payload);
                """,
                true
            )
            .Render();

        Assert.Contains("const values_count = #value.values", luau);
        Assert.Contains("buffer_writeu32(b, 0, values_count)", luau);
        Assert.Contains("for i = 1, values_count do", luau);
        Assert.Contains("offset += 1", luau);

        Assert.Contains("if b_len < offset + values_count then", luau);
    }
    [Fact]
    public void PackedSentinel_SkipsComponentsOnMatch()
    {
        var luau = Utility.GetLuauAST(
                """
                [serializable, packed] interface Entity {
                    position: Vector3<i16>;
                }

                let payload = serialize_binary(none as never as Entity);
                let restored = deserialize_binary::<Entity>(payload);
                """,
                true
            )
            .Render();

        Assert.Contains("if position_value == Vector3.zero then", luau);
        Assert.Contains("buffer_create(1 + (if position_sentinel == 0 then 6 else 0))", luau);
        Assert.Contains("if position_sentinel == 0 then", luau);

        Assert.Contains("kind = \"invalid_tag\"", luau);
    }

    [Fact]
    public void PackedCFrame_KeepsIdentityToASingleByte()
    {
        var luau = Utility.GetLuauAST(
                """
                [serializable, packed] interface Waypoint {
                    frame: CFrame<f32>;
                }

                let payload = serialize_binary(none as never as Waypoint);
                let restored = deserialize_binary::<Waypoint>(payload);
                """,
                true
            )
            .Render();

        Assert.Contains("buffer_create(1 + (if frame_sentinel == 0 then 16 else 0))", luau);

        // The rotation goes out as one u32, past the three position components the addressing folded in.
        Assert.Contains("buffer_writeu32(b, offset + 12, Loom.pack_quaternion(", luau);
    }

    [Fact]
    public void ConditionalPayload_IsBoundsCheckedBeforeReading()
    {
        var luau = Utility.GetLuauAST(
                """
                [serializable] interface Entity {
                    target: i16?;
                }

                let payload = serialize_binary(none as never as Entity);
                let restored = deserialize_binary::<Entity>(payload);
                """,
                true
            )
            .Render();

        Assert.Contains("if b_len < offset + 2 then", luau);
    }


}
