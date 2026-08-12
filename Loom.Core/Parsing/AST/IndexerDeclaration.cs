using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class IndexerDeclaration(Token? mutKeyword, Token leftBracket, Token rightBracket, TypeExpression indexType, ColonTypeClause colonTypeClause)
    : Statement([mutKeyword, leftBracket, rightBracket], [indexType, colonTypeClause])
{
    public Token? MutKeyword { get; } = mutKeyword;
    public Token LeftBracket { get; } = leftBracket;
    public Token RightBracket { get; } = rightBracket;
    public TypeExpression IndexType { get; } = indexType;
    public ColonTypeClause ColonTypeClause { get; } = colonTypeClause;

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitIndexerDeclaration(this);
}