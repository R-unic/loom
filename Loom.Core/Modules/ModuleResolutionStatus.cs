namespace Loom.Core.Modules;

public enum ModuleResolutionStatus
{
    Resolved,

    /// <summary>No file in the unit matches the specifier.</summary>
    NotFound,

    /// <summary>The specifier resolves to the file doing the importing.</summary>
    SelfImport,

    /// <summary>The specifier climbs out of the source directory it is resolved against.</summary>
    OutsideSourceDirectory,

    /// <summary>Neither a relative path nor a well-formed package name, so it names nothing at all.</summary>
    UnsupportedSpecifier,

    /// <summary>No root in the unit publishes the package the specifier names.</summary>
    PackageNotFound,

    /// <summary>The package is part of the build, but the importing project does not depend on it.</summary>
    UndeclaredDependency
}
