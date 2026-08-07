using Loom.Config;
using Loom.Core.Text;

namespace Loom.Core.Modules;

/// <param name="CaseInsensitiveMatch">
///     A file the specifier would have named had it been cased the way the file is. Set only alongside
///     <see cref="ModuleResolutionStatus.NotFound" />, since resolution is case-sensitive even where the file
///     system is not.
/// </param>
/// <param name="Package">
///     The package a bare specifier named, once it is known which of its segments were the package's name.
///     Null for a relative specifier and for one too malformed to name a package at all.
/// </param>
public sealed record ModuleResolution(
    ModuleResolutionStatus Status,
    SourceFile? File,
    SourceFile? CaseInsensitiveMatch = null,
    PackageName? Package = null)
{
    public static ModuleResolution Resolved(SourceFile file) => new(ModuleResolutionStatus.Resolved, file);
    public static ModuleResolution Failed(ModuleResolutionStatus status, PackageName? package = null) => new(status, null, Package: package);
    public static ModuleResolution NotFound(SourceFile? caseInsensitiveMatch, PackageName? package = null) =>
        new(ModuleResolutionStatus.NotFound, null, caseInsensitiveMatch, package);
}
