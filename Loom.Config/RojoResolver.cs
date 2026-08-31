namespace Loom.Config;

public sealed class RojoResolver
{
    public const string ProjectFileName = "default.project.json";
    public const string RuntimeFileName = "loom_runtime.luau";
    private static readonly string[] _luauSuffixes = [".server.luau", ".client.luau", ".luau"];
    private readonly RojoProject _project;

    private readonly string _projectDirectory;

    private RojoResolver(string projectDirectory, RojoProject project)
    {
        _projectDirectory = projectDirectory;
        _project = project;
    }

    public static RojoResolver? FromProjectDirectory(string projectDirectory)
    {
        if (string.IsNullOrEmpty(projectDirectory) || !Directory.Exists(projectDirectory))
            return null;

        var projectFilePath = Path.Combine(projectDirectory, ProjectFileName);
        var project = RojoProject.Read(projectFilePath);
        return project == null ? null : new RojoResolver(projectDirectory, project);
    }

    public IReadOnlyList<string>? ResolveRuntimePath() => FindFile(_project.Tree, [], RuntimeFileName);

    public IReadOnlyList<string>? ResolvePath(string filePath) => FindPath(_project.Tree, [], Path.GetFullPath(filePath));

    private IReadOnlyList<string>? FindPath(RojoNode node, IReadOnlyList<string> segments, string filePath)
    {
        foreach (var (name, child) in node.Children)
        {
            var resolved = FindPath(child, [..segments, name], filePath);
            if (resolved != null)
                return resolved;
        }

        if (node.Path == null)
            return null;

        var mapped = Path.GetFullPath(Path.Combine(_projectDirectory, node.Path));

        if (mapped == filePath)
            return segments;

        var prefix = mapped + Path.DirectorySeparatorChar;
        if (!filePath.StartsWith(prefix, StringComparison.Ordinal))
            return null;

        var parts = filePath[prefix.Length..].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return [..segments, ..ToInstanceSegments(parts)];
    }

    private IReadOnlyList<string>? FindFile(RojoNode node, IReadOnlyList<string> segments, string fileName)
    {
        if (node.Path != null)
        {
            var resolved = ResolveInPath(node.Path, segments, fileName);
            if (resolved != null)
                return resolved;
        }

        foreach (var (name, child) in node.Children)
        {
            var resolved = FindFile(child, [.. segments, name], fileName);
            if (resolved != null)
                return resolved;
        }

        return null;
    }

    private IReadOnlyList<string>? ResolveInPath(string nodePath, IReadOnlyList<string> segments, string fileName)
    {
        var absolute = Path.GetFullPath(Path.Combine(_projectDirectory, nodePath));

        if (File.Exists(absolute))
            return Path.GetFileName(absolute) == fileName ? segments : null;

        if (!Directory.Exists(absolute))
            return null;

        var match = Directory.EnumerateFiles(absolute, fileName, SearchOption.AllDirectories).FirstOrDefault();
        if (match == null)
            return null;

        var relative = Path.GetRelativePath(absolute, match);
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return [..segments, ..ToInstanceSegments(parts)];
    }

    private static IEnumerable<string> ToInstanceSegments(string[] parts)
    {
        for (var i = 0; i < parts.Length; i++)
            if (i < parts.Length - 1)
            {
                yield return parts[i];
            }
            else
            {
                var name = StripLuauSuffix(parts[i]);
                if (name != "init")
                    yield return name;
            }
    }

    private static string StripLuauSuffix(string fileName)
    {
        foreach (var suffix in _luauSuffixes)
            if (fileName.EndsWith(suffix))
                return fileName[..^suffix.Length];

        return fileName;
    }
}