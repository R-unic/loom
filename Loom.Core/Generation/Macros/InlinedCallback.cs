using System.Diagnostics.CodeAnalysis;
using Loom.Luau;
using Loom.Luau.AST;

namespace Loom.Core.Generation.Macros;

internal sealed record InlinedCallback(List<string> ParameterNames, List<LuauStatement> Prelude, LuauExpression Value)
{
    public static bool TryInline(
        LuauExpression callback,
        int argumentCount,
        IReadOnlyCollection<string> reserved,
        [MaybeNullWhen(false)] out InlinedCallback inlined)
    {
        inlined = null;
        if (LuauFactory.UnwrapParentheses(callback) is not AnonymousFunction { TypeParameters: null } function)
            return false;

        var parameterNames = new List<string>(function.Parameters.Count);
        foreach (var parameter in function.Parameters)
        {
            if (parameter.Name == "..."
                || LuauFactory.Keywords.Contains(parameter.Name)
                || reserved.Contains(parameter.Name)
                || parameterNames.Contains(parameter.Name))
                return false;

            parameterNames.Add(parameter.Name);
        }

        if (parameterNames.Count > argumentCount)
            return false;

        var statements = function.Body.Statements;
        if (statements is not [.., Return { Expression: { } value }])
            return false;

        var prelude = statements.GetRange(0, statements.Count - 1);
        if (prelude.Exists(ReturnsEarly))
            return false;

        inlined = new InlinedCallback(parameterNames, prelude, value);
        return true;
    }

    private static bool ReturnsEarly(LuauStatement statement) =>
        statement switch
        {
            Return or MultiReturn => true,
            Chunk chunk => chunk.Statements.Exists(ReturnsEarly),
            Do @do => ReturnsEarly(@do.Body),
            IfStatement ifStatement => ReturnsEarly(ifStatement.ThenBranch)
                || ifStatement.ElseIfBranches.Exists(branch => ReturnsEarly(branch.Branch))
                || ifStatement.ElseBranch != null && ReturnsEarly(ifStatement.ElseBranch),

            ForStatement forStatement => ReturnsEarly(forStatement.Body),
            NumericForStatement numericFor => ReturnsEarly(numericFor.Body),
            WhileStatement whileStatement => ReturnsEarly(whileStatement.Body),
            _ => false
        };
}
