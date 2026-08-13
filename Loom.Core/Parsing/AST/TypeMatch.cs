using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

/// <summary>
///     <c>match T { Future&lt;let V&gt; -&gt; V, _ -&gt; T }</c> - the n-armed form of a type-level branch,
///     which is where nesting <see cref="ConditionalType" /> stops being readable.
/// </summary>
/// <remarks>
///     <c>match each T</c> runs the arms once per member of a union rather than against the whole type.
///     TypeScript distributes over a naked type parameter silently, which is both what makes
///     <c>Exclude</c> work and what forces the tuple-wrapping trick to opt out; <c>each</c> says it
///     outright instead.
/// </remarks>
public class TypeMatch(Token keyword, Token? eachKeyword, TypeExpression subject, Token leftBrace, Token rightBrace, List<TypeMatchArm> arms)
    : TypeExpression([keyword, eachKeyword, leftBrace, rightBrace], [subject, ..arms])
{
    public Token Keyword { get; } = keyword;
    public Token? EachKeyword { get; } = eachKeyword;
    public TypeExpression Subject { get; } = subject;
    public Token LeftBrace { get; } = leftBrace;
    public Token RightBrace { get; } = rightBrace;
    public List<TypeMatchArm> Arms { get; } = arms;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitTypeMatch(this);
}
