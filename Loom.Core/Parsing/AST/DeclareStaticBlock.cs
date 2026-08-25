using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

/// <summary>
///     The companion-block form for a type alias's ambient statics (e.g. <c>declare static Result { ok:
///     fn&lt;T, Error&gt;(value: T): Result&lt;T, Error&gt;; }</c>). A type alias has no body of its own to
///     declare <c>static</c> members inline in the way an interface does, so this stands in for that -
///     signature-only, no bodies, trusted to exist the same way a <c>declare interface</c>'s own statics are.
/// </summary>
public sealed class DeclareStaticBlock(Token staticKeyword, Token name, Token leftBrace, Token rightBrace, List<PropertyDeclaration> members)
    : DeclareSignature([staticKeyword], name, [..members])
{
    public Token StaticKeyword { get; } = staticKeyword;
    public Token LeftBrace { get; } = leftBrace;
    public Token RightBrace { get; } = rightBrace;
    public List<PropertyDeclaration> Members { get; } = members;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitDeclareStaticBlock(this);
}
