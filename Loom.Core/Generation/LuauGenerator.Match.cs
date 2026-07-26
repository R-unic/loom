using Loom.Core.Parsing.AST;
using Loom.Luau.AST;
using BinaryOperator = Loom.Luau.AST.BinaryOperator;
using ExpressionStatement = Loom.Luau.AST.ExpressionStatement;
using Identifier = Loom.Luau.AST.Identifier;

namespace Loom.Core.Generation;

public sealed partial class LuauGenerator
{
    public override LuauNode VisitMatchExpression(MatchExpression matchExpression)
    {
        if (matchExpression.Arms.Count == 0)
            return new NilLiteral();

        var subject = _state.PushToVariable("_subject", Visit(matchExpression.Expression));
        var matchName = _state.Scope.AddIdentifier("_match");
        var matchIdentifier = new Identifier(matchName);
        _state.Prereq(new LocalVariable(matchName, null, null));

        LuauExpression? thenCondition = null;
        Chunk? thenBranch = null;
        var elseIfBranches = new List<ElseIfBranch>();
        Chunk? elseBranch = null;

        foreach (var arm in matchExpression.Arms)
        {
            if (elseBranch != null)
                break;

            if (arm.Guard != null)
                _diagnostics.NotImplemented(arm.Guard, "Match arm guards are not yet supported in code generation.");

            if (!TryCompilePatternTest(arm.Pattern, subject, out var condition, out var isIrrefutable))
                continue;

            var armChunk = CompileMatchArmBody(arm, matchIdentifier);
            if (isIrrefutable)
            {
                elseBranch = armChunk;
                break;
            }

            if (thenCondition == null)
            {
                thenCondition = condition;
                thenBranch = armChunk;
            }
            else
            {
                elseIfBranches.Add(new ElseIfBranch(condition!, armChunk));
            }
        }

        if (thenCondition == null)
        {
            if (elseBranch != null)
                _state.Prereq([..elseBranch.Statements]);
        }
        else
        {
            _state.Prereq(new IfStatement(thenCondition, thenBranch!, elseIfBranches, elseBranch));
        }

        return matchIdentifier;
    }

    private Chunk CompileMatchArmBody(MatchArm arm, Identifier matchIdentifier)
    {
        var statements = new List<LuauStatement>();
        var (body, scope) = _state.Capture(() => Visit(arm.Body));
        ApplyPrereqAndPostreq(
            statements,
            scope,
            new ExpressionStatement(new BinaryOperator(matchIdentifier, "=", body))
        );

        return new Chunk(statements);
    }

    /// <summary>
    ///     Step 1 supports wildcard, literal (including <c>none</c>), and error-recovery null patterns.
    ///     Everything else diagnoses and returns false so the arm is skipped.
    /// </summary>
    private bool TryCompilePatternTest(
        Pattern pattern,
        LuauExpression subject,
        out LuauExpression? condition,
        out bool isIrrefutable)
    {
        switch (pattern)
        {
            case WildcardPattern:
                condition = null;
                isIrrefutable = true;
                return true;

            case LiteralPattern literalPattern:
                condition = new BinaryOperator(subject, "==", LiteralValueToExpression(literalPattern.Value));
                isIrrefutable = false;
                return true;

            case NullPattern:
                condition = new BinaryOperator(subject, "==", new NilLiteral());
                isIrrefutable = false;
                return true;

            default:
                _diagnostics.NotImplemented(
                    pattern,
                    $"Pattern kind '{pattern.GetType().Name}' is not yet supported in code generation."
                );

                condition = null;
                isIrrefutable = false;
                return false;
        }
    }

    private static LuauExpression LiteralValueToExpression(object? value) =>
        value switch
        {
            long l => new NumberLiteral(l),
            int i => new NumberLiteral(i),
            double d => new NumberLiteral(d),
            string s => new StringLiteral(s),
            bool b => new BooleanLiteral(b),
            _ => new NilLiteral()
        };
}