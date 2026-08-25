using Loom.Core.Parsing.AST;
using Loom.Core.Text;
using PrimitiveType = Loom.Core.TypeChecking.Types.PrimitiveType;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.Core.TypeChecking.Operators;

internal static class UnaryOperatorBinder
{
    private static readonly HashSet<UnaryOperatorRule> _rules =
    [
        new(SyntaxKind.Bang, PrimitiveType.Bool), new(SyntaxKind.Tilde, PrimitiveType.Number), new(SyntaxKind.Minus, PrimitiveType.Number)
    ];

    public static UnaryOperatorRule? GetRule(UnaryOperator unaryOperator, Type operandType) =>
        _rules.FirstOrDefault(rule => rule.OperatorKind == unaryOperator.Operator.Kind
            && Type.IsNotNever(operandType)
            && operandType.IsAssignableTo(rule.OperandType)
        );

    public static UnaryOperatorRule? GetSuggestion(UnaryOperator unaryOperator, Type operandType)
    {
        var sameOp = _rules.FirstOrDefault(r => r.OperatorKind == unaryOperator.Operator.Kind
            && Type.IsNotNever(operandType)
            && !operandType.IsAssignableTo(r.OperandType)
        );

        var differentOp = _rules.FirstOrDefault(r => r.OperatorKind != unaryOperator.Operator.Kind
            && Type.IsNotNever(operandType)
            && operandType.IsAssignableTo(r.OperandType)
        );

        return differentOp ?? sameOp;
    }
}