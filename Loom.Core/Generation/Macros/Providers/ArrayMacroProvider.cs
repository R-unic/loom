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
using ExpressionStatement = Loom.Luau.AST.ExpressionStatement;
using Identifier = Loom.Luau.AST.Identifier;
using Type = Loom.Core.TypeChecking.Types.Type;
using UnaryOperator = Loom.Luau.AST.UnaryOperator;

namespace Loom.Core.Generation.Macros.Providers;

internal sealed class ArrayMacroProvider : IMacroProvider
{
    public bool Supports(SemanticModel _, Type type) => type is ArrayType;
    public bool Supports(SemanticModel _, Expression __) => false;

    public bool IsInvocationOnlyMember(string memberName) =>
        memberName is "join" or "push" or "pop" or "insert" or "remove" or "index_of" or "has" or "select" or "where" or "aggregate" or "select_many"
            or "any" or "all" or "count" or "flatten" or "to_set";

    public bool TryProperty(MacroContext context, string name, LuauExpression target, [MaybeNullWhen(false)] out LuauExpression expression)
    {
        switch (name)
        {
            case "length":
            {
                expression = new UnaryOperator("#", target);
                return true;
            }
        }

        expression = null;
        return false;
    }

    public bool TryInvocation(
        MacroContext context,
        string name,
        TypeArguments? typeArguments,
        Call call,
        [MaybeNullWhen(false)] out LuauExpression expression)
    {
        var array = MacroContext.GetCallObject(call);
        switch (name)
        {
            case "join":
            {
                expression = LuauFactory.TableCall("concat", [array, ..call.Arguments]);
                return true;
            }
            case "push" or "insert":
            {
                expression = LuauFactory.TableCall("insert", [array, ..call.Arguments]);
                return true;
            }
            case "pop" or "remove":
            {
                expression = LuauFactory.TableCall("remove", [array, ..call.Arguments]);
                return true;
            }
            case "index_of":
            {
                expression = LuauFactory.TableCall("find", [array, ..call.Arguments]);
                return true;
            }
            case "has":
            {
                expression = new BinaryOperator(LuauFactory.TableCall("find", [array, ..call.Arguments]), "~=", new NilLiteral());
                return true;
            }
            case "select":
            {
                expression = GenerateSelect(context.State, array, call.Arguments[0]);
                return true;
            }
            case "where":
            {
                expression = GenerateWhere(context.State, array, call.Arguments[0]);
                return true;
            }
            case "aggregate":
            {
                expression = GenerateAggregate(context.State, array, call.Arguments[0], call.Arguments[1]);
                return true;
            }
            case "select_many":
            {
                expression = GenerateSelectMany(context.State, array, call.Arguments[0]);
                return true;
            }
            case "flatten":
            {
                expression = GenerateFlatten(context.State, array);
                return true;
            }
            case "any":
            {
                expression = GenerateQuantifier(context.State, array, call.Arguments[0], FoundName, matched: true);
                return true;
            }
            case "all":
            {
                expression = GenerateQuantifier(context.State, array, call.Arguments[0], SatisfiedName, matched: false);
                return true;
            }
            case "count":
            {
                expression = GenerateCount(context.State, array, call.Arguments[0]);
                return true;
            }
            case "to_set":
            {
                expression = GenerateToSet(context.State, array);
                return true;
            }
        }

        expression = null;
        return false;
    }

    private static LuauExpression GenerateSelect(LuauState state, LuauExpression array, LuauExpression transform)
    {
        var source = state.PushToVariable(SourceName, array);
        var resultName = state.Scope.AddIdentifier(ResultName);
        var result = new Identifier(resultName);
        var callback = BindCallback(state, transform, 2, [resultName]);
        var applied = callback.Inlined == null ? state.PushToVariable(CallbackName, transform) : null;
        state.Prereq(new ConstVariable(resultName, null, LuauFactory.TableCall("create", [new UnaryOperator("#", source)])));

        var element = callback.OptionalName(state, 0, ElementName);
        var index = callback.RequiredName(state, 1, IndexName);
        var body = new List<LuauStatement>();
        LuauExpression mapped;
        if (callback.Inlined is { } inlined)
        {
            body.AddRange(inlined.Prelude);
            mapped = inlined.Value;
        }
        else
        {
            mapped = new Call(applied!, [new Identifier(element), new Identifier(index)]);
        }

        body.Add(new ExpressionStatement(new BinaryOperator(new ElementAccess(result, new Identifier(index)), "=", mapped)));
        state.Prereq(new ForStatement([index, element], source, new Chunk(body)));

        return result;
    }

