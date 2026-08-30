using Loom.Core.Diagnostics;


namespace Loom.Testing.TypeChecking;

public partial class SerializationSchemaTest
{
    [Theory]
    [InlineData("name: string?", "size += 4 + #value.name")]
    [InlineData("values: string[]", "size += 4 + #values_element")]
    [InlineData("values: number[]", "#value.values * 4")]
    public void VariableWidthField_IsMeasuredBeforeAllocating(string property, string expected)
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

        Assert.Contains("for i = 1, rows_count do", luau);
        Assert.Contains("for i_2 = 1, rows_element_count do", luau);
        Assert.Contains("size += 4 + #rows_element_element", luau);

        Assert.Contains("const rows_element = value.rows[i]", luau);
        Assert.Contains("const rows_element_element = rows_element[i_2]", luau);
        Assert.DoesNotContain("#value.rows[i][i_2]", luau);

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

        Assert.Contains("size += 4 + #value.label", luau);
        Assert.Contains("size += 4 + #value.payload.message", luau);
        Assert.Contains("#value.points * 6", luau);

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

        Assert.Contains("local entries_count = 0", luau);
        Assert.Contains("buffer_writeu32(b, 0, entries_count)", luau);
        Assert.DoesNotContain("entries_written", luau);

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
                    [cframe_type(CFrameType::Compressed)]
                    aim: CFrame<f32>;
                }

                let payload = serialize_binary(none as never as Snapshot);
                let restored = deserialize_binary::<Snapshot>(payload);
                """,
                true
            )
            .Render();

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

        Assert.Contains("table.insert(blobs, events_element.attacker)", luau);
        Assert.DoesNotContain("value[\"events[]\"]", luau);
    }
}
