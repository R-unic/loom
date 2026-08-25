using Loom.Core.Text;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.Core.TypeChecking.Operators;

internal sealed record BinaryOperatorRule(SyntaxKind OperatorKind, Type LeftType, Type? RightType = null, Type? ReturnType = null)
{
    public Type RightType { get; } = RightType ?? LeftType;
    public Type ReturnType { get; } = ReturnType ?? RightType ?? LeftType;
}