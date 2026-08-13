using System.Text.Json;

namespace Loom.Config;

public sealed class RojoProject
{
    public required RojoNode Tree { get; init; }

    public static RojoProject? Read(string projectFilePath)
    {
        if (!File.Exists(projectFilePath))
            return null;

        // A malformed manifest reads as no manifest, the way ConfigReader treats one it cannot bind: this is
        // a file the user may hand-edit, so it is their error to fix and not a stack trace out of the CLI.
        // What it costs is the require path falling back to the default, which is reported where it is used.
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(projectFilePath));
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
            return !document.RootElement.TryGetProperty("tree", out var treeElement)
                ? null
                : new RojoProject { Tree = RojoNode.Parse(treeElement) };
    }
}