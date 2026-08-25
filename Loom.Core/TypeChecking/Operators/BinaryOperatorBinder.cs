using Loom.Core.Parsing.AST;
using Loom.Core.Text;
using PrimitiveType = Loom.Core.TypeChecking.Types.PrimitiveType;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.Core.TypeChecking.Operators;

internal static class BinaryOperatorBinder
{
    private static readonly Dictionary<SyntaxKind, string> _binaryMetamethodNames = new()
    {
        [SyntaxKind.Plus] = "__add",
        [SyntaxKind.Minus] = "__sub",
        [SyntaxKind.Star] = "__mul",
        [SyntaxKind.Slash] = "__div",
        [SyntaxKind.SlashSlash] = "__idiv",
        [SyntaxKind.Percent] = "__mod",
        [SyntaxKind.Caret] = "__pow",
        [SyntaxKind.EqualsEquals] = "__eq",
        [SyntaxKind.LArrow] = "__lt",
        [SyntaxKind.LArrowEquals] = "__le"
    };

    public static string? GetMetamethodName(SyntaxKind operatorKind) => _binaryMetamethodNames.GetValueOrDefault(operatorKind);

    private static readonly HashSet<BinaryOperatorRule> _rules =
    [
        new(SyntaxKind.Plus, PrimitiveType.Number),
        new(SyntaxKind.PlusEquals, PrimitiveType.Number),
        new(SyntaxKind.Plus, PrimitiveType.String),
        new(SyntaxKind.PlusEquals, PrimitiveType.String),
        new(SyntaxKind.Minus, PrimitiveType.Number),
        new(SyntaxKind.MinusEquals, PrimitiveType.Number),
        new(SyntaxKind.Star, PrimitiveType.Number),
        new(SyntaxKind.StarEquals, PrimitiveType.Number),
        new(SyntaxKind.Slash, PrimitiveType.Number),
        new(SyntaxKind.SlashEquals, PrimitiveType.Number),
        new(SyntaxKind.SlashSlash, PrimitiveType.Number),
        new(SyntaxKind.SlashSlashEquals, PrimitiveType.Number),
        new(SyntaxKind.Percent, PrimitiveType.Number),
        new(SyntaxKind.PercentEquals, PrimitiveType.Number),
        new(SyntaxKind.Caret, PrimitiveType.Number),
        new(SyntaxKind.CaretEquals, PrimitiveType.Number),
        new(SyntaxKind.Ampersand, PrimitiveType.Number),
        new(SyntaxKind.AmpersandEquals, PrimitiveType.Number),
        new(SyntaxKind.Pipe, PrimitiveType.Number),
        new(SyntaxKind.PipeEquals, PrimitiveType.Number),
        new(SyntaxKind.Tilde, PrimitiveType.Number),
        new(SyntaxKind.TildeEquals, PrimitiveType.Number),
        new(SyntaxKind.LArrowLArrow, PrimitiveType.Number),
        new(SyntaxKind.LArrowLArrowEquals, PrimitiveType.Number),
        new(SyntaxKind.RArrowRArrow, PrimitiveType.Number),
        new(SyntaxKind.RArrowRArrowEquals, PrimitiveType.Number),
        new(SyntaxKind.RArrowRArrowRArrow, PrimitiveType.Number),
        new(SyntaxKind.RArrowRArrowRArrowEquals, PrimitiveType.Number),
        new(SyntaxKind.AmpersandAmpersand, PrimitiveType.Bool),
        new(SyntaxKind.AmpersandAmpersandEquals, PrimitiveType.Bool),
        new(SyntaxKind.PipePipe, PrimitiveType.Bool),
        new(SyntaxKind.PipePipeEquals, PrimitiveType.Bool),
        new(SyntaxKind.EqualsEquals, PrimitiveType.Unknown, PrimitiveType.Unknown, PrimitiveType.Bool),
        new(SyntaxKind.BangEquals, PrimitiveType.Unknown, PrimitiveType.Unknown, PrimitiveType.Bool),
        new(SyntaxKind.LArrow, PrimitiveType.Number, PrimitiveType.Number, PrimitiveType.Bool),
        new(SyntaxKind.LArrowEquals, PrimitiveType.Number, PrimitiveType.Number, PrimitiveType.Bool),
        new(SyntaxKind.RArrow, PrimitiveType.Number, PrimitiveType.Number, PrimitiveType.Bool),
        new(SyntaxKind.RArrowEquals, PrimitiveType.Number, PrimitiveType.Number, PrimitiveType.Bool),
        new(SyntaxKind.LArrow, PrimitiveType.String, PrimitiveType.String, PrimitiveType.Bool),
        new(SyntaxKind.LArrowEquals, PrimitiveType.String, PrimitiveType.String, PrimitiveType.Bool),
        new(SyntaxKind.RArrow, PrimitiveType.String, PrimitiveType.String, PrimitiveType.Bool),
        new(SyntaxKind.RArrowEquals, PrimitiveType.String, PrimitiveType.String, PrimitiveType.Bool),
        new(SyntaxKind.InKeyword, PrimitiveType.String, PrimitiveType.Unknown, PrimitiveType.Bool)
    ];

    public static BinaryOperatorRule? GetRule(BinaryOperator binaryOperator, Type leftType, Type rightType) =>
        _rules.FirstOrDefault(rule => rule.OperatorKind == binaryOperator.Operator.Kind
            && Type.IsNotNever(leftType)
            && leftType.IsAssignableTo(rule.LeftType)
            && Type.IsNotNever(rightType)
            && rightType.IsAssignableTo(rule.RightType)
        );

    public static BinaryOperatorRule? GetSuggestion(BinaryOperator binaryOperator, Type leftType, Type rightType)
    {
        var sameOp = _rules.FirstOrDefault(r => r.OperatorKind == binaryOperator.Operator.Kind
            && !(Type.IsNotNever(leftType) && leftType.IsAssignableTo(r.LeftType) && Type.IsNotNever(rightType) && rightType.IsAssignableTo(r.RightType))
        );

        var differentOp = _rules.FirstOrDefault(r => r.OperatorKind != binaryOperator.Operator.Kind
            && Type.IsNotNever(leftType)
            && leftType.IsAssignableTo(r.LeftType)
            && Type.IsNotNever(rightType)
            && rightType.IsAssignableTo(r.RightType)
        );

        var fixRight = _rules.FirstOrDefault(r => r.OperatorKind == binaryOperator.Operator.Kind
            && Type.IsNotNever(leftType)
            && leftType.IsAssignableTo(r.LeftType)
            && Type.IsNotNever(rightType)
            && !rightType.IsAssignableTo(r.RightType)
        );

        var fixLeft = _rules.FirstOrDefault(r => r.OperatorKind == binaryOperator.Operator.Kind
            && Type.IsNotNever(leftType)
            && !leftType.IsAssignableTo(r.LeftType)
            && Type.IsNotNever(rightType)
            && rightType.IsAssignableTo(r.RightType)
        );

        return fixRight
            ?? fixLeft
            ?? differentOp
            ?? sameOp;
    }
}