namespace Loom.Luau.AST;

public sealed class TableTypeIndexer(LuauVisibility? visibility, LuauType? keyType, LuauType valueType) : LuauType
{
    public LuauVisibility? Visibility { get; } = visibility;
    public LuauType? KeyType { get; } = keyType;
    public LuauType ValueType { get; } = valueType;

    public override string Render(RenderState state) =>
        KeyType != null ? $"{RenderState.RenderVisibility(Visibility)}[{KeyType.Render(state)}]: {ValueType.Render(state)}" : ValueType.Render(state);
}