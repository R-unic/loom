using System.Diagnostics.CodeAnalysis;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Luau;
using Loom.Luau.AST;
using static Loom.Core.Generation.Macros.ArrayLowering;
using ArrayType = Loom.Core.TypeChecking.Types.ArrayType;
using BinaryOperator = Loom.Luau.AST.BinaryOperator;
using Break = Loom.Luau.AST.Break;
using Continue = Loom.Luau.AST.Continue;
using ElementAccess = Loom.Luau.AST.ElementAccess;
using Expression = Loom.Core.Parsing.AST.Expression;
using ExpressionStatement = Loom.Luau.AST.ExpressionStatement;
using Identifier = Loom.Luau.AST.Identifier;
using PropertyAccess = Loom.Core.Parsing.AST.PropertyAccess;
using Return = Loom.Luau.AST.Return;
using UnaryOperator = Loom.Luau.AST.UnaryOperator;

namespace Loom.Core.Generation.Macros;

/// <summary>
///     Lowers a chain of array combinators into a single loop.
/// </summary>
/// <remarks>
///     <para>
///         Each combinator on its own is already a tight loop, but a chain of them is one loop and one
///         intermediate array per link: <c>xs.select(f).where(p).aggregate(0, r)</c> walks three times
///         and allocates two arrays nobody ever names. Fused, it walks once and allocates nothing.
///     </para>
///     <para>
///         The chain has to be read off the Loom tree rather than the emitted Luau, because by the time
///         a macro sees a call its receiver has already been generated - the inner loop is in the
///         prerequisite list before the outer combinator is asked what it wants. So this runs from
///         <see cref="LuauGenerator.VisitInvocation" />, ahead of the receiver being visited, and the
///         outermost call is the one that gets to claim the whole chain.
///     </para>
///     <para>
///         Fusing makes the stages interleave - <c>f</c> then <c>p</c> per element, rather than every
///         <c>f</c> then every <c>p</c> - and lets a short-circuiting terminal stop the stages above it
///         early. That is the semantics a combinator chain is written against, and the one LINQ has,
///         but it is observable from a callback with side effects.
///     </para>
/// </remarks>
internal sealed class ArrayPipeline(SemanticModel semanticModel, LuauState state, Func<Expression, LuauExpression> visit)
{
    /// <summary>Stages that pass elements along, so anything may follow them.</summary>
    private static readonly HashSet<string> _intermediates = ["select", "where"];

    /// <summary>Stages that consume the chain, so they may only ever be last.</summary>
    private static readonly HashSet<string> _terminals =
        ["select", "where", "aggregate", "any", "all", "count", "to_set", "select_many", "flatten"];

    private readonly record struct Stage(string Name, List<Expression> Arguments)
    {
        /// <summary>Where the callback sits in the argument list, or -1 when the stage takes none.</summary>
        public int CallbackIndex => Name switch
        {
            "select" or "where" or "any" or "all" or "count" or "select_many" => 0,
            "aggregate" => 1,
            _ => -1
        };

        /// <summary>How many arguments the callback is handed, so an over-long lambda is left alone.</summary>
        public int Arity => Name == "aggregate" ? 3 : 2;

        /// <summary>Which of those arguments is the index - the one a filter has to renumber.</summary>
        public int IndexPosition => Name == "aggregate" ? 2 : 1;

        /// <summary>Whether the stage writes its result at the position it was handed.</summary>
        /// <remarks><c>where</c> materializes too, but counts its own survivors rather than reading a position.</remarks>
        public bool WritesAtPosition => Name == "select";
    }

    private sealed class BoundStage(Stage stage)
    {
        public Stage Stage { get; } = stage;
        public List<LuauExpression> Arguments { get; set; } = [];
        public InlinedCallback? Inlined { get; set; }

        /// <summary>The hoisted closure, for a stage whose callback could not be inlined.</summary>
        public Identifier? Applied { get; set; }

        public LuauExpression? Callback => Stage.CallbackIndex >= 0 ? Arguments[Stage.CallbackIndex] : null;

        public bool ReadsIndex => Stage.CallbackIndex >= 0 && (Inlined == null || Stage.IndexPosition < Inlined.ParameterNames.Count);
    }

