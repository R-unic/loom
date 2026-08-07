using System.Collections;
using Loom.Config;
using Loom.Core.Text;

namespace Loom.Core.Pipeline;

/// <summary>
///     The projects a <see cref="CompilationUnit" /> spans: the entry project first, then one root per
///     dependency compiled from source. Everything a file's project decides — where its Luau is written, which
///     Rojo project names it, how far a relative import may reach — is read off the root owning that file
///     rather than off the unit, which is what lets one unit compile projects whose configs disagree.
/// </summary>
public sealed class SourceRootSet : IReadOnlyList<SourceRoot>
{
    private readonly IReadOnlyList<SourceRoot> _roots;

    public SourceRootSet(SourceRoot entry, params IEnumerable<SourceRoot> dependencies)
    {
        _roots = [entry, ..dependencies];
        if (_roots.Count > 1)
            DisownNestedFiles();
    }

    /// <summary>The project the unit was started for, and the root that owns every file no other root claims.</summary>
    public SourceRoot Entry => field ??= _roots[0];

    /// <summary>Every root's files, entry project first.</summary>
    public IReadOnlyList<SourceFile> Files => field ??= _roots.SelectMany(root => root.Files).ToArray();

    public int Count => _roots.Count;
    public SourceRoot this[int index] => _roots[index];

    /// <summary>
    ///     The root owning <paramref name="file" />: the one whose source directory contains it, the innermost
    ///     when roots nest — a dependency vendored under the entry project's own source directory owns its
    ///     files, not the project it sits inside. Files under no root at all, such as an intrinsic compiled
    ///     from an embedded resource or a lone file handed to <see cref="CompilationUnit.Compile(SourceFile)" />,
    ///     fall back to <see cref="Entry" /> and so read the settings they read when a unit had a single root.
    /// </summary>
    public SourceRoot Of(SourceFile file)
    {
        var path = Path.GetFullPath(file.AbsolutePath);
        SourceRoot? owner = null;
        foreach (var root in _roots)
        {
            if (!root.Contains(path))
                continue;

            // a root nested in another has the longer source directory of the two, so the longest match
            // is the innermost root containing the file
            if (owner == null || root.SourceDirectory.Length > owner.SourceDirectory.Length)
                owner = root;
        }

        return owner ?? Entry;
    }

    /// <summary>The config governing <paramref name="file" />, which is its own root's rather than the unit's.</summary>
    public LoomConfig ConfigOf(SourceFile file) => Of(file).Config;

    /// <summary>
    ///     The root publishing <paramref name="package" />, which is the root a specifier naming that package
    ///     resolves in, or <see langword="null" /> when the unit compiles no such package.
    /// </summary>
    public SourceRoot? WithPackage(PackageName package) => _roots.FirstOrDefault(root => package.Equals(root.Package?.Name));

    /// <summary>
    ///     Where <paramref name="file" />'s Luau is written. The entry project's own files mirror its source
    ///     tree into its output directory as they always have; a dependency's land under
    ///     <see cref="FilesConfig.PackagesDirectoryName" /> inside that same output directory, in a folder
    ///     named by the package — a scope being simply a folder above the name.
    /// </summary>
    /// <remarks>
    ///     A dependency's output belongs to the build consuming it rather than to the dependency: compiled
    ///     output is already consumer-specific (it names the entry project's runtime library, and is checked
    ///     against the intrinsics of the entry project's type), so one copy cannot serve two consumers and has
    ///     no business next to sources a package manager may share between them. Writing it into the consumer's
    ///     own output directory also means the <c>$path</c> a project already maps for its own code covers
    ///     every package it depends on, however many that becomes.
    /// </remarks>
    public string OutputPathOf(SourceFile file)
    {
        var root = Of(file);
        if (root == Entry || root.Package?.Name is not { } package)
            return FileManager.GetOutputPath(file, root.Config);

        var relativePath = Path.GetRelativePath(root.Config.Files.SourceDirectory, Path.GetFullPath(file.AbsolutePath));
        var packageDirectory = Path.Combine(PackagesDirectory, package.Scope ?? "", package.Name);
        return Path.ChangeExtension(Path.Combine(packageDirectory, relativePath), ".luau");
    }

    /// <summary>Where every dependency of this unit is written, which is what a Rojo project has to map for their requires to be nameable.</summary>
    public string PackagesDirectory => Path.Combine(Entry.Config.Files.OutputDirectory, FilesConfig.PackagesDirectoryName);

    /// <summary>Swaps the file already held at <paramref name="file" />'s path for <paramref name="file" /> itself, in whichever root holds it.</summary>
    /// <returns>Whether any root held a file at that path.</returns>
    public bool Replace(SourceFile file)
    {
        foreach (var root in _roots)
        {
            var index = root.Files.FindIndex(existing => existing.AbsolutePath == file.AbsolutePath);
            if (index < 0)
                continue;

            root.Files[index] = file;
            return true;
        }

        return false;
    }

    public IEnumerator<SourceRoot> GetEnumerator() => _roots.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    ///     Drops from every root the files a root nested inside it owns. A dependency vendored under another
    ///     project's source directory is loaded by both roots, and a file compiled once per root would be
    ///     analyzed twice and emitted to two different output directories.
    /// </summary>
    private void DisownNestedFiles()
    {
        foreach (var root in _roots)
            root.Files.RemoveAll(file => Of(file) != root);
    }
}
