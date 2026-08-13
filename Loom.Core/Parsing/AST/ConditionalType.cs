using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

/// <summary>
///     <c>T is U ? A : B</c> - the two-armed form of a type-level branch.
/// </summary>
/// <remarks>
///     The <c>?</c> is what tells this apart from <see cref="TypePredicateType" />, which is the same
///     <c>x is T</c> shape in a function's return position. Because the parser has to see past the
///     <c>is</c> target before it knows which of the two it is reading, a <c>?</c> directly on that
///     target is read as the branch rather than as an optional type - <c>T is number? ? A : B</c> needs
///     the target parenthesized.
/// </remarks>
public class ConditionalType(
    TypeExpression checkType,
    Token isKeyword,
    TypeExpression targetType,
    Token question,
    TypeExpression thenType,
    Token colon,
    TypeExpression elseType)
    : TypeExpression([isKeyword, question, colon], [checkType, targetType, thenType, elseType])
{
    public TypeExpression CheckType { get; } = checkType;
    public Token IsKeyword { get; } = isKeyword;
    /// <summary>What <see cref="CheckType" /> is measured against. May contain <see cref="InferType" /> binders.</summary>
    public TypeExpression TargetType { get; } = targetType;
    public Token Question { get; } = question;
    public TypeExpression ThenType { get; } = thenType;
    public Token Colon { get; } = colon;
    public TypeExpression ElseType { get; } = elseType;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitConditionalType(this);
}
