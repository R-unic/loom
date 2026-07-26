using Loom.Core.Text;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.Core.TypeChecking;

internal sealed record UnaryOperatorRule(SyntaxKind OperatorKind, Type OperandType, Type? ReturnType = null)
{
    public Type ReturnType { get; } = ReturnType ?? OperandType;
}