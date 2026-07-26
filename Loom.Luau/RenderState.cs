using Loom.Luau.AST;

namespace Loom.Luau;

public sealed class RenderState
{
    private readonly List<string> _indentCache = [""];
    private int _depth;
    public static string Indent { get; set; } = "  ";
    public static char StringDelimiter { get; set; } = '"';

    private string CurrentIndent
    {
        get
        {
            while (_indentCache.Count <= _depth)
                _indentCache.Add(_indentCache[^1] + Indent);

            return _indentCache[_depth];
        }
    }

    public List<string> RenderList<T>(List<T> nodes)
        where T : LuauNode =>
        nodes.ConvertAll(a => a.Render(this));

    public string IndentedLine(string text) => Indented(text) + '\n';
    public string Indented(string text) => CurrentIndent + text;

    public string ParenthesizeIfNeeded(LuauNode node) => RequiresParentheses(node) ? $"({node.Render(this)})" : node.Render(this);

    private static bool RequiresParentheses(LuauNode node) =>
        node is (UnionType or IntersectionType or FunctionType or Table or BinaryOperator or UnaryOperator or IfExpression or TypeCast) and not OptionalType;

    public static string RenderVisibility(LuauVisibility? visibility) =>
        visibility == null
            ? ""
            : visibility.ToString() is { } s
                ? s.ToLower() + " "
                : "";

    public static string Escape(string input) =>
        input
            .Replace("\\", @"\\")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t")
            .Replace("\0", "\\0")
            .Replace("\a", "\\a")
            .Replace("\b", "\\b")
            .Replace("\f", "\\f")
            .Replace("\v", "\\v")
            .Replace(StringDelimiter.ToString(), "\\" + StringDelimiter);

    public string Block(Func<string> callback)
    {
        PushIndent();
        var result = callback();
        PopIndent();

        return result;
    }

    private void PushIndent() => _depth++;
    private void PopIndent() => _depth--;
}