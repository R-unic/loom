namespace Loom.Core.Modules;

public enum ModuleRequirePathStatus
{
    /// <summary>Rojo maps the module's output file, so it is required by its instance path.</summary>
    Resolved,

    /// <summary>No Rojo project file was found next to the Loom config.</summary>
    RojoMissing,

    /// <summary>A Rojo project exists but no <c>$path</c> mapping covers the module's output file.</summary>
    NotFoundInRojo
}

/// <summary>
///     How a module is named in the <c>require</c> that pulls it in.
/// </summary>
/// <remarks>
///     The fallback is the specifier as written. It resolves correctly on its own because the output tree
///     mirrors the source tree, but only where relative require-by-string is available, so falling back to it
///     is reported rather than silent.
/// </remarks>
public sealed record ModuleRequirePath(ModuleRequirePathStatus Status, string Path)
{
    public bool IsFallback => Status != ModuleRequirePathStatus.Resolved;

    public static ModuleRequirePath Fallback(ModuleRequirePathStatus status, string specifier) => new(status, specifier);

    public static ModuleRequirePath Resolved(IEnumerable<string> instanceSegments) =>
        new(ModuleRequirePathStatus.Resolved, RuntimeImport.PathPrefix + string.Join('/', instanceSegments));
}