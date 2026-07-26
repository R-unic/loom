using System.Reflection;
using Loom.Core.Parsing.AST;
using Loom.Core.Text;

namespace Loom.Core.Debug;

public static class AstInspector
{
    private static int _indent;
    private static readonly HashSet<string> _ignoredProperties =
    [
        "Parent",
        "Span",
        "LocationSpan",
        "Tokens",
        "Children",
        "Id",
        "File",
        "Keyword"
    ];

    public static string Inspect(Tree tree) => string.Join(Environment.NewLine, tree.Statements.ConvertAll(Inspect));

    private static string Inspect(object node)
    {
        var type = node.GetType();
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => !_ignoredProperties.Contains(p.Name) && p.GetIndexParameters().Length == 0);

        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => !_ignoredProperties.Contains(f.Name));

        _indent++;
        var members = (
            from prop in properties
            let value = prop.GetValue(node)
            let display = FormatValue(value)
            select Indented(display.Split('(').First() == prop.Name ? display : $"{prop.Name}: {display}")).ToList();

        _indent--;

        members.AddRange(
            from field in fields
            let value = field.GetValue(node)
            let display = FormatValue(value)
            select Indented(display.Split('(').First() == field.Name ? display : $"{field.Name}: {display}")
        );

        return type.Name + "(\n" + string.Join('\n', members) + "\n" + Indented(")");
    }

    private static string FormatValue(object? value)
    {
        switch (value)
        {
            case null:
                return "null";
            case string s:
                return $"\"{s}\"";
            case Token token:
                return $"Token({token.Kind}, \"{token.Text}\")";
            case Node node:
                return Inspect(node);
            case IEnumerable<object> nodes:
            {
                _indent++;
                var items = nodes.Select(n => Indented(Inspect(n))).ToList();
                _indent--;
                return items.Count == 0
                    ? "[]"
                    : "[\n" + string.Join('\n', items) + "\n" + Indented("]");
            }

            default:
                return value.ToString() ?? "???";
        }
    }

    private static string Indented(string content) => new string(' ', 2 * _indent) + content;
}