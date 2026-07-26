namespace Loom.Core.Modules;

public enum ModuleResolutionStatus
{
    Resolved,

    /// <summary>No file in the unit matches the specifier.</summary>
    NotFound,

    /// <summary>The specifier resolves to the file doing the importing.</summary>
    SelfImport,

    /// <summary>The specifier climbs out of the configured source directory.</summary>
    OutsideSourceDirectory,

    /// <summary>Not a relative specifier bare specifiers are reserved for packages.</summary>
    UnsupportedSpecifier
}