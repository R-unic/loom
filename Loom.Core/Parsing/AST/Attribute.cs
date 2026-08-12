using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class Attribute(Expression expression, TypeArguments? typeArguments, Arguments? arguments)
    : Invocation(expression, typeArguments, arguments ?? EmptyArgumentsAt(expression))
{
    public bool IsInvoked => Arguments.LeftParen != Arguments.RightParen;

    /// <summary>
    ///     The name the attribute was written with: the last identifier of its expression, so a decorator
    ///     reached through a namespace (<c>meta.replicated</c>) answers with <c>replicated</c>.
    /// </summary>
    /// <remarks>
    ///     Read off the expression alone, never off the whole attribute: the arguments are part of the node
    ///     too, so <c>[attribute_usage(AttributeTargets.Property)]</c> would otherwise answer <c>Property</c>.
    /// </remarks>
    public string? Name => Expression.AllTokens().LastOrDefault(token => token.Kind == SyntaxKind.Identifier)?.Text;

    /// <summary>An attribute written without parentheses still needs an argument list; it sits where the expression ends.</summary>
    private static Arguments EmptyArgumentsAt(Expression expression)
    {
        var last = expression.LastToken()!;
        return new Arguments(last, last, []);
    }

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitAttribute(this);
}