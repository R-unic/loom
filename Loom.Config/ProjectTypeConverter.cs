using System.Diagnostics.CodeAnalysis;
using Tomlyn.Serialization;

namespace Loom.Config;

public sealed class ProjectTypeConverter : TomlConverter<ProjectType>
{
    public override ProjectType Read(TomlReader reader)
    {
        var typeName = reader.GetString();
        return typeName.ToLowerInvariant() switch
        {
            "game" => ProjectType.Game,
            "library" => ProjectType.Library,
            "plugin" => ProjectType.Plugin,
            _ => throw new InvalidOperationException($"unknown project type '{typeName}'.")
        };
    }

    [ExcludeFromCodeCoverage]
    public override void Write(TomlWriter writer, ProjectType value)
    {
        switch (value)
        {
            case ProjectType.Game:
                writer.WriteStringValue("game");
                break;
            case ProjectType.Library:
                writer.WriteStringValue("library");
                break;
            case ProjectType.Plugin:
                writer.WriteStringValue("plugin");
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(value));
        }
    }
}