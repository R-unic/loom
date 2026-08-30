using Loom.Config;
using Version = Loom.Config.Version;

namespace Loom.Testing.Packages;

/// <summary>
///     A throwaway workspace holding a local index and a project that resolves from it — the shape a package
///     manager works in, written to a temp directory so resolution can be tested without a network.
/// </summary>
internal sealed class PackageIndexFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "loom-index-test-" + Guid.NewGuid());

    public string IndexDirectory => Path.Combine(Root, "index");

    public string ProjectDirectory => Path.Combine(Root, "app");

    public string LibraryDirectory => Path.Combine(Root, "lib");

    /// <summary>Publishes one version of a package into the index: a version directory holding a project of its own.</summary>
    public PackageIndexFixture Publish(string name, string version, string dependencies = "", string source = "export let value = 1;")
    {
        var package = PackageName.Parse(name);
        var directory = Path.Combine(IndexDirectory, package.Scope ?? string.Empty, package.Name, version);
        Directory.CreateDirectory(Path.Combine(directory, "src"));
        File.WriteAllText(
            Path.Combine(directory, ConfigReader.ConfigFileName),
            $"project_type = \"library\"\n[package]\nname = \"{name}\"\nversion = \"{version}\"\n"
            + (dependencies.Length == 0 ? "" : $"[dependencies]\n{dependencies}\n")
            + "[files]\nsource_directory = \"src\"\noutput_directory = \"dist\"\n"
        );

        File.WriteAllText(Path.Combine(directory, "src", "init.loom"), source);
        return this;
    }

    /// <summary>Writes the project that depends on what the index publishes, pointed at the index as its registry.</summary>
    public LoomConfig WriteProject(string dependencies, string source = "let x = 1;", bool withRegistry = true)
    {
        Directory.CreateDirectory(Path.Combine(ProjectDirectory, "src"));
        File.WriteAllText(
            Path.Combine(ProjectDirectory, ConfigReader.ConfigFileName),
            "project_type = \"game\"\n"
            + (dependencies.Length == 0 ? "" : $"[dependencies]\n{dependencies}\n")
            + (withRegistry ? "[registry]\nindex = \"../index\"\n" : "")
            + "[files]\nsource_directory = \"src\"\noutput_directory = \"dist\"\n"
        );

        File.WriteAllText(Path.Combine(ProjectDirectory, "src", "main.loom"), source);
        var config = ConfigReader.LocateFromDirectory(ProjectDirectory, out var diagnostics);
        Assert.Empty(diagnostics);
        Assert.NotNull(config);
        config.NoEmit = true;
        return config;
    }

    /// <summary>
    ///     Writes a project that is a package of its own, in a directory of its own — the subject a publish takes,
    ///     kept apart from <see cref="ProjectDirectory" /> so one fixture can hold both a package and something that
    ///     depends on it.
    /// </summary>
    public LoomConfig WriteLibrary(string name, string version, string dependencies = "", string source = "export let value = 1;")
    {
        Directory.CreateDirectory(Path.Combine(LibraryDirectory, "src"));
        File.WriteAllText(
            Path.Combine(LibraryDirectory, ConfigReader.ConfigFileName),
            $"project_type = \"library\"\n[package]\nname = \"{name}\"\nversion = \"{version}\"\n"
            + (dependencies.Length == 0 ? "" : $"[dependencies]\n{dependencies}\n")
            + "[registry]\nindex = \"../index\"\n"
            + "[files]\nsource_directory = \"src\"\noutput_directory = \"dist\"\n"
        );

        File.WriteAllText(Path.Combine(LibraryDirectory, "src", "init.loom"), source);
        var config = ConfigReader.LocateFromDirectory(LibraryDirectory, out var diagnostics);
        Assert.Empty(diagnostics);
        Assert.NotNull(config);
        return config;
    }

    /// <summary>The manifest of the project, as it stands on disk.</summary>
    public string ReadManifest() => File.ReadAllText(Path.Combine(ProjectDirectory, ConfigReader.ConfigFileName));

    /// <summary>The version installed in the project's packages directory, or null when the package is not installed.</summary>
    public Version? InstalledVersion(string name)
    {
        var directory = Path.Combine(ProjectDirectory, "packages", PackageName.Parse(name).Scope ?? string.Empty, PackageName.Parse(name).Name);
        return ConfigReader.LocateFromDirectory(directory, out _)?.Package?.Version;
    }

    public LockFile? ReadLock() => LockFileReader.LocateFromDirectory(ProjectDirectory, out _);

    public void Dispose() => Directory.Delete(Root, true);
}
