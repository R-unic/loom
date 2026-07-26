using Loom.Core.Diagnostics;

namespace Loom.Core.Pipeline;

public sealed record CompilationResult(List<CompiledFile> Files, DiagnosticBag Diagnostics)
    : DiagnosedResult(Diagnostics);