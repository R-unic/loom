using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

/// <summary>
///     <c>mut [K from keyof(T)]: T[K]</c> - an interface body that describes one member per key of
///     another type rather than naming its members itself.
/// </summary>
public class MappedTypeDeclaration(
    Token? mutKeyword,
    Token leftBracket,
    Token rightBracket,
    Token name,
    Token fromKeyword,
    TypeExpression sourceType,
    ColonTypeClause colonTypeClause)
    : Statement([mutKeyword, leftBracket, name, fromKeyword, rightBracket], [sourceType, colonTypeClause])
{
    public Token? MutKeyword { get; } = mutKeyword;
    public Token LeftBracket { get; } = leftBracket;
    public Token RightBracket { get; } = rightBracket;
    /// <summary>The binder each key is bound to - <c>K</c> above.</summary>
    public Token Name { get; } = name;
    public Token FromKeyword { get; } = fromKeyword;
    /// <summary>The keys to map over - <c>keyof(T)</c> above.</summary>
    public TypeExpression SourceType { get; } = sourceType;
    /// <summary>What each key maps to, written in terms of the binder.</summary>
    public ColonTypeClause ColonTypeClause { get; } = colonTypeClause;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitMappedTypeDeclaration(this);
}
