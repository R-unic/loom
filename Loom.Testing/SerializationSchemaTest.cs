using Loom.Core.Diagnostics;
using Loom.Core.TypeChecking.Serialization;

namespace Loom.Testing;

[Collection("Assembly")]
public class SerializationSchemaTest
{
    private static SerializationSchema GetSchema(string source, string interfaceName = "MyData")
    {
        var (_, semanticModel, flowAnalyzer) = Utility.FlowAnalyze(source);
        var result = new Core.TypeChecking.TypeChecker(semanticModel, flowAnalyzer).Check();
        Utility.AssertNoErrors(result.Diagnostics);

        var schema = semanticModel.SerializationSchemas
            .FirstOrDefault(pair => pair.Key.Name == interfaceName)
            .Value;

        Assert.NotNull(schema);
        return schema;
    }

    #region AttributeMatrix
    [Fact]
    public void ThrowsFor_Packed_WithoutSerializable()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("[packed] interface MyData { id: number }");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.MissingRequiredAttribute,
            "'packed' requires interface 'MyData' to also have the 'serializable' attribute.",
            "'packed' only changes how a serializable type is encoded."
        );
    }

    [Fact]
    public void ThrowsFor_PropertyAttribute_OnNonSerializableInterface()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface MyData {
                [number_range(0, 100)]
                id: number;
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.MissingRequiredAttribute,
            "'number_range' requires interface 'MyData' to have the 'serializable' attribute.",
            "add 'serializable' to 'MyData', or remove the attribute from 'id'."
        );
    }

    [Fact]
    public void ThrowsFor_NumberRange_OnSizedType()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            [serializable] interface MyData {
                [number_range(0, 100)]
                health: u8;
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.ConflictingAttributes,
            "'health' is already 'u8', so 'number_range' has nothing left to set.",
            "remove 'number_range', or declare 'health: number' to use a bounded range instead."
        );
    }

    [Fact]
    public void ThrowsFor_Quantize_WithoutNumberRange()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            [serializable] interface MyData {
                [number_step(0.01)]
                opacity: number;
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.MissingRequiredAttribute,
            "'number_step' on 'opacity' requires 'number_range'.",
            "without bounds there is no bit width to derive from a step."
        );
    }

    [Fact]
    public void ThrowsFor_IgnoreSerialization_OnRequiredProperty()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            [serializable] interface MyData {
                [ignore_serialization]
                cached: string;
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidAttributeTargetType,
            "'ignore_serialization' requires 'cached' to be optional, since there is no default value to restore.",
            "declare it as 'cached: string?'."
        );
    }

    [Fact]
    public void ThrowsFor_IgnoreSerialization_WithEncodingAttribute()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            [serializable] interface MyData {
                [ignore_serialization, number_range(0, 100)]
                cached: number?;
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.ConflictingAttributes,
            "'cached' is both ignored and annotated with 'number_range'.",
            "an ignored property is not encoded, so it cannot carry encoding attributes."
        );
    }

    [Fact]
    public void ThrowsFor_LengthType_NoLongerExists()
    {
        // Same treatment as number_type: string<u8>/Array<T, u8> replace every use it had, so it isn't
        // declared at all anymore rather than kept around as an always-invalid stub.
        var diagnostics = Utility.GetSemanticModel(
            """
            [serializable] interface MyData {
                [length_type(NumberType.U8)]
                name: string;
            }
            """
        ).Diagnostics;

        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find name 'length_type'.");
    }

    [Fact]
    public void ThrowsFor_CFrameType_OnNonCFrameProperty()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            [serializable] interface MyData {
                [cframe_type(CFrameType.Precise)]
                position: Vector3;
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidAttributeTargetType,
            "'cframe_type' requires 'position' to be a CFrame, but it is 'Vector3<f32>'."
        );
    }

    [Fact]
    public void ThrowsFor_NumberType_NoLongerExists()
    {
        // number_type isn't merely invalid on any particular target anymore - it isn't declared at all,
        // now that every property it used to configure has a type-level replacement (a sized type,
        // Vector3/Vector2/CFrame's own <T>) or, for the other 7 Roblox datatypes and an all-numeric
        // tuple, no replacement, just a fixed f32 default.
        var diagnostics = Utility.GetSemanticModel(
            """
            [serializable] interface MyData {
                [number_type(NumberType.I16)]
                position: Vector3;
            }
            """
        ).Diagnostics;

        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find name 'number_type'.");
    }
    #endregion AttributeMatrix

    #region Encodability
    [Fact]
    public void ThrowsFor_NestedNonSerializableInterface()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface Inner { value: number }
            [serializable] interface MyData { inner: Inner }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.NotSerializable,
            "'inner' has type 'Inner', which is not serializable.",
            "add the 'serializable' attribute to interface 'Inner'."
        );
    }

    [Fact]
    public void ThrowsFor_RecursiveSerializableType()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("[serializable] interface Node { next: Node }");
        Assert.NotNull(diagnostics.Find(d => d.Code == InternalCodes.RecursiveSerializableType));
    }

    [Fact]
    public void ThrowsFor_AmbiguousUnion()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            [serializable] interface Circle { radius: number }
            [serializable] interface Square { side: number }
            [serializable] interface MyData { shape: Circle | Square }
            """
        );

        Assert.NotNull(diagnostics.Find(d => d.Code == InternalCodes.AmbiguousSerializableUnion));
    }

    [Theory]
    [InlineData("Vector2int16", "Vector2")]
    [InlineData("Vector3int16", "Vector3")]
    public void ThrowsFor_Int16Datatype_PointingAtItsGenericReplacement(string datatype, string replacement)
    {
        // Vector2int16/Vector3int16 are permanently unserializable now - Vector2<i16>/Vector3<i16> already
        // say the same thing with a configurable width, so there is no reason to keep both around.
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            $$"""
            [serializable] interface MyData {
                position: {{datatype}};
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.NotSerializable,
            $"'position' has type '{datatype}', which cannot be serialized.",
            $"use '{replacement}<i16>' instead - its components are already i16, and the width is configurable."
        );
    }
    #endregion Encodability

    #region Calls
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

                // The codec is emitted and exported once, and the consumer reaches it through the import
                // rather than through function names local to the declaring file.
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
        // A dispatch table is typically built by merging several single-key interfaces through
        // inheritance, each contributing its own '[Message["..."]]: ...Packet' indexer - the map has to
        // read every constraint's indexer, not just whichever one is reached first.
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

        // A variable-width element cannot state its width as an expression, so the total is accumulated
        // by walking the value before the buffer is allocated.
        Assert.Contains("local size = 0", luau);
        Assert.Contains("const values_element = value.values[i]", luau);
        Assert.Contains("size += 4 + #values_element", luau);
        Assert.Contains("buffer_create(size)", luau);

        // Element locals must not collide with the collection's own, nor carry brackets into a name.
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

        // Entries share a block reserved after the length prefix, so the bodies stay byte-aligned and
        // eight bools cost one byte rather than eight.
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

        // The length is named, since the prefix and the loop bound both want it.
        Assert.Contains("const values_count = #value.values", luau);
        Assert.Contains("buffer_writeu32(b, 0, values_count)", luau);
        Assert.Contains("for i = 1, values_count do", luau);
        Assert.Contains("offset += 1", luau);

        // The count is bounds-checked before the loop rather than running off the end element by element.
        // A one-byte element folds away the scale, so the bound is just the count.
        Assert.Contains("if buffer_len(b) < offset + values_count then", luau);
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

        // The sentinel resolves before the allocation, because a match writes no components at all.
        Assert.Contains("if position_value == Vector3.zero then", luau);
        Assert.Contains("buffer_create(1 + (if position_sentinel == 0 then 6 else 0))", luau);
        Assert.Contains("if position_sentinel == 0 then", luau);

        // Reserved tags decode to nothing the type allows, so they report instead.
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

        // Header bits are paid for unconditionally, so a sentinelled rotation moves to the body where it
        // rides behind the same conditional as the position - identity costs one byte, not five.
        Assert.Contains("buffer_create(1 + (if frame_sentinel == 0 then 16 else 0))", luau);
        Assert.Contains("buffer_writeu32(b, offset, Loom.pack_quaternion(", luau);
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

        // The up-front minimum only covers what every payload carries, so a branch the sender chose to
        // take has to prove its bytes are present rather than throwing out of the read.
        Assert.Contains("if buffer_len(b) < offset + 2 then", luau);
    }
    #region Unions
    private const string ActionUnion =
        """
        interface IAction<Kind: string> { kind: Kind }
        [serializable] interface LogOutAction: IAction<"LogOut">;
        [serializable] interface ClickAction: IAction<"Click"> {
            x: u8;
        }

        [serializable] interface Envelope { action: LogOutAction | ClickAction }

        let payload = serialize_binary(none as never as Envelope);
        let restored = deserialize_binary::<Envelope>(payload);
        """;

    [Fact]
    public void DiscriminatedUnion_RestoresDiscriminantFromTheTag()
    {
        var luau = Utility.GetLuauAST(ActionUnion, true).Render();

        Assert.Contains("if action_value.kind == \"Click\" then", luau);
        Assert.Contains("buffer_writebits(b, 0, 1, action_tag)", luau);

        // The tag carries 'kind', so it costs nothing on the wire and is rebuilt on the way back.
        Assert.Contains("action = { kind = \"LogOut\" }", luau);
        Assert.Contains("action = { kind = \"Click\", x = ", luau);
        Assert.DoesNotContain("value.action.kind)", luau);
    }

    [Fact]
    public void DiscriminatedUnion_SizesPerVariant()
    {
        var luau = Utility.GetLuauAST(ActionUnion, true).Render();

        // The empty variant adds nothing, so only the one with a payload gets a branch.
        Assert.Contains("if action_tag == 1 then", luau);
        Assert.Contains("size += 1", luau);
        Assert.DoesNotContain("size += 0", luau);
    }

    [Fact]
    public void LiteralUnion_IsTagOnly()
    {
        var luau = Utility.GetLuauAST(
                """
                [serializable] interface Paint { color: "red" | "green" | "blue" }

                let payload = serialize_binary(none as never as Paint);
                let restored = deserialize_binary::<Paint>(payload);
                """,
                true
            )
            .Render();

        // Three variants fit in two bits and the value is the tag, so nothing follows it.
        Assert.Contains("buffer_writebits(b, 0, 2, color_tag)", luau);
        Assert.Contains("color = \"red\"", luau);
        Assert.Contains("buffer_create(1)", luau);
    }

    [Fact]
    public void RuntimeKindUnion_DiscriminatesWithTypeof()
    {
        var luau = Utility.GetLuauAST(
                """
                [serializable] interface Cell { content: number | string }

                let payload = serialize_binary(none as never as Cell);
                let restored = deserialize_binary::<Cell>(payload);
                """,
                true
            )
            .Render();

        Assert.Contains("if typeof(content_value) == \"string\" then", luau);

        // A variant carrying a string has to be measured, not just its fixed part, or the allocation
        // would be short before the variant's own writes began.
        Assert.Contains("size += 4 + #value.content", luau);
    }

    [Fact]
    public void Union_ReportsTagsOutsideTheDeclaredVariants()
    {
        var luau = Utility.GetLuauAST(ActionUnion, true).Render();
        Assert.Contains("kind = \"invalid_tag\", field = \"action\"", luau);
    }

    [Fact]
    public void Union_BoundsChecksVariantPayload()
    {
        var luau = Utility.GetLuauAST(ActionUnion, true).Render();

        // The minimum only covers the tag, so a variant the sender chose has to prove its bytes exist.
        Assert.Contains("if buffer_len(b) < offset + 1 then", luau);
    }

    [Fact]
    public void Union_ResolvesVariantsThatShadowIntrinsicNames()
    {
        // 'Path' is also a Roblox class, and resolving the intrinsic instead would leave a perfectly
        // serializable variant looking unserializable.
        var diagnostics = Utility.GetGeneratorDiagnostics(
            """
            interface IShape<Kind: string> { kind: Kind }
            [serializable] interface Dot: IShape<"Dot">;
            [serializable] interface Path: IShape<"Path"> {
                n: u8;
            }

            [serializable] interface Drawing { shape: Dot | Path }
            """,
            true
        );

        Utility.AssertNoErrors(diagnostics);
    }

    #endregion Unions

    #region Measurability
    [Theory]
    [InlineData("name: string?", "size += 4 + #value.name")]
    [InlineData("values: string[]", "size += 4 + #values_element")]
    [InlineData("values: number[]", "#value.values * 4")]
    public void VariableWidthField_IsMeasuredBeforeAllocating(string property, string expected)
    {
        // Everything is allocated before a byte is written, so a width left out of the measure leaves
        // the buffer short and the writes running off the end.
        var luau = Utility.GetLuauAST(
                $$"""
                [serializable] interface Probe {{{property}}}

                let payload = serialize_binary(none as never as Probe);
                let restored = deserialize_binary::<Probe>(payload);
                """,
                true
            )
            .Render();
        Assert.Contains(expected, luau);
    }

    [Fact]
    public void NestedArray_MeasuresWithALoopPerLevel()
    {
        var luau = Utility.GetLuauAST(
                """
                [serializable] interface Grid { rows: string[][] }

                let payload = serialize_binary(none as never as Grid);
                let restored = deserialize_binary::<Grid>(payload);
                """,
                true
            )
            .Render();

        // A counter per level, or an inner loop would clobber the outer's, and a length prefix per level.
        Assert.Contains("for i = 1, rows_count do", luau);
        Assert.Contains("for i_2 = 1, rows_element_count do", luau);
        Assert.Contains("size += 4 + #rows_element_element", luau);

        // Each level measures off the element the level above bound, not off the parameter: measuring an
        // inner element by spelling out every level above it costs a lookup per level, per entry.
        Assert.Contains("const rows_element = value.rows[i]", luau);
        Assert.Contains("const rows_element_element = rows_element[i_2]", luau);
        Assert.DoesNotContain("#value.rows[i][i_2]", luau);

        // Nested paths carry a bracket group per level; stopping after the first leaves the rest in the
        // name and produces something that is not an identifier at all.
        Assert.Contains("rows_element_element_length", luau);
        Assert.DoesNotContain("rows_][", luau);
    }

    [Fact]
    public void CombinedFieldKinds_ComposeIntoOneSchema()
    {
        var luau = Utility.GetLuauAST(
                """
                interface IEvent<Kind: string> { kind: Kind }
                [serializable] interface Ping: IEvent<"Ping">;
                [serializable] interface Chat: IEvent<"Chat"> {
                    message: string;
                    [number_range(0, 100)]
                    volume: number;
                }

                [serializable] interface Inner {
                    flag: bool;
                    code: u8;
                }

                [serializable] interface KitchenSink {
                    tag: "sink";
                    inner: Inner;
                    label: string?;
                    points: Vector3<i16>[];
                    payload: Ping | Chat;
                    owner: Part;
                }

                let sink_payload = serialize_binary(none as never as KitchenSink);
                let sink_restored = deserialize_binary::<KitchenSink>(sink_payload);
                """,
                true
            )
            .Render();

        // Bits from the nested struct, the optional's presence, the union tag, and the selected
        // variant's ranged number all share one header; every variable part is measured separately.
        Assert.Contains("size += 4 + #value.label", luau);
        Assert.Contains("size += 4 + #value.payload.message", luau);
        Assert.Contains("#value.points * 6", luau);

        // The literal-typed tag and the blob both stay off the wire.
        Assert.Contains("tag = \"sink\"", luau);
        Assert.Contains("table.insert(blobs, value.owner)", luau);
    }
    [Fact]
    public void NestedStruct_IsRebuiltNested()
    {
        var luau = Utility.GetLuauAST(
                """
                [serializable] interface Inner {
                    flag: bool;
                    code: u8;
                }

                [serializable] interface Outer { inner: Inner }

                let payload = serialize_binary(none as never as Outer);
                let restored = deserialize_binary::<Outer>(payload);
                """,
                true
            )
            .Render();

        // Flattening puts the nested properties under dotted paths; reading them back into a flat table
        // would hand the caller the wrong shape entirely. Inner's own serializer is still flat, so the
        // nesting has to be asserted on Outer's return specifically.
        Assert.Contains("value = { inner = { flag = ", luau);
    }

    [Fact]
    public void InnerRead_DoesNotShadowItsAccumulator()
    {
        var luau = Utility.GetLuauAST(
                """
                [serializable] interface Probe { nickname: string? }

                let payload = serialize_binary(none as never as Probe);
                let restored = deserialize_binary::<Probe>(payload);
                """,
                true
            )
            .Render();

        // The optional's accumulator and the string read filling it both want the leaf name; if the
        // inner binding shadows the outer, the assignment writes to itself and the value stays nil.
        Assert.Contains("local nickname = nil", luau);
        Assert.Contains("nickname_2 = buffer_readstring", luau);
        Assert.Contains("nickname = nickname_2", luau);
    }

    [Fact]
    public void NestedStructWithVariableField_IsMeasured()
    {
        var luau = Utility.GetLuauAST(
                """
                [serializable] interface Profile { nickname: string? }
                [serializable] interface Account { profile: Profile }

                let payload = serialize_binary(none as never as Account);
                let restored = deserialize_binary::<Account>(payload);
                """,
                true
            )
            .Render();

        Assert.Contains("size += 4 + #value.profile.nickname", luau);
    }
    [Fact]
    public void ThrowsFor_RangeWiderThanWritebitsCarries()
    {
        // Anything past 32 bits would clamp, silently dropping the top of every value.
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            [serializable] interface Counter {
                [number_range(0, 4294967296)]
                total: number;
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidSerializationRange,
            "'total' needs more than 32 bits for range [0, 4294967296] with a step of 1.",
            "narrow the range, widen the step, or use a sized type (e.g. 'u32') for a byte-aligned width."
        );
    }

    [Fact]
    public void Allows_RangeSpanningExactlyThirtyTwoBits()
    {
        var luau = Utility.GetLuauAST(
                """
                [serializable] interface Counter {
                    [number_range(0, 4294967295)]
                    total: number;
                }

                let payload = serialize_binary(none as never as Counter);
                let restored = deserialize_binary::<Counter>(payload);
                """,
                true
            )
            .Render();

        Assert.Contains("buffer_writebits(b, 0, 32,", luau);
    }

    [Fact]
    public void ThrowsFor_InterfaceThatIsItselfAnIndexer()
    {
        // The map encoding lives on a property; an interface that is only an indexer has nothing for the
        // schema to name, and used to serialize to nothing at all.
        var diagnostics = Utility.GetTypeCheckerDiagnostics("[serializable] interface Lookup { [string]: number }");

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.NotSerializable,
            "Serializable interface 'Lookup' is itself an indexer, so it has no properties to serialize.",
            "hold the map in a property instead - 'interface Lookup { entries: Record<K, V> }' encodes as pairs."
        );
    }

    [Fact]
    public void Map_EncodesAsCountPrefixedPairs()
    {
        var luau = Utility.GetLuauAST(
                """
                [serializable] interface Scores { entries: Record<string, number> }

                let payload = serialize_binary(none as never as Scores);
                let restored = deserialize_binary::<Scores>(payload);
                """,
                true
            )
            .Render();

        // A map has no length operator, so the size pass counts by walking. The write reuses that count
        // for its length prefix rather than walking a second time.
        Assert.Contains("local entries_count = 0", luau);
        Assert.Contains("buffer_writeu32(b, 0, entries_count)", luau);
        Assert.DoesNotContain("entries_written", luau);

        // Pairs go out and come back keyed, not positional.
        Assert.Contains("for entries_key, entries_value in value.entries do", luau);
        Assert.Contains("entries[entries_key] = ", luau);
    }
    [Fact]
    public void ChainedConditionalSizes_AreParenthesised()
    {
        var luau = Utility.GetLuauAST(
                """
                [serializable, packed] interface Snapshot {
                    name: string;
                    velocity: Vector3<i16>;
                    [cframe_type(CFrameType.Compressed)]
                    aim: CFrame<f32>;
                }

                let payload = serialize_binary(none as never as Snapshot);
                let restored = deserialize_binary::<Snapshot>(payload);
                """,
                true
            )
            .Render();

        // Luau binds an if-expression loosely enough that its else branch swallows whatever follows, so
        // a sum of several would nest rather than add up and the buffer would come out short.
        Assert.Contains("+ (if velocity_sentinel == 0 then 6 else 0)", luau);
        Assert.Contains("+ (if aim_sentinel == 0 then 16 else 0)", luau);
    }

    [Fact]
    public void OptionalInsideVariantInsideArray_ReadsFromTheEntry()
    {
        var luau = Utility.GetLuauAST(
                """
                interface IEvent<Kind: string> { kind: Kind }
                [serializable] interface Hit: IEvent<"Hit"> { attacker: Player? }
                [serializable] interface Log { events: Hit[] }

                let payload = serialize_binary(none as never as Log);
                let restored = deserialize_binary::<Log>(payload);
                """,
                true
            )
            .Render();

        // The optional's payload has to come from the loop entry; through the value parameter it would
        // index a property literally named 'events[]'.
        Assert.Contains("table.insert(blobs, events_element.attacker)", luau);
        Assert.DoesNotContain("value[\"events[]\"]", luau);
    }
    #endregion Measurability

    #endregion Calls

    #region Layout
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

        // Five Vector3 sentinels plus the reserved "components follow" state need three bits, and the
        // components become conditional - so the type stops being fixed-size.
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

        // 101 states fit in 7 bits, versus the 32 an unannotated f32 would spend.
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

        // One shared header rather than a partial byte per nesting level.
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

        // The tag carries 'kind', so no variant re-encodes it.
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
        // A sized element picks its own width; an unsized sibling has no shared attribute to fall back
        // to anymore, so it always takes the f32 default.
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
        // T and L are looked up by parameter name, not position - this guards against a positional
        // read silently swapping which argument means "element type" and which means "length width".
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

        // 32 bits of rotation plus a 12-byte position.
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
                [cframe_type(CFrameType.Precise)]
                frame: CFrame<f32>;
            }
            """
        );

        // Unlike Compressed, Precise writes four ordinary components next to the position rather than
        // packing the rotation into header bits - bit-writing a float would truncate it to an integer.
        Assert.Equal(0, schema.HeaderBits);
        Assert.Equal(28, schema.FixedByteCount);
    }
    #endregion Layout
}
