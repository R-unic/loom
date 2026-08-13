namespace Loom.Luau.AST;

/// <summary>
///     A variadic type pack — <c>...T</c> — for a generic whose parameter is a pack rather than a type,
///     such as the runtime's <c>Event&lt;T... = ...any&gt;</c>. Passing it the array a rest parameter
///     declares would say the handler takes one table, not any number of its elements.
/// </summary>
public class VariadicTypePack(LuauType elementType) : LuauType
{
    public LuauType ElementType { get; } = elementType;

    public override string Render(RenderState state) => $"...{ElementType.Render(state)}";
}
