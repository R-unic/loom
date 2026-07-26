namespace Loom.Luau.AST;

public class TableType(TableTypeIndexer? indexer, List<TableTypeProperty> properties) : LuauType
{
    public TableTypeIndexer? Indexer { get; } = indexer;
    public List<TableTypeProperty> Properties { get; } = properties;
    public static TableType Array(LuauType elementType) => new(new TableTypeIndexer(null, null, elementType), []);

    public override string Render(RenderState state) =>
        Properties.Count > 0
            ? state.IndentedLine("{")
            + state.Block(() => (Indexer != null ? state.IndentedLine(Indexer.Render(state) + ",") : "")
                + string.Join("", state.RenderList(Properties).ConvertAll(p => state.IndentedLine(p + ",")))
            )
            + state.Indented("}")
            : Indexer != null
                ? $"{{ {Indexer.Render(state)} }}"
                : "{}";
}