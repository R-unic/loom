using Loom.Core.Diagnostics;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.Core.TypeChecking;

public sealed record TypeCheckerResult(Type ReturnType, DiagnosticBag Diagnostics)
    : DiagnosedResult(Diagnostics);