    private static LuauExpression GenerateWhere(LuauState state, LuauExpression array, LuauExpression predicate)
    {
        var source = state.PushToVariable(SourceName, array);
        var resultName = state.Scope.AddIdentifier(ResultName);
        var countName = state.Scope.AddIdentifier(CountName);
        var result = new Identifier(resultName);
        var count = new Identifier(countName);
        var callback = BindCallback(state, predicate, 2, [resultName, countName]);
        var applied = callback.Inlined == null ? state.PushToVariable(CallbackName, predicate) : null;
        state.Prereq(
            new ConstVariable(resultName, null, LuauFactory.TableCall("create", [new UnaryOperator("#", source)])),
            new LocalVariable(countName, null, new NumberLiteral(0))
        );

        var element = callback.RequiredName(state, 0, ElementName);
        var index = callback.OptionalName(state, 1, IndexName);
        var body = new List<LuauStatement>();
        LuauExpression condition;
        if (callback.Inlined is { } inlined)
        {
            body.AddRange(inlined.Prelude);
            condition = inlined.Value;
        }
        else
        {
            condition = new Call(applied!, [new Identifier(element), new Identifier(index)]);
        }

        var keep = new Chunk(
            [
                new ExpressionStatement(new BinaryOperator(count, "+=", new NumberLiteral(1))),
                new ExpressionStatement(new BinaryOperator(new ElementAccess(result, count), "=", new Identifier(element)))
            ]
        );

        body.Add(new IfStatement(condition, keep, [], null));
        state.Prereq(new ForStatement([index, element], source, new Chunk(body)));

        return result;
    }

    private static LuauExpression GenerateAggregate(LuauState state, LuauExpression array, LuauExpression seed, LuauExpression reducer)
    {
        var source = state.PushToVariable(SourceName, array);
        var carriedName = state.Scope.AddIdentifier(AccumulatorName);
        var carried = new Identifier(carriedName);
        var callback = BindCallback(state, reducer, 3, [carriedName]);
        state.Prereq(new LocalVariable(carriedName, null, seed));
        var applied = callback.Inlined == null ? state.PushToVariable(CallbackName, reducer) : null;

        var element = callback.OptionalName(state, 1, ElementName);
        var index = callback.OptionalName(state, 2, IndexName);
        var body = new List<LuauStatement>();
        LuauExpression next;
        if (callback.Inlined is { } inlined)
        {
            if (inlined.ParameterNames.Count > 0)
                body.Add(new ConstVariable(inlined.ParameterNames[0], null, carried));

            body.AddRange(inlined.Prelude);
            next = inlined.Value;
        }
        else
        {
            next = new Call(applied!, [carried, new Identifier(element), new Identifier(index)]);
        }

        body.Add(new ExpressionStatement(new BinaryOperator(carried, "=", next)));
        state.Prereq(new ForStatement([index, element], source, new Chunk(body)));

        return carried;
    }

    private static LuauExpression GenerateSelectMany(LuauState state, LuauExpression array, LuauExpression transform)
    {
        var resultName = state.Scope.AddIdentifier(ResultName);
        var countName = state.Scope.AddIdentifier(CountName);
        var result = new Identifier(resultName);
        var count = new Identifier(countName);
        var callback = BindCallback(state, transform, 2, [resultName, countName]);
        var source = callback.Inlined == null ? state.PushToVariable(SourceName, array) : array;
        var applied = callback.Inlined == null ? state.PushToVariable(CallbackName, transform) : null;
        state.Prereq(
            new ConstVariable(resultName, null, new Table([])),
            new LocalVariable(countName, null, new NumberLiteral(0))
        );

        var element = callback.RequiredName(state, 0, ElementName);
        var index = callback.OptionalName(state, 1, IndexName);
        var body = new List<LuauStatement>();
        LuauExpression segment;
        if (callback.Inlined is { } inlined)
        {
            body.AddRange(inlined.Prelude);
            segment = inlined.Value;
        }
        else
        {
            segment = new Call(applied!, [new Identifier(element), new Identifier(index)]);
        }

        AppendSegment(state, body, result, count, segment);
        state.Prereq(new ForStatement([index, element], source, new Chunk(body)));

        return result;
    }

    private static LuauExpression GenerateFlatten(LuauState state, LuauExpression array)
    {
        var resultName = state.Scope.AddIdentifier(ResultName);
        var countName = state.Scope.AddIdentifier(CountName);
        var result = new Identifier(resultName);
        var count = new Identifier(countName);
        state.Prereq(
            new ConstVariable(resultName, null, new Table([])),
            new LocalVariable(countName, null, new NumberLiteral(0))
        );

        var segmentName = state.Scope.AddIdentifier(SegmentName);
        var body = new List<LuauStatement>();
        AppendSegment(state, body, result, count, new Identifier(segmentName));
        state.Prereq(new ForStatement([DiscardName, segmentName], array, new Chunk(body)));

        return result;
    }

