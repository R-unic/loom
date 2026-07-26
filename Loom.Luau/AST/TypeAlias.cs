namespace Loom.Luau.AST;

public class TypeAlias(string name, TypeParameters typeParameters, LuauType type) : LuauStatement
{
    public string Name { get; } = name;
    public TypeParameters TypeParameters { get; } = typeParameters;
    public LuauType Type { get; } = type;
    public bool IsExported { get; set; }

    public override string Render(RenderState state) => $"{(IsExported ? "export " : "")}type {Name}{TypeParameters.Render(state)} = {Type.Render(state)}";
}