    public bool TryGenerate(Invocation invocation, [MaybeNullWhen(false)] out LuauExpression expression)
    {
        expression = null;
        if (!TryCollectChain(invocation, out var stages, out var sourceExpression))
            return false;

        var bound = stages.ConvertAll(stage => new BoundStage(stage));
        var source = state.PushToVariable(SourceName, visit(sourceExpression));
        foreach (var stage in bound)
            stage.Arguments = stage.Stage.Arguments.ConvertAll(argument => visit(argument));

        var reserved = new HashSet<string> { source.Name };
        var terminal = bound[^1];
        var answer = OpenTerminal(terminal, source, reserved, out var result, out var count);
        BindCallbacks(bound, reserved);

        var elementName = bound[0].Inlined is { ParameterNames: [var first, ..] } ? first : state.Scope.AddIdentifier(ElementName);
        var indexName = ChooseIndexName(bound);
        LuauExpression current = new Identifier(elementName);
        LuauExpression position = new Identifier(indexName);

        var body = new List<LuauStatement>();
        var statements = body;
        for (var i = 0; i < bound.Count - 1; i++)
        {
            var value = ApplyCallback(bound[i], statements, ref current, position);
            if (bound[i].Stage.Name == "select")
            {
                current = value;
                continue;
            }

            var kept = new List<LuauStatement>();
            statements.Add(new IfStatement(value, new Chunk(kept), [], null));
            statements = kept;
            if (!NeedsPosition(bound, i))
                continue;

            var compacted = new Identifier(state.Scope.AddIdentifier(CountName));
            state.Prereq(new LocalVariable(compacted.Name, null, new NumberLiteral(0)));
            statements.Add(new ExpressionStatement(new BinaryOperator(compacted, "+=", new NumberLiteral(1))));
            position = compacted;
        }

        CloseTerminal(terminal, statements, ref current, position, result, count);
        state.Prereq(new ForStatement([indexName, elementName], source, new Chunk(body)));
        expression = answer;

        return true;
    }

    /// <summary>
    ///     Walks the receiver chain outwards-in, stopping at the first thing that is not a stage - which
    ///     becomes the source the fused loop reads. A chain whose inner links are not fusable still fuses
    ///     the outer ones and treats the rest as its source, so a partial win is still taken.
    /// </summary>
    private bool TryCollectChain(Invocation invocation, out List<Stage> stages, out Expression source)
    {
        stages = [];
        var current = invocation;
        while (true)
        {
            if (!TryReadStage(current, stages.Count == 0 ? _terminals : _intermediates, out var name, out var receiver))
            {
                source = current;
                break;
            }

            stages.Add(new Stage(name, current.Arguments.ArgumentList));
            if (receiver is Invocation inner)
            {
                current = inner;
                continue;
            }

            source = receiver;
            break;
        }

        stages.Reverse();
        return stages.Count >= 2;
    }

    private bool TryReadStage(
        Invocation invocation,
        HashSet<string> allowed,
        [MaybeNullWhen(false)] out string name,
        [MaybeNullWhen(false)] out Expression receiver)
    {
        name = null;
        receiver = null;
        if (invocation.TypeArguments != null)
            return false;

        switch (invocation.Expression)
        {
            case PropertyAccess { Names: [var only] } property when !only.IsOptional:
            {
                name = only.Name.Text.Trim();
                receiver = property.Expression;
                break;
            }
            case QualifiedName { Names: [var only] } qualified when !only.IsOptional:
            {
                name = only.Name.Text.Trim();
                receiver = qualified.Identifier;
                break;
            }
            default:
                return false;
        }

        return allowed.Contains(name) && semanticModel.GetType(receiver) is ArrayType;
    }

