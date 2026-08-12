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
    private static readonly HashSet<string> _intermediates = ["select", "where", "select_many", "flatten"];

    /// <summary>Terminals that leave the loop early, which a stage nesting a loop inside it cannot do.</summary>
    private static readonly HashSet<string> _shortCircuiting = ["any", "all"];

    /// <summary>Stages that consume the chain, so they may only ever be last.</summary>
    private static readonly HashSet<string> _terminals =
        ["select", "where", "aggregate", "any", "all", "count", "to_set", "select_many", "flatten"];

    /// <param name="MeasuresOnly">
    ///     Set where the chain's array is only ever measured, so the terminal counts what it would have
    ///     appended instead of building the array to ask it how long it is.
    /// </param>
    private readonly record struct Stage(string Name, List<Expression> Arguments, bool MeasuresOnly = false)
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

        /// <summary>Which of those arguments is the element, which <c>aggregate</c> takes second.</summary>
        public int ElementPosition => Name == "aggregate" ? 1 : 0;

        /// <summary>Whether the stage writes its result at the position it was handed.</summary>
        /// <remarks><c>where</c> materializes too, but counts its own survivors rather than reading a position.</remarks>
        public bool WritesAtPosition => Name == "select";

        /// <summary>Whether the stage turns one element into a run of them, which needs a loop of its own.</summary>
        public bool Spreads => Name is "select_many" or "flatten";

        /// <summary>Whether what follows the stage is positioned against a fresh count rather than the one above.</summary>
        public bool Renumbers => Name == "where" || Spreads;
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

    public bool TryGenerate(Invocation invocation, [MaybeNullWhen(false)] out LuauExpression expression) =>
        TryGenerate(invocation, minimumStages: 2, measured: false, out expression);

    /// <summary>
    ///     Rewrites <c>chain.length</c> so the chain counts rather than collects. A filter already visits
    ///     every element the count needs, and the array it fills is a fresh one nobody else holds, so
    ///     building it to read <c>#</c> off it is the whole of the wasted work - one allocation and one
    ///     write per surviving element. Worth claiming even for a single stage, which is why this does not
    ///     wait for a chain to fuse.
    /// </summary>
    /// <remarks>
    ///     <c>select</c> is deliberately not rewritten. Its length is its source's, but dropping it would
    ///     drop whatever the transform does, and nothing here can yet say a transform does nothing.
    /// </remarks>
    public bool TryGenerateLength(PropertyAccess access, [MaybeNullWhen(false)] out LuauExpression expression)
    {
        expression = null;

        return access.Names is [{ IsOptional: false } only]
            && only.Name.Text.Trim() == "length"
            && access.Expression is Invocation invocation
            && semanticModel.GetType(invocation) is ArrayType
            && TryGenerate(invocation, minimumStages: 1, measured: true, out expression);
    }

    private bool TryGenerate(Invocation invocation, int minimumStages, bool measured, [MaybeNullWhen(false)] out LuauExpression expression)
    {
        expression = null;
        if (!TryCollectChain(invocation, minimumStages, out var stages, out var sourceExpression))
            return false;

        if (measured && !TryMeasureTerminal(stages))
            return false;

        // Arguments before the receiver, which is the order VisitInvocation evaluates them in. Only a
        // callback expression that does something on the way to being a callback can tell, but the fused
        // path should not be the one place that order differs.
        var bound = stages.ConvertAll(stage => new BoundStage(stage));
        foreach (var stage in bound)
            stage.Arguments = stage.Stage.Arguments.ConvertAll(argument => visit(argument));

        var source = state.PushToVariable(SourceName, visit(sourceExpression));

        var reserved = new HashSet<string> { source.Name };
        var terminal = bound[^1];
        var answer = OpenTerminal(terminal, source, reserved, out var result, out var count);
        BindCallbacks(bound, reserved);

        var elementName = ElementNameFor(bound, 0, bound[0].Stage.Name == "flatten" ? SegmentName : ElementName);
        var indexName = ChooseIndexName(bound);
        LuauExpression current = new Identifier(elementName);
        LuauExpression position = new Identifier(indexName);

        var body = new List<LuauStatement>();
        var statements = body;
        for (var i = 0; i < bound.Count - 1; i++)
        {
            var stage = bound[i];
            if (stage.Stage.Name == "select")
            {
                current = ApplyCallback(stage, statements, ref current, position);
                continue;
            }

            if (stage.Stage.Spreads)
            {
                var segment = stage.Stage.CallbackIndex >= 0 ? ApplyCallback(stage, statements, ref current, position) : current;
                var spreadName = ElementNameFor(bound, i + 1, ElementName);
                var spread = new List<LuauStatement>();
                statements.Add(new ForStatement([DiscardName, spreadName], segment, new Chunk(spread)));
                statements = spread;
                current = new Identifier(spreadName);
                position = Renumber(bound, i, statements, position);
                continue;
            }

            var condition = ApplyCallback(stage, statements, ref current, position);
            var kept = new List<LuauStatement>();
            statements.Add(new IfStatement(condition, new Chunk(kept), [], null));
            statements = kept;
            position = Renumber(bound, i, statements, position);
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
    /// <summary>Marks the terminal as measured, or answers false where measuring it would drop work.</summary>
    private static bool TryMeasureTerminal(List<Stage> stages)
    {
        if (stages[^1].Name is not ("where" or "select_many" or "flatten"))
            return false;

        stages[^1] = stages[^1] with { MeasuresOnly = true };
        return true;
    }

    private bool TryCollectChain(Invocation invocation, int minimumStages, out List<Stage> stages, out Expression source)
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

            var stage = new Stage(name, current.Arguments.ArgumentList);

            // A spread nests a loop inside the body, and the break an 'any' or 'all' ends on would only
            // leave that one. The chain stops here instead, so the spread keeps a loop of its own.
            if (stages.Count > 0 && stage.Spreads && _shortCircuiting.Contains(stages[0].Name))
            {
                source = current;
                break;
            }

            stages.Add(stage);
            if (receiver is Invocation inner)
            {
                current = inner;
                continue;
            }

            source = receiver;
            break;
        }

        stages.Reverse();
        return stages.Count >= minimumStages;
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

    /// <summary>
    ///     Names a loop variable after the parameter of the stage that reads it, so the stage needs no
    ///     binding of its own. Only a loop header can do this - anywhere else the element is an
    ///     expression that has to be named before it can be read twice.
    /// </summary>
    private string ElementNameFor(List<BoundStage> bound, int index, string fallback)
    {
        if (index < bound.Count
            && bound[index].Inlined is { } inlined
            && bound[index].Stage.ElementPosition < inlined.ParameterNames.Count)
            return inlined.ParameterNames[bound[index].Stage.ElementPosition];

        return state.Scope.AddIdentifier(fallback);
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
        {
            if (bound[i].ReadsIndex || i == bound.Count - 1 && bound[i].Stage.WritesAtPosition)
                return true;

            // A stage that renumbers hands everything below it a position of its own, so what they read
            // is that one and not this. Whether it renumbers is this same question asked one stage later.
            if (i < bound.Count - 1 && bound[i].Stage.Renumbers)
                return false;
        }

        return false;
    }

    /// <summary>Starts counting positions afresh below a stage that filtered or spread what came in.</summary>
    private LuauExpression Renumber(List<BoundStage> bound, int index, List<LuauStatement> statements, LuauExpression position)
    {
        if (!NeedsPosition(bound, index))
            return position;

        var counter = new Identifier(state.Scope.AddIdentifier(CountName));
        state.Prereq(new LocalVariable(counter.Name, null, new NumberLiteral(0)));
        statements.Add(new ExpressionStatement(new BinaryOperator(counter, "+=", new NumberLiteral(1))));

        return counter;
    }

    /// <summary>Declares whatever the terminal accumulates into, and hands back what the chain evaluates to.</summary>
    private LuauExpression OpenTerminal(BoundStage terminal, Identifier source, HashSet<string> reserved, out Identifier? result, out Identifier? count)
    {
        result = null;
        count = null;
        if (terminal.Stage.MeasuresOnly)
        {
            count = Declare(CountName, reserved, name => new LocalVariable(name, null, new NumberLiteral(0)));
            return count;
        }

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
                if (terminal.Stage.MeasuresOnly)
                {
                    statements.Add(
                        new IfStatement(condition, new Chunk([new ExpressionStatement(new BinaryOperator(count!, "+=", new NumberLiteral(1)))]), [], null)
                    );

                    return;
                }

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
                if (terminal.Stage.MeasuresOnly)
                    statements.Add(new ExpressionStatement(new BinaryOperator(count!, "+=", new UnaryOperator("#", segment))));
                else
                    AppendSegment(state, statements, result!, count!, segment);

                return;
            }
            case "flatten":
            {
                if (terminal.Stage.MeasuresOnly)
                    statements.Add(new ExpressionStatement(new BinaryOperator(count!, "+=", new UnaryOperator("#", current))));
                else
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
