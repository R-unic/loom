namespace Loom.Luau.AST;

public class Function(
    string name,
    TypeParameters? typeParameters,
    List<Parameter> parameters,
    LuauType? returnType,
    Chunk body,
    bool isConst = true
) : LuauStatement
{
    public string Name { get; } = name;
    public TypeParameters? TypeParameters { get; } = typeParameters;
    public List<Parameter> Parameters { get; } = parameters;
    public LuauType? ReturnType { get; } = returnType;
    public Chunk Body { get; } = body;
    public bool IsConst { get; } = isConst;

    public override string Render(RenderState state) =>
        $"{(IsConst ? "const " : "")}function "
        + Name
        + (TypeParameters?.Render(state) ?? "")
        + '('
        + string.Join(", ", Parameters.ConvertAll(p => p.Render(state)))
        + ')'
        + (ReturnType != null ? ": " + ReturnType.Render(state) : "")
        + '\n'
        + Body.Render(state)
        + "end";
}