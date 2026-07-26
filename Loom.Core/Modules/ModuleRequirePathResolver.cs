using Loom.Config;
using Loom.Core.Pipeline;
using Loom.Core.Text;

namespace Loom.Core.Modules;

/// <summary>
///     Turns a module into the path a <c>require</c> names it by, using the Rojo project to find where the
///     module's compiled output lands in the instance tree.
/// </summary>
public sealed class ModuleRequirePathResolver(LoomConfig config)
{
    private readonly Dictionary<SourceFile, IReadOnlyList<string>?> _instancePaths = [];
    private readonly RojoResolver? _rojoResolver = RojoResolver.FromProjectDirectory(config.ProjectDirectory);

    /// <param name="specifier">The specifier as written, used when Rojo cannot name the module.</param>
    public ModuleRequirePath Resolve(SourceFile module, string specifier)
    {
        if (_rojoResolver == null)
            return ModuleRequirePath.Fallback(ModuleRequirePathStatus.RojoMissing, specifier);

        if (!_instancePaths.TryGetValue(module, out var instancePath))
            _instancePaths[module] = instancePath = _rojoResolver.ResolvePath(FileManager.GetOutputPath(module, config));

        return instancePath == null
            ? ModuleRequirePath.Fallback(ModuleRequirePathStatus.NotFoundInRojo, specifier)
            : ModuleRequirePath.Resolved(instancePath);
    }
}