    /// <summary>
    ///     Decides which callbacks may be inlined, and settles the one hazard fusing introduces: a
    ///     stage's parameter becomes an ordinary local, so it stays in scope over every stage below it,
    ///     where a callback written against a same-named variable from outside the loop would read it
    ///     instead. Unfused that cannot happen - each stage had a loop of its own to be confined to. A
    ///     stage whose parameter is named anywhere below it therefore keeps its closure, which binds
    ///     nothing and is hoisted clear of the loop.
    /// </summary>
    private void BindCallbacks(List<BoundStage> bound, HashSet<string> reserved)
    {
        foreach (var stage in bound)
            if (stage.Callback is { } callback && InlinedCallback.TryInline(callback, stage.Stage.Arity, reserved, out var inlined))
                stage.Inlined = inlined;

        var mentioned = new HashSet<string>();
        for (var i = bound.Count - 1; i >= 0; i--)
        {
            if (bound[i].Inlined is not { } inlined)
                continue;

            if (inlined.ParameterNames.Exists(mentioned.Contains))
            {
                bound[i].Inlined = null;
                continue;
            }

            // A stage's own parameters are bound by the stage itself, ahead of its body, so they shadow
            // anything a stage above it left in scope and are not what this is looking for.
            var references = new HashSet<string>();
            if (LuauIdentifiers.TryCollect(new Chunk([.. inlined.Prelude, new Return(inlined.Value)]), references))
            {
                references.ExceptWith(inlined.ParameterNames);
                mentioned.UnionWith(references);
                continue;
            }

            // A body this cannot read may mention anything, so nothing above it may bind a name.
            for (var j = 0; j < i; j++)
                bound[j].Inlined = null;

            break;
        }

        foreach (var stage in bound)
        {
            if (stage.Inlined is { } inlined)
            {
                foreach (var parameterName in inlined.ParameterNames)
                    state.Scope.Reserve(parameterName);

                continue;
            }

            if (stage.Callback is { } callback)
                stage.Applied = state.PushToVariable(CallbackName, callback);
        }
    }

    /// <summary>Names the loop's index variable after whoever reads it, and discards it when nobody does.</summary>
    private string ChooseIndexName(List<BoundStage> bound)
    {
        if (bound[0].Inlined is { } inlined && bound[0].Stage.IndexPosition < inlined.ParameterNames.Count)
            return inlined.ParameterNames[bound[0].Stage.IndexPosition];

        return NeedsPosition(bound, -1) ? state.Scope.AddIdentifier(IndexName) : DiscardName;
    }

    /// <summary>Whether anything after <paramref name="index" /> still reads the position it is standing at.</summary>
    private static bool NeedsPosition(List<BoundStage> bound, int index)
    {
        for (var i = index + 1; i < bound.Count; i++)
            if (bound[i].ReadsIndex || i == bound.Count - 1 && bound[i].Stage.WritesAtPosition)
                return true;

        return false;
    }

    /// <summary>Declares whatever the terminal accumulates into, and hands back what the chain evaluates to.</summary>
    private LuauExpression OpenTerminal(BoundStage terminal, Identifier source, HashSet<string> reserved, out Identifier? result, out Identifier? count)
    {
        result = null;
        count = null;
        switch (terminal.Stage.Name)
        {
            case "select" or "where":
                result = Declare(ResultName, reserved, name => new ConstVariable(name, null, LuauFactory.TableCall("create", [new UnaryOperator("#", source)])));
                return result;

            case "select_many" or "flatten":
                result = Declare(ResultName, reserved, name => new ConstVariable(name, null, new Table([])));
                count = Declare(CountName, reserved, name => new LocalVariable(name, null, new NumberLiteral(0)));
                return result;

            case "to_set":
                result = Declare(ResultName, reserved, name => new ConstVariable(name, null, new Table([])));
                return result;

            case "aggregate":
                result = Declare(AccumulatorName, reserved, name => new LocalVariable(name, null, terminal.Arguments[0]));
                return result;

            case "count":
                count = Declare(CountName, reserved, name => new LocalVariable(name, null, new NumberLiteral(0)));
                return count;

            default:
            {
                var matched = terminal.Stage.Name == "any";
                result = Declare(matched ? FoundName : SatisfiedName, reserved, name => new LocalVariable(name, null, new BooleanLiteral(!matched)));
                return result;
            }
        }
    }

    private Identifier Declare(string name, HashSet<string> reserved, Func<string, LuauStatement> declaration)
    {
        var allocated = state.Scope.AddIdentifier(name);
        reserved.Add(allocated);
        state.Prereq(declaration(allocated));

        return new Identifier(allocated);
    }

