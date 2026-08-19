using Loom.Core.Text;

namespace Loom.Core.Parsing;

internal sealed record BinaryPrecedenceLevel(bool RightAssociative, Predicate<SyntaxKind> Matches)
{
    public static readonly BinaryPrecedenceLevel[] Levels =
    [
        new(true, SyntaxFacts.IsAssignmentOperator),
        new(true, SyntaxKind.Question),
        new(true, SyntaxKind.QuestionQuestion),
        new(false, SyntaxKind.PipePipe),
        new(false, SyntaxKind.AmpersandAmpersand),
        new(false, SyntaxKind.Pipe),
        new(false, SyntaxKind.Tilde),
        new(false, SyntaxKind.Ampersand),
        new(false, SyntaxKind.EqualsEquals, SyntaxKind.BangEquals),
        new(
            false,
            SyntaxKind.LArrow,
            SyntaxKind.LArrowEquals,
            SyntaxKind.RArrow,
            SyntaxKind.RArrowEquals,
            SyntaxKind.InKeyword,
            SyntaxKind.IsKeyword
        ),
        new(false, SyntaxKind.LArrowLArrow, SyntaxKind.RArrowRArrow, SyntaxKind.RArrowRArrowRArrow),
        new(false, SyntaxKind.Plus, SyntaxKind.Minus),
        new(false, SyntaxKind.Star, SyntaxKind.Slash, SyntaxKind.SlashSlash, SyntaxKind.Percent),
        new(true, SyntaxKind.Caret),
        new(false, SyntaxKind.AsKeyword),
        new(false, SyntaxKind.WithKeyword)
    ];

    private BinaryPrecedenceLevel(bool rightAssociative, params SyntaxKind[] kinds)
        : this(rightAssociative, kinds.Contains)
    {
    }
}