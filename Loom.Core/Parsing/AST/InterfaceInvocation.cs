using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class InterfaceInvocation(Token keyword, Name name, TypeArguments? typeArguments, InterfaceInvocationBody body)
    : Expression([keyword, ..name.Tokens, ..typeArguments?.Tokens ?? [], ..body.Tokens], [name, typeArguments, body])
{
    public Token Keyword { get; } = keyword;
    public Name Name { get; } = name;
    public TypeArguments? TypeArguments { get; } = typeArguments;
    public InterfaceInvocationBody Body { get; } = body;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitInterfaceInvocation(this);
}