    /// <summary>
    ///     Builds the same plain keys-are-members table <see cref="SetStaticMacroProvider" /> does, so a set
    ///     from an array is the same thing as a set from <c>Set.of</c> and needs no conversion between them.
    /// </summary>
    private static LuauExpression GenerateToSet(LuauState state, LuauExpression array)
    {
        var result = state.PushToVariable(ResultName, new Table([]));
        var body = new List<LuauStatement>();
        AddElementsToSet(state, body, result, array);
        state.Prereq(body.ToArray());

        return result;
    }

    private static LuauExpression GenerateQuantifier(LuauState state, LuauExpression array, LuauExpression predicate, string stateName, bool matched)
    {
        var answerName = state.Scope.AddIdentifier(stateName);
        var answer = new Identifier(answerName);
        var callback = BindCallback(state, predicate, 2, [answerName]);
        var source = callback.Inlined == null ? state.PushToVariable(SourceName, array) : array;
        var applied = callback.Inlined == null ? state.PushToVariable(CallbackName, predicate) : null;
        state.Prereq(new LocalVariable(answerName, null, new BooleanLiteral(!matched)));

        var element = callback.RequiredName(state, 0, ElementName);
        var index = callback.OptionalName(state, 1, IndexName);
        var body = new List<LuauStatement>();
        LuauExpression condition;
        if (callback.Inlined is { } inlined)
        {
            body.AddRange(inlined.Prelude);
            condition = inlined.Value;
        }
        else
        {
            condition = new Call(applied!, [new Identifier(element), new Identifier(index)]);
        }

        var decide = new Chunk([new ExpressionStatement(new BinaryOperator(answer, "=", new BooleanLiteral(matched))), new Break()]);
        if (matched)
            body.Add(new IfStatement(condition, decide, [], null));
        else
        {
            body.Add(new IfStatement(condition, new Chunk([new Continue()]), [], null));
            body.AddRange(decide.Statements);
        }

        state.Prereq(new ForStatement([index, element], source, new Chunk(body)));

        return answer;
    }

    private static LuauExpression GenerateCount(LuauState state, LuauExpression array, LuauExpression predicate)
    {
        var countName = state.Scope.AddIdentifier(CountName);
        var count = new Identifier(countName);
        var callback = BindCallback(state, predicate, 2, [countName]);
        var source = callback.Inlined == null ? state.PushToVariable(SourceName, array) : array;
        var applied = callback.Inlined == null ? state.PushToVariable(CallbackName, predicate) : null;
        state.Prereq(new LocalVariable(countName, null, new NumberLiteral(0)));

        var element = callback.RequiredName(state, 0, ElementName);
        var index = callback.OptionalName(state, 1, IndexName);
        var body = new List<LuauStatement>();
        LuauExpression condition;
        if (callback.Inlined is { } inlined)
        {
            body.AddRange(inlined.Prelude);
            condition = inlined.Value;
        }
        else
        {
            condition = new Call(applied!, [new Identifier(element), new Identifier(index)]);
        }

        body.Add(new IfStatement(condition, new Chunk([new ExpressionStatement(new BinaryOperator(count, "+=", new NumberLiteral(1)))]), [], null));
        state.Prereq(new ForStatement([index, element], source, new Chunk(body)));

        return count;
    }

    private readonly record struct BoundCallback(InlinedCallback? Inlined)
    {
        public string RequiredName(LuauState state, int position, string fallback) =>
            ParameterName(position) ?? state.Scope.AddIdentifier(fallback);

        public string OptionalName(LuauState state, int position, string fallback) =>
            ParameterName(position) ?? (Inlined != null ? DiscardName : state.Scope.AddIdentifier(fallback));

        private string? ParameterName(int position) =>
            Inlined != null && position < Inlined.ParameterNames.Count ? Inlined.ParameterNames[position] : null;
    }

    private static BoundCallback BindCallback(LuauState state, LuauExpression callback, int argumentCount, IReadOnlyCollection<string> reserved)
    {
        if (!InlinedCallback.TryInline(callback, argumentCount, reserved, out var inlined))
            return new BoundCallback(null);

        foreach (var parameterName in inlined.ParameterNames)
            state.Scope.Reserve(parameterName);

        return new BoundCallback(inlined);
    }
}
