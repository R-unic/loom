using Loom.Core.Generation.Macros;
using Loom.Core.Parsing.AST;
using Loom.Core.Text;
using Loom.Core.TypeChecking;
using Loom.Core.TypeChecking.Types;
using Loom.Luau;
using Loom.Luau.AST;
using Break = Loom.Core.Parsing.AST.Break;
using Continue = Loom.Core.Parsing.AST.Continue;
using ExpressionStatement = Loom.Core.Parsing.AST.ExpressionStatement;
using Return = Loom.Core.Parsing.AST.Return;
using ArrayType = Loom.Core.TypeChecking.Types.ArrayType;
using BinaryOperator = Loom.Luau.AST.BinaryOperator;
using PropertyAccess = Loom.Luau.AST.PropertyAccess;
using TypeAlias = Loom.Luau.AST.TypeAlias;
using UnaryOperator = Loom.Luau.AST.UnaryOperator;

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

        MarkListExportedTypes(statements);
        statements.AddRange(GenerateExportedTypeAliases());

        var valueExports = _semanticModel.Exports.FindAll(export => export.EmitsRuntimeBinding);
        if (valueExports.Count > 0)
        {
            var initializers = valueExports.ConvertAll(TableInitializer (export) => new PropertyTableInitializer(export.Name, GenerateExportedValue(export)));

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
    public override LuauNode VisitDeclare(Declare declare) => declare.Signature is InterfaceDeclaration ? Visit(declare.Signature) : new NoOpStatement();
    public override LuauNode VisitExpressionStatement(ExpressionStatement expressionStatement) => WrapExpressionAsStatement(Visit(expressionStatement.Expression));

    public override LuauNode VisitImportDeclaration(ImportDeclaration import) => new NoOpStatement();
    public override LuauNode VisitNamespaceImport(NamespaceImport import) => new NoOpStatement();
    public override LuauNode VisitExportList(ExportList export) => new NoOpStatement();

    public override LuauNode VisitExportDeclaration(ExportDeclaration export)
    {
        var generated = Visit(export.Declaration);
        if (generated is TypeAlias typeAlias)
            typeAlias.IsExported = true;

        return generated;
    }

    public override LuauNode VisitVariableDeclaration(VariableDeclaration variableDeclaration)
    {
        var isConst = variableDeclaration.Keyword is { Kind: SyntaxKind.LetKeyword };
        var name = variableDeclaration.Name.Text;
        var type = variableDeclaration.ColonTypeClause != null ? Visit(variableDeclaration.ColonTypeClause.Type) : null;
        var initializer = variableDeclaration.EqualsValueClause != null ? Visit(variableDeclaration.EqualsValueClause.Value) : null;
        if (initializer != null)
            initializer = LuauFactory.UnwrapParentheses(initializer);

        return isConst
            ? new ConstVariable(name, type, initializer!)
            : new LocalVariable(name, type, initializer);
    }

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
        var negativeOne = new UnaryOperator("-", one);
        if (@for.CollectionExpression is RangeLiteral range)
        {
            start = Visit(range.Minimum);
            end = Visit(range.Maximum);
            incrementBy = MacroContext.TryComputeConstantArithmetic(start, out var minimum) && MacroContext.TryComputeConstantArithmetic(end, out var maximum)
                ? maximum < minimum ? negativeOne : null
                : new IfExpression(new BinaryOperator(end, "<", start), negativeOne, [], one);
        }
        else
        {
            var rangeIdentifier = _state.PushToVariable("_range", collectionExpression);
            var minimum = new PropertyAccess(rangeIdentifier, ["minimum"]);
            var maximum = new PropertyAccess(rangeIdentifier, ["maximum"]);
            start = minimum;
            end = maximum;
            incrementBy = new IfExpression(new BinaryOperator(end, "<", start), negativeOne, [], one);
        }

        return new NumericForStatement(names.First(), start, end, incrementBy, body);
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