    private void CloseTerminal(
        BoundStage terminal,
        List<LuauStatement> statements,
        ref LuauExpression current,
        LuauExpression position,
        Identifier? result,
        Identifier? count)
    {
        switch (terminal.Stage.Name)
        {
            case "select":
            {
                var mapped = ApplyCallback(terminal, statements, ref current, position);
                statements.Add(new ExpressionStatement(new BinaryOperator(new ElementAccess(result!, position), "=", mapped)));
                return;
            }
            case "where":
            {
                var condition = ApplyCallback(terminal, statements, ref current, position);
                var kept = new List<LuauStatement>();
                statements.Add(new IfStatement(condition, new Chunk(kept), [], null));

                var written = new Identifier(state.Scope.AddIdentifier(CountName));
                state.Prereq(new LocalVariable(written.Name, null, new NumberLiteral(0)));
                kept.Add(new ExpressionStatement(new BinaryOperator(written, "+=", new NumberLiteral(1))));
                kept.Add(new ExpressionStatement(new BinaryOperator(new ElementAccess(result!, written), "=", current)));
                return;
            }
            case "select_many":
            {
                var segment = ApplyCallback(terminal, statements, ref current, position);
                AppendSegment(state, statements, result!, count!, segment);
                return;
            }
            case "flatten":
            {
                AppendSegment(state, statements, result!, count!, current);
                return;
            }
            case "to_set":
            {
                statements.Add(new ExpressionStatement(new BinaryOperator(new ElementAccess(result!, current), "=", new BooleanLiteral(true))));
                return;
            }
            case "aggregate":
            {
                var next = ApplyAggregate(terminal, statements, result!, current, position);
                statements.Add(new ExpressionStatement(new BinaryOperator(result!, "=", next)));
                return;
            }
            case "count":
            {
                var condition = ApplyCallback(terminal, statements, ref current, position);
                statements.Add(
                    new IfStatement(condition, new Chunk([new ExpressionStatement(new BinaryOperator(count!, "+=", new NumberLiteral(1)))]), [], null)
                );

                return;
            }
            default:
            {
                var matched = terminal.Stage.Name == "any";
                var condition = ApplyCallback(terminal, statements, ref current, position);
                var decide = new Chunk([new ExpressionStatement(new BinaryOperator(result!, "=", new BooleanLiteral(matched))), new Break()]);
                if (matched)
                {
                    statements.Add(new IfStatement(condition, decide, [], null));
                    return;
                }

                statements.Add(new IfStatement(condition, new Chunk([new Continue()]), [], null));
                statements.AddRange(decide.Statements);
                return;
            }
        }
    }

    /// <summary>
    ///     Evaluates one stage against the element standing at this point in the loop, either by binding
    ///     the inlined callback's parameters to it or by calling the closure hoisted for it.
    /// </summary>
    /// <remarks>
    ///     The element is named before the stage reads it, because a stage that both tests an element and
    ///     passes it on would otherwise evaluate the whole chain above it twice. The binding is free when
    ///     the stage is inlined: the name it needs is the callback's own parameter.
    /// </remarks>
    private LuauExpression ApplyCallback(BoundStage stage, List<LuauStatement> statements, ref LuauExpression current, LuauExpression position)
    {
        current = BindElement(stage, statements, current);
        if (stage.Inlined is not { } inlined)
            return new Call(stage.Applied!, [current, position]);

        if (inlined.ParameterNames.Count > stage.Stage.IndexPosition)
            BindName(statements, inlined.ParameterNames[stage.Stage.IndexPosition], position);

        statements.AddRange(inlined.Prelude);
        return inlined.Value;
    }

    private LuauExpression ApplyAggregate(BoundStage stage, List<LuauStatement> statements, Identifier carried, LuauExpression current, LuauExpression position)
    {
        if (stage.Inlined is not { } inlined)
            return new Call(stage.Applied!, [carried, current, position]);

        var names = inlined.ParameterNames;
        if (names.Count > 0)
            BindName(statements, names[0], carried);

        if (names.Count > 1)
            BindName(statements, names[1], current);

        if (names.Count > 2)
            BindName(statements, names[2], position);

        statements.AddRange(inlined.Prelude);
        return inlined.Value;
    }

    private LuauExpression BindElement(BoundStage stage, List<LuauStatement> statements, LuauExpression current)
    {
        if (stage.Inlined is { ParameterNames: [var parameterName, ..] })
            return BindName(statements, parameterName, current);

        if (current is Identifier)
            return current;

        return BindName(statements, state.Scope.AddIdentifier(ElementName), current);
    }

    private static Identifier BindName(List<LuauStatement> statements, string name, LuauExpression value)
    {
        if (value is Identifier existing && existing.Name == name)
            return existing;

        statements.Add(new ConstVariable(name, null, value));
        return new Identifier(name);
    }
}
