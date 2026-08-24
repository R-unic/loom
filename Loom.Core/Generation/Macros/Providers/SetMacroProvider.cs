using System.Diagnostics.CodeAnalysis;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Core.TypeChecking.Types;
using Loom.Luau;
using Loom.Luau.AST;
using BinaryOperator = Loom.Luau.AST.BinaryOperator;
using Break = Loom.Luau.AST.Break;
using Continue = Loom.Luau.AST.Continue;
using ElementAccess = Loom.Luau.AST.ElementAccess;
using ExpressionStatement = Loom.Luau.AST.ExpressionStatement;
using Identifier = Loom.Luau.AST.Identifier;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.Core.Generation.Macros.Providers;

/// <summary>
///     Lowers the members declared on <c>Set</c>/<c>MutSet</c> in <c>loom.loom</c> inline, so a set stays
///     the plain table <see cref="SetStaticMacroProvider" /> builds - keys are the members - rather than a
///     table carrying a closure per operation.
/// </summary>
/// <remarks>
///     Membership is <c>value == true</c> rather than <c>value ~= nil</c> throughout. The declared indexer
///     is <c>[T]: bool</c> and <c>MutSet</c>'s is mutable, so a caller may write <c>false</c> through it
///     directly; reading that back as "present" would contradict what they wrote.
/// </remarks>
internal sealed class SetMacroProvider : IMacroProvider
{
    private const string OtherName = "_other";
    private const string ResultName = "_result";
    private const string CountName = "_count";
    private const string KeyName = "_key";
    private const string SubsetName = "_subset";

    private static readonly HashSet<string> _members =
        ["has", "add", "remove", "union", "intersect", "difference", "is_subset_of", "to_array"];

    public bool Supports(SemanticModel _, Type type) => IsSet(type);
    public bool Supports(SemanticModel semanticModel, Expression expression) => IsSet(semanticModel.GetType(expression));

    public bool IsInvocationOnlyMember(string memberName) => _members.Contains(memberName);

    public bool TryProperty(MacroContext context, string name, LuauExpression target, [MaybeNullWhen(false)] out LuauExpression expression)
    {
        switch (name)
        {
            case "size":
                expression = GenerateSize(context, target);
                return true;
            case "is_empty":
                // 'next(t) == nil' is the whole test: '#t' counts the array part, which a set never fills.
                expression = new BinaryOperator(new Call(new Identifier("next"), [target]), "==", new NilLiteral());
                return true;
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
        if (!_members.Contains(name))
        {
            expression = null;
            return false;
        }

        var set = MacroContext.GetCallObject(call);
        switch (name)
        {
            case "has":
                expression = IsMember(set, call.Arguments.Single());
                return true;
            case "add":
                expression = Mutate(context, new ElementAccess(set, call.Arguments.Single()), new BooleanLiteral(true));
                return true;
            case "remove":
                expression = Mutate(context, new ElementAccess(set, call.Arguments.Single()), new NilLiteral());
                return true;
            case "union":
                expression = GenerateUnion(context, set, call.Arguments.Single());
                return true;
            case "intersect":
                expression = GenerateCombination(context, set, call.Arguments.Single(), keepWhenPresent: true);
                return true;
            case "difference":
                expression = GenerateCombination(context, set, call.Arguments.Single(), keepWhenPresent: false);
                return true;
            case "is_subset_of":
                expression = GenerateIsSubsetOf(context, set, call.Arguments.Single());
                return true;
            case "to_array":
                expression = GenerateToArray(context, set);
                return true;
        }

        expression = null;
        return false;
    }

    private static LuauExpression IsMember(LuauExpression set, LuauExpression value, bool present = true) =>
        new BinaryOperator(new ElementAccess(set, value), present ? "==" : "~=", new BooleanLiteral(true));

    /// <summary>
    ///     An assignment is a Luau statement, not an expression, so it can only be returned as the macro's
    ///     value where the generator is about to write it as one. Anywhere else - <c>let x = s.add(1)</c>,
    ///     where the caller binds the <c>void</c> result - it has to be hoisted, or the emitted Luau would
    ///     be <c>local x = s[1] = true</c>, which does not parse.
    /// </summary>
    private static LuauExpression Mutate(MacroContext context, ElementAccess slot, LuauExpression value)
    {
        var assignment = new BinaryOperator(slot, "=", value);
        if (context.Node.Parent is Parsing.AST.ExpressionStatement)
            return assignment;

        context.State.Prereq(new ExpressionStatement(assignment));
        return new NilLiteral();
    }

    private static LuauExpression GenerateSize(MacroContext context, LuauExpression set)
    {
        var count = context.State.PushToVariable(CountName, new NumberLiteral(0), isConst: false);
        var key = context.State.Scope.AddIdentifier(KeyName);
        context.State.Prereq(
            new ForStatement(
                [key],
                set,
                new Chunk([new ExpressionStatement(new BinaryOperator(count, "+=", new NumberLiteral(1)))])
            )
        );

        return count;
    }

    /// <summary>Clones rather than copying key by key, so only the smaller side is walked.</summary>
    private static LuauExpression GenerateUnion(MacroContext context, LuauExpression set, LuauExpression otherSet)
    {
        var other = context.State.PushToVariable(OtherName, otherSet);
        var result = context.State.PushToVariable(ResultName, LuauFactory.TableCall("clone", [set]));
        var key = context.State.Scope.AddIdentifier(KeyName);
        context.State.Prereq(
            new ForStatement(
                [key],
                other,
                new Chunk([new ExpressionStatement(new BinaryOperator(new ElementAccess(result, new Identifier(key)), "=", new BooleanLiteral(true)))])
            )
        );

        return result;
    }

    /// <summary>Intersection and difference differ only in which side of the membership test they keep.</summary>
    private static LuauExpression GenerateCombination(MacroContext context, LuauExpression set, LuauExpression otherSet, bool keepWhenPresent)
    {
        var other = context.State.PushToVariable(OtherName, otherSet);
        var result = context.State.PushToVariable(ResultName, new Table([]));
        var key = context.State.Scope.AddIdentifier(KeyName);
        var keyIdentifier = new Identifier(key);
        var keep = new Chunk([new ExpressionStatement(new BinaryOperator(new ElementAccess(result, keyIdentifier), "=", new BooleanLiteral(true)))]);
        context.State.Prereq(
            new ForStatement(
                [key],
                set,
                new Chunk([new IfStatement(IsMember(other, keyIdentifier, keepWhenPresent), keep, [], null)])
            )
        );

        return result;
    }

    private static LuauExpression GenerateIsSubsetOf(MacroContext context, LuauExpression set, LuauExpression otherSet)
    {
        var other = context.State.PushToVariable(OtherName, otherSet);
        var subset = context.State.PushToVariable(SubsetName, new BooleanLiteral(true), isConst: false);
        var key = context.State.Scope.AddIdentifier(KeyName);
        context.State.Prereq(
            new ForStatement(
                [key],
                set,
                new Chunk(
                    [
                        new IfStatement(IsMember(other, new Identifier(key)), new Chunk([new Continue()]), [], null),
                        new ExpressionStatement(new BinaryOperator(subset, "=", new BooleanLiteral(false))),
                        new Break()
                    ]
                )
            )
        );

        return subset;
    }

    private static LuauExpression GenerateToArray(MacroContext context, LuauExpression set)
    {
        var result = context.State.PushToVariable(ResultName, new Table([]));
        var count = context.State.PushToVariable(CountName, new NumberLiteral(0), isConst: false);
        var key = context.State.Scope.AddIdentifier(KeyName);
        context.State.Prereq(
            new ForStatement(
                [key],
                set,
                new Chunk(
                    [
                        new ExpressionStatement(new BinaryOperator(count, "+=", new NumberLiteral(1))),
                        new ExpressionStatement(new BinaryOperator(new ElementAccess(result, count), "=", new Identifier(key)))
                    ]
                )
            )
        );

        return result;
    }

    // A set reaches here as an InstantiatedType over the generic interface, not as the interface itself.
    private static bool IsSet(Type type) => IsSet(type, 0);

    private static bool IsSet(Type type, int depth) =>
        depth < 4
        && type switch
        {
            InterfaceType { Name: "Set" or "MutSet", IsIntrinsic: true } => true,
            InstantiatedType instantiated => IsSet(instantiated.Expand(), depth + 1),
            _ => false
        };
}
