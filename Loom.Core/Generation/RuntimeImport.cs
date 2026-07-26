using Loom.Config;

namespace Loom.Core.Generation;

public sealed record RuntimeImport(RuntimeImportStatus Status, string Path)
{
    public const string DefaultPath = PathPrefix + "ReplicatedStorage/include/loom_runtime";
    internal const string PathPrefix = "@game/";

    public static RuntimeImport Default { get; } = new(RuntimeImportStatus.RojoMissing, DefaultPath);

    public static RuntimeImport Resolve(LoomConfig config)
    {
        var resolver = RojoResolver.FromProjectDirectory(config.ProjectDirectory);
        if (resolver == null)
            return Default;

        var segments = resolver.ResolveRuntimePath();
        return segments == null
            ? new RuntimeImport(RuntimeImportStatus.NotFoundInRojo, DefaultPath)
            : new RuntimeImport(RuntimeImportStatus.Resolved, PathPrefix + string.Join('/', segments));
    }
}