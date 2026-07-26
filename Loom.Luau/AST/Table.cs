namespace Loom.Luau.AST;

public class Table(List<TableInitializer> initializers) : LuauExpression
{
    public static readonly Table Empty = new([]);

    public List<TableInitializer> Initializers { get; } = initializers;

    public override string Render(RenderState state)
    {
        if (Initializers.Count > 5)
            return "{\n"
                + state.Block(() => string.Join("", state.RenderList(Initializers).ConvertAll(s => s + ',').ConvertAll(state.IndentedLine)))
                + state.Indented("}");

        var spacing = Initializers.Any(i => i is PropertyTableInitializer or ComputedPropertyTableInitializer) ? " " : "";
        return $"{{{spacing}{string.Join(", ", state.RenderList(Initializers))}{spacing}}}";
    }
}