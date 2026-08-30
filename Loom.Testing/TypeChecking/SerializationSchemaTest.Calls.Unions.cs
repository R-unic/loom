namespace Loom.Testing.TypeChecking;

public partial class SerializationSchemaTest
{
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

        Assert.Contains("action = { kind = \"LogOut\" }", luau);
        Assert.Contains("action = { kind = \"Click\", x = ", luau);
        Assert.DoesNotContain("value.action.kind)", luau);
    }

    [Fact]
    public void DiscriminatedUnion_SizesPerVariant()
    {
        var luau = Utility.GetLuauAST(ActionUnion, true).Render();

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

        Assert.Contains("if b_len < offset + 1 then", luau);
    }

    [Fact]
    public void Union_ResolvesVariantsThatShadowIntrinsicNames()
    {
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

}
