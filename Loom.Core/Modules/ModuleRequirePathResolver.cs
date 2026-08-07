using Loom.Config;
using Loom.Core.Pipeline;
using Loom.Core.Text;

namespace Loom.Core.Modules;

/// <summary>
///     Turns a module into the path a <c>require</c> names it by, using the Rojo project to find where the
///     module's compiled output lands in the instance tree.
/// </summary>
/// <remarks>
///     A module from a dependency root is named through the entry project's Rojo project too: that project
///     describes the tree the compiled game actually runs in, whereas a dependency's own project file only
///     describes how that dependency is laid out when it is built alone. Its output is written into the entry
///     project's own output directory (see <see cref="SourceRootSet.OutputPathOf" />), so the mapping a project
///     already has for its own code is the one that names its packages.
/// </remarks>
public sealed class ModuleRequirePathResolver(SourceRootSet roots)
{
    private readonly Dictionary<SourceFile, IReadOnlyList<string>?> _instancePaths = [];
    private readonly RojoResolver? _rojoResolver = RojoResolver.FromProjectDirectory(roots.Entry.Config.ProjectDirectory);

    /// <summary>
    ///     Where packages are written, written the way a <c>$path</c> is: relative to the project directory the
    ///     Rojo project sits in, with forward slashes, so a diagnostic can quote it straight into the file.
    /// </summary>
    private string PackagesPath =>
        field ??= Path.GetRelativePath(roots.Entry.Config.ProjectDirectory, roots.PackagesDirectory).Replace(Path.DirectorySeparatorChar, '/');

    /// <param name="importingFile">The file whose <c>require</c> this is, which decides whether the module is one of its own project's.</param>
    /// <param name="module">The module file to resolve.</param>
    /// <param name="specifier">The specifier as written, used when Rojo cannot name the module.</param>
    public ModuleRequirePath Resolve(SourceFile importingFile, SourceFile module, string specifier)
    {
        var package = PackageCrossedBy(importingFile, module);
        if (_rojoResolver == null)
            return ModuleRequirePath.Fallback(ModuleRequirePathStatus.RojoMissing, specifier, package, PackagesPath);

        if (!_instancePaths.TryGetValue(module, out var instancePath))
            _instancePaths[module] = instancePath = _rojoResolver.ResolvePath(roots.OutputPathOf(module));

        return instancePath == null
            ? ModuleRequirePath.Fallback(ModuleRequirePathStatus.NotFoundInRojo, specifier, package, PackagesPath)
            : ModuleRequirePath.Resolved(instancePath);
    }

    /// <summary>
    ///     The package <paramref name="module" /> belongs to, when requiring it takes the importing file out of
    ///     its own project — the case a relative require cannot stand in for, since nothing puts the two
    ///     projects' output in a fixed position relative to one another.
    /// </summary>
    private PackageName? PackageCrossedBy(SourceFile importingFile, SourceFile module)
    {
        var moduleRoot = roots.Of(module);
        return moduleRoot == roots.Of(importingFile) ? null : moduleRoot.Package?.Name;
    }
}
