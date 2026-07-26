using Loom.Core.Generation.Macros;
using Loom.Core.Parsing.AST;
using Loom.Core.Text;
using Loom.Core.TypeChecking;
using Loom.Core.TypeChecking.Types;
using Loom.Luau;
using Loom.Luau.AST;
using BinaryOperator = Loom.Luau.AST.BinaryOperator;
using Break = Loom.Core.Parsing.AST.Break;
using Continue = Loom.Core.Parsing.AST.Continue;
using ExpressionStatement = Loom.Core.Parsing.AST.ExpressionStatement;
using Identifier = Loom.Core.Parsing.AST.Identifier;
using Return = Loom.Core.Parsing.AST.Return;
using ArrayType = Loom.Core.TypeChecking.Types.ArrayType;

namespace Loom.Core.Generation;

public sealed partial class LuauGenerator
{
    public override LuauTree VisitTree(Tree tree)
    {
        var statements = tree.Tokens
            .Where(token => token.Kind is not SyntaxKind.Whitespace)
            .TakeWhile(token => token.Kind is SyntaxKind.Comment or SyntaxKind.BlockComment)
            .Select(LuauStatement (token) => new Comment(
                    token.Text.Replace("##", "")
                        .Replace("#:", "")
                        .Replace(":#", "")
                )
            )
            .ToList();

        statements.AddRange(GenerateStatements(tree.Statements));

        _moduleGenerator.MarkListExportedTypes(statements);
        statements.AddRange(_moduleGenerator.GenerateExportedTypeAliases());

        var valueExports = _semanticModel.Exports.FindAll(export => export.EmitsRuntimeBinding);
        if (valueExports.Count > 0)
        {
            var initializers = valueExports.ConvertAll(TableInitializer (export) => new PropertyTableInitializer(export.Name, _moduleGenerator.GenerateExportedValue(export)));

            statements.Add(new Luau.AST.Return(new Table(initializers)));
        }

        return new LuauTree(statements);
    }

    public override Do VisitBlock(Block block) => new(GenerateChunk(block));
    public override LuauNode VisitBreak(Break @break) => new Luau.AST.Break();
    public override LuauNode VisitContinue(Continue @continue) => new Luau.AST.Continue();
    public override LuauNode VisitWhile(While @while) => new WhileStatement(Visit(@while.Condition), GenerateChunk(@while.Body));

    public override LuauNode VisitAfter(After after) =>
        new Luau.AST.ExpressionStatement(LuauFactory.TaskCall("delay", [Visit(after.Duration), ..UnwrapFunctionArgument(after.Body)]));

    public override LuauNode VisitReturn(Return @return) => new Luau.AST.Return(MaybeVisit<LuauExpression>(@return.Expression));
    public override LuauNode VisitExpressionStatement(ExpressionStatement expressionStatement) => WrapExpressionAsStatement(Visit(expressionStatement.Expression));

    public override LuauNode VisitFor(For @for)
    {
        var names = @for.Names.ConvertAll(n => n.Token.Text);
        var body = GenerateChunk(@for.Body);
        var collectionType = _semanticModel.GetType(@for.CollectionExpression);
        var collectionExpression = Visit(@for.CollectionExpression);
        if (names.Count == 2 && collectionType is ArrayType)
        {
            names.Reverse();
            return new ForStatement(names, collectionExpression, body);
        }

        if (!collectionType.Equals(Intrinsics.Range))
            return collectionType is ObjectType or InterfaceType
                ? new ForStatement(names.Count == 1 ? ["_", names[0]] : names, collectionExpression, body)
                : new ForStatement(names, collectionExpression, body);

        LuauExpression start;
        LuauExpression end;
        LuauExpression? incrementBy;
        var one = new NumberLiteral(1);
        var negativeOne = new Luau.AST.UnaryOperator("-", one);
        if (@for.CollectionExpression is RangeLiteral range)
        {
            start = Visit(range.Minimum);
            end = Visit(range.Maximum);
            incrementBy = MacroContext.TryComputeConstantArithmetic(start, out var minimum) && MacroContext.TryComputeConstantArithmetic(end, out var maximum)
                ? maximum < minimum ? negativeOne : null
                : new IfExpression(new Luau.AST.BinaryOperator(end, "<", start), negativeOne, [], one);
        }
        else
        {
            var rangeIdentifier = _state.PushToVariable("_range", collectionExpression);
            var minimum = new Luau.AST.PropertyAccess(rangeIdentifier, ["minimum"]);
            var maximum = new Luau.AST.PropertyAccess(rangeIdentifier, ["maximum"]);
            start = minimum;
            end = maximum;
            incrementBy = new IfExpression(new Luau.AST.BinaryOperator(end, "<", start), negativeOne, [], one);
        }

        return new NumericForStatement(names.First(), start, end, incrementBy, body);
    }

    private LuauNode GenerateAssignment(AssignmentOperator assignmentOperator)
    {
        if (assignmentOperator.Parent is ExpressionStatement)
            return VisitBinaryOperator(assignmentOperator);

        if (assignmentOperator.Left is Identifier)
        {
            var binary = (BinaryOperator)VisitBinaryOperator(assignmentOperator);
            var assignmentStatement = new Luau.AST.ExpressionStatement(binary);
            _state.Prereq(assignmentStatement);

            return binary.Left;
        }

        var left = Visit(assignmentOperator.Left);
        var right = Visit(assignmentOperator.Right);
        if (assignmentOperator.Parent is EqualsValueClause { Parent: NamedDeclaration declaration })
        {
            var identifierAssignment = new BinaryOperator(left, "=", new Luau.AST.Identifier(declaration.Name.Text));
            _state.Postreq(new Luau.AST.ExpressionStatement(identifierAssignment));

            return right;
        }

        var assigned = _state.PushToVariable("_assigned", right);
        var boundAssignment = new BinaryOperator(left, "=", assigned);
        _state.Prereq(new Luau.AST.ExpressionStatement(boundAssignment));

        return assigned;
    }

    public override IfStatement VisitIf(If @if)
    {
        var condition = Visit(@if.Condition);
        var thenBranch = GenerateChunk(@if.ThenBranch);
        var elseBranch = @if.ElseBranch != null ? GenerateChunk(@if.ElseBranch.Branch) : null;
        var elseIfBranches = new List<ElseIfBranch>();
        if (@if.ElseBranch is not { Branch: If elseIf })
            return new IfStatement(condition, thenBranch, elseIfBranches, elseBranch);

        var luauElseIf = VisitIf(elseIf);
        elseBranch = luauElseIf.ElseBranch;
        elseIfBranches.Add(new ElseIfBranch(luauElseIf.Condition, luauElseIf.ThenBranch));
        elseIfBranches.AddRange(luauElseIf.ElseIfBranches);

        return new IfStatement(condition, thenBranch, elseIfBranches, elseBranch);
    }
}