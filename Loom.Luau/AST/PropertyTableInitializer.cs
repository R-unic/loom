namespace Loom.Luau.AST;

public class PropertyTableInitializer(string propertyName, LuauExpression value)
    : TableInitializer(value)
{
    public string PropertyName { get; } = propertyName;

    public override string Render(RenderState state) => $"{PropertyName} = {Value.Render(state)}";
}