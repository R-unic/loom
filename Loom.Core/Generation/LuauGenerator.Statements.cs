using Loom.Core.Diagnostics;
using Loom.Core.Generation.Events;
using Loom.Core.Generation.Macros;
using Loom.Core.Generation.Serialization;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Core.Resolving.Symbols;
using Loom.Core.Text;
using Loom.Core.TypeChecking;
using Loom.Core.TypeChecking.Types;
using Loom.Luau;
using Loom.Luau.AST;
using Break = Loom.Core.Parsing.AST.Break;
using Continue = Loom.Core.Parsing.AST.Continue;
using ExpressionStatement = Loom.Core.Parsing.AST.ExpressionStatement;
using Identifier = Loom.Core.Parsing.AST.Identifier;
using Return = Loom.Core.Parsing.AST.Return;
using ArrayType = Loom.Core.TypeChecking.Types.ArrayType;
using TupleType = Loom.Core.TypeChecking.Types.TupleType;

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
        var serializerExports = GenerateSerializerExports();
        if (valueExports.Count <= 0 && serializerExports.Count == 0)
            return new LuauTree(statements);

        {
            var initializers = valueExports.ConvertAll(TableInitializer (export) => new PropertyTableInitializer(export.Name, _moduleGenerator.GenerateExportedValue(export)));
            initializers.AddRange(GenerateConnectionStoreExports(valueExports));
            initializers.AddRange(serializerExports);

            statements.Add(new Luau.AST.Return(new Table(initializers)));
        }

        return new LuauTree(statements);
    }

    /// <summary>
    ///     Exports the codec of every exported serializable interface. An interface is a type, so it
    ///     carries no runtime binding of its own - without this the serializer stays file-local and a
    ///     consumer in another module has nothing to reach. A re-exported interface's codec was emitted by
    ///     the module that declared it, so it is forwarded off that module's table the way its connections
    ///     store would be, rather than read from a local that does not exist here.
    /// </summary>
    private List<TableInitializer> GenerateSerializerExports()
    {
        var initializers = new List<TableInitializer>();
        foreach (var export in _semanticModel.Exports)
        {
            if (export.Symbol is not InterfaceSymbol interfaceSymbol || !_semanticModel.SerializationSchemas.ContainsKey(interfaceSymbol))
                continue;

            if (export.Module == null)
            {
                var name = SerializationEmitter.SerializerName(interfaceSymbol.Name);
                initializers.Add(new PropertyTableInitializer(name, new Luau.AST.Identifier(name)));
                continue;
            }

            initializers.Add(
                new PropertyTableInitializer(
                    SerializationEmitter.SerializerName(export.Name),
                    _moduleGenerator.GenerateModuleMember(export.Module, SerializationEmitter.SerializerName(export.SourceName))
                )
            );
        }

        return initializers;
    }

    private List<TableInitializer> GenerateConnectionStoreExports(List<ExportBinding> valueExports)
    {
        var exportedNames = valueExports.Select(export => export.Name).ToHashSet();
        var initializers = new List<TableInitializer>();
        foreach (var export in valueExports.Where(export => export.Symbol.Kind == SymbolKind.Event))
        {
            var storeName = EventConnectionTracker.StoreExportName(export.Name);
            if (!exportedNames.Add(storeName))
            {
                _diagnostics.Error(
                    (Node?)export.Export ?? export.Symbol.Declaration,
                    InternalCodes.EventStoreExportCollision,
                    $"Exporting event '{export.Name}' also exports its connections as '{storeName}', which is already exported.",
                    $"rename the '{storeName}' export"
                );

                continue;
            }

            initializers.Add(new PropertyTableInitializer(storeName, GenerateExportedConnectionStore(export)));
        }

        return initializers;
    }

    private LuauExpression GenerateExportedConnectionStore(ExportBinding export) =>
        export.Module == null
            ? GetConnectionStore(new EventTarget(null, export.Symbol))
            : _moduleGenerator.GenerateModuleMember(export.Module, EventConnectionTracker.StoreExportName(export.SourceName));

    public override Do VisitBlock(Block block) => new(GenerateChunk(block));
    public override LuauNode VisitBreak(Break @break) => new Luau.AST.Break();
    public override LuauNode VisitContinue(Continue @continue) => new Luau.AST.Continue();
    public override LuauNode VisitWhile(While @while) => new WhileStatement(Visit(@while.Condition), GenerateChunk(@while.Body));

    public override LuauNode VisitAfter(After after) =>
        new Luau.AST.ExpressionStatement(LuauFactory.TaskCall("delay", [Visit(after.Duration), ..UnwrapFunctionArgument(after.Body)]));

    public override LuauNode VisitEvery(Every every)
    {
        _semanticModel.RuntimeReferences += 1;
        LuauExpression conditionArgument = every.Condition != null
            ? new AnonymousFunction(null, [], null, new Chunk([new Luau.AST.Return(Visit(every.Condition))]))
            : new NilLiteral();

        return new Luau.AST.ExpressionStatement(
            LuauFactory.RuntimeLibraryCall(["every"], [Visit(every.Duration), conditionArgument, ..UnwrapFunctionArgument(every.Body)])
        );
    }

    public override LuauNode VisitReturn(Return @return)
    {
        if (@return.Expression == null)
            return new Luau.AST.Return();

        if (_semanticModel.GetType(@return.Expression) is not TupleType)
            return new Luau.AST.Return(Visit(@return.Expression));

        if (@return.Expression is TupleExpression tupleExpression)
            return new MultiReturn(tupleExpression.Expressions.ConvertAll(Visit));

        return new Luau.AST.Return(LuauFactory.TableCall("unpack", [Visit(@return.Expression)]));
    }
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

        return new NumericForStatement(names[0], start, end, incrementBy, body);
    }

    private LuauNode GenerateAssignment(AssignmentOperator assignmentOperator)
    {
        if (assignmentOperator.Parent is ExpressionStatement)
            return VisitBinaryOperator(assignmentOperator);

        if (assignmentOperator.Left is Identifier)
        {
            var binary = (Luau.AST.BinaryOperator)VisitBinaryOperator(assignmentOperator);
            var assignmentStatement = new Luau.AST.ExpressionStatement(binary);
            _state.Prereq(assignmentStatement);

            return binary.Left;
        }

        var left = Visit(assignmentOperator.Left);
        var right = Visit(assignmentOperator.Right);
        if (assignmentOperator.Parent is EqualsValueClause { Parent: NamedDeclaration declaration })
        {
            var identifierAssignment = new Luau.AST.BinaryOperator(left, "=", new Luau.AST.Identifier(declaration.Name.Text));
            _state.Postreq(new Luau.AST.ExpressionStatement(identifierAssignment));

            return right;
        }

        var assigned = _state.PushToVariable("_assigned", right);
        var boundAssignment = new Luau.AST.BinaryOperator(left, "=", assigned);
        _state.Prereq(new Luau.AST.ExpressionStatement(boundAssignment));

        return assigned;
    }

    public override IfStatement VisitIf(If @if)
    {
        var condition = Visit(@if.Condition);
        var thenBranch = GenerateChunk(@if.ThenBranch);
        thenBranch.Statements.InsertRange(0, CollectIsPreludes(@if.Condition));
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

    private List<LuauStatement> CollectIsPreludes(Expression condition) =>
        condition switch
        {
            Is isExpression => _isPreludes.GetValueOrDefault(isExpression, []),
            Parsing.AST.BinaryOperator { Operator.Kind: SyntaxKind.AmpersandAmpersand } and =>
                [..CollectIsPreludes(and.Left), ..CollectIsPreludes(and.Right)],
            Parsing.AST.Parenthesized parenthesized => CollectIsPreludes(parenthesized.Expression),
            _ => []
        };
}