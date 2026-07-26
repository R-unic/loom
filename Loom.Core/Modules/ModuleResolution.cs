using Loom.Core.Text;

namespace Loom.Core.Modules;

public sealed record ModuleResolution(ModuleResolutionStatus Status, SourceFile? File)
{
    public static ModuleResolution Resolved(SourceFile file) => new(ModuleResolutionStatus.Resolved, file);
    public static ModuleResolution Failed(ModuleResolutionStatus status) => new(status, null);
}