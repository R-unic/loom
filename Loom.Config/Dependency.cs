namespace Loom.Config;

/// <summary>
///     One entry of the <c>[dependencies]</c> table: the source a specifier resolves from. Written either as a bare
///     version requirement (<c>serio = "^1.2"</c>) or as a table (<c>runit = { version = "^0.4", dev = true }</c>).
///     The requirement is read into a <see cref="Config.VersionRequirement" /> here, so anything holding a dependency
///     can ask what it accepts; finding a published version that satisfies it and putting it on disk stays the
///     package manager's job.
/// </summary>
public sealed class Dependency(PackageName name, VersionRequirement versionRequirement, bool isDevelopmentOnly = false)
{
    /// <summary>The package this entry depends on, taken from the key it is written under.</summary>
    public PackageName Name { get; } = name;

    /// <summary>The versions this entry accepts, e.g. <c>^1.2</c> or <c>&gt;=0.4.0</c>.</summary>
    public VersionRequirement VersionRequirement { get; } = versionRequirement;

    /// <summary>Whether the dependency is only needed to develop the package, e.g. a test framework.</summary>
    public bool IsDevelopmentOnly { get; } = isDevelopmentOnly;

    public override string ToString() => IsDevelopmentOnly ? $"{VersionRequirement} (dev)" : VersionRequirement.ToString();
}
