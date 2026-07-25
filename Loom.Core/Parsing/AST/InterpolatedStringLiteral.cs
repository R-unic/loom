using Loom.Core.Text;

namespace Loom.Core.Parsing.AST;

public class InterpolatedStringLiteral(Token startToken, List<InterpolationPart> parts, Token endToken)
    : Expression([startToken, ..parts.SelectMany(PartTokens), endToken], Holes(parts))
{
    public Token StartToken { get; } = startToken;
    public List<InterpolationPart> Parts { get; } = parts;
    public Token EndToken { get; } = endToken;
    public List<Expression> Expressions { get; } = Holes(parts).ToList();

    public override T Accept<T>(Visitor<T> visitor) => visitor.VisitInterpolatedStringLiteral(this);

    private static IEnumerable<Expression> Holes(List<InterpolationPart> parts) =>
        parts.OfType<InterpolationHolePart>().Select(p => p.Expression);

    private static IEnumerable<Token?> PartTokens(InterpolationPart part) =>
        part switch
        {
            InterpolationTextPart text => [text.Token],
            InterpolationHolePart hole => [hole.LeftBrace, ..hole.Expression.Tokens, hole.RightBrace],
            _ => []
        };
}
