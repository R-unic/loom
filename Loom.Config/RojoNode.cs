using System.Text.Json;

namespace Loom.Config;

public sealed class RojoNode
{
    public string? Path { get; private init; }
    public string? ClassName { get; init; }
    public IReadOnlyDictionary<string, RojoNode> Children { get; private init; } = new Dictionary<string, RojoNode>();

    public static RojoNode Parse(JsonElement element)
    {
        string? path = null;
        string? className = null;
        var children = new Dictionary<string, RojoNode>();

        foreach (var property in element.EnumerateObject())
            switch (property.Name)
            {
                case "$path" when property.Value.ValueKind == JsonValueKind.String:
                    path = property.Value.ToString();
                    break;
                case "$className" when property.Value.ValueKind == JsonValueKind.String:
                    className = property.Value.ToString();
                    break;
                default:
                    if (!property.Name.StartsWith('$') && property.Value.ValueKind == JsonValueKind.Object)
                        children[property.Name] = Parse(property.Value);

                    break;
            }

        return new RojoNode { Path = path, ClassName = className, Children = children };
    }
}