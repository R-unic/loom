using System.Text;
using System.Diagnostics.CodeAnalysis;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Luau;
using Loom.Luau.AST;
using BinaryOperator = Loom.Luau.AST.BinaryOperator;
using Parenthesized = Loom.Luau.AST.Parenthesized;
using PrimitiveType = Loom.Core.TypeChecking.Types.PrimitiveType;
using Type = Loom.Core.TypeChecking.Types.Type;
using UnaryOperator = Loom.Luau.AST.UnaryOperator;

namespace Loom.Core.Generation.Macros.Providers;

internal sealed class StringMacroProvider : IMacroProvider
{
    public bool Supports(SemanticModel _, Type type) => type.IsAssignableTo(PrimitiveType.String);
    public bool Supports(SemanticModel _, Expression __) => false;

    public bool IsInvocationOnlyMember(string memberName) =>
        memberName is "upper" or "lower" or "trim" or "replace" or "reverse" or "repeat" or "split" or "has" or "index_of" or "starts_with" or "ends_with"
            or "byte";

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
        var str = MacroContext.GetCallObject(call);
        switch (name)
        {
            case "upper":
            {
                expression = LuauFactory.StringCall("upper", [str]);
                return true;
            }
            case "lower":
            {
                expression = LuauFactory.StringCall("lower", [str]);
                return true;
            }
            case "reverse":
            {
                expression = LuauFactory.StringCall("reverse", [str]);
                return true;
            }
            case "split":
            {
                expression = LuauFactory.StringCall("split", [str, ..call.Arguments]);
                return true;
            }
            case "repeat":
            {
                expression = LuauFactory.StringCall("rep", [str, ..call.Arguments]);
                return true;
            }
            case "byte":
            {
                expression = LuauFactory.StringCall("byte", [str, ..call.Arguments]);
                return true;
            }
            case "trim":
            {
                expression = new Parenthesized(
                    LuauFactory.StringCall("gsub", [str, new StringLiteral("^%s*(.-)%s*$"), new StringLiteral("%1")])
                );

                return true;
            }
            case "replace":
            {
                expression = new Parenthesized(LuauFactory.StringCall("gsub", [str, ..call.Arguments]));
                return true;
            }
            case "has":
            {
                expression = new BinaryOperator(
                    LuauFactory.StringCall("find", [str, call.Arguments.Single(), new NumberLiteral(1), new BooleanLiteral(true)]),
                    "~=",
                    new NilLiteral()
                );

                return true;
            }
            case "index_of":
            {
                expression = LuauFactory.StringCall("find", [str, call.Arguments.Single(), new NumberLiteral(1), new BooleanLiteral(true)]);
                return true;
            }
            case "starts_with":
            {
                var prefix = context.State.PushIfRepeated("_prefix", call.Arguments.Single());
                expression = new BinaryOperator(LuauFactory.StringCall("sub", [str, new NumberLiteral(1), LengthOf(prefix)]), "==", prefix);
                return true;
            }
            case "ends_with":
            {
                var self = context.State.PushIfRepeated("_str", str);
                var suffix = context.State.PushIfRepeated("_suffix", call.Arguments.Single());
                expression = new BinaryOperator(LuauFactory.StringCall("sub", [self, LastCharacters(self, LengthOf(suffix))]), "==", suffix);
                return true;
            }
        }

        expression = null;
        return false;
    }

    /// <summary>
    ///     Where <c>string.sub</c> has to start to read the last <paramref name="count" /> characters.
    ///     Written out that is <c>#s - count + 1</c>, but a known count folds the two into one subtraction
    ///     - and a one-character suffix into none at all.
    /// </summary>
    private static LuauExpression LastCharacters(LuauExpression self, LuauExpression count) =>
        count is not NumberLiteral { Value: var length }
            ? new BinaryOperator(new BinaryOperator(LengthOf(self), "-", count), "+", new NumberLiteral(1))
            : Math.Abs(length - 1) < 1e-8
                ? LengthOf(self)
                : new BinaryOperator(LengthOf(self), "-", new NumberLiteral(length - 1));

    /// <summary>
    ///     The length of a literal is known here, so <c>s.starts_with("ab")</c> compares against 2 rather
    ///     than measuring a constant at runtime. Luau measures a string in bytes, which is what the source
    ///     was read as, so a literal outside ASCII counts the same either way.
    /// </summary>
    private static LuauExpression LengthOf(LuauExpression expression) =>
        expression is StringLiteral literal
            ? new NumberLiteral(Encoding.UTF8.GetByteCount(literal.Value))
            : new UnaryOperator("#", expression);
}
