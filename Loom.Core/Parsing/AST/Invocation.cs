namespace Loom.Core.Parsing.AST;

public class Invocation(Expression expression, TypeArguments? typeArguments, Arguments arguments)
    : Expression([..expression.Tokens, ..arguments.Tokens], [expression, arguments])
{
    public Expression Expression { get; } = expression;
    public TypeArguments? TypeArguments { get; } = typeArguments;
    public Arguments Arguments { get; } = arguments;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitInvocation(this);
}