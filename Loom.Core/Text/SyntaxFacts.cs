namespace Loom.Core.Text;

public static class SyntaxFacts
{
    public static readonly Dictionary<string, SyntaxKind> KeywordMap = new(
        [
            new KeyValuePair<string, SyntaxKind>("true", SyntaxKind.TrueLiteral),
            new KeyValuePair<string, SyntaxKind>("false", SyntaxKind.FalseLiteral),
            new KeyValuePair<string, SyntaxKind>("none", SyntaxKind.NoneLiteral),
            new KeyValuePair<string, SyntaxKind>("let", SyntaxKind.LetKeyword),
            new KeyValuePair<string, SyntaxKind>("mut", SyntaxKind.MutKeyword),
            new KeyValuePair<string, SyntaxKind>("type", SyntaxKind.TypeKeyword),
            new KeyValuePair<string, SyntaxKind>("interface", SyntaxKind.InterfaceKeyword),
            new KeyValuePair<string, SyntaxKind>("export", SyntaxKind.ExportKeyword),
            new KeyValuePair<string, SyntaxKind>("import", SyntaxKind.ImportKeyword),
            new KeyValuePair<string, SyntaxKind>("fn", SyntaxKind.FnKeyword),
            new KeyValuePair<string, SyntaxKind>("async", SyntaxKind.AsyncKeyword),
            new KeyValuePair<string, SyntaxKind>("await", SyntaxKind.AwaitKeyword),
            new KeyValuePair<string, SyntaxKind>("return", SyntaxKind.ReturnKeyword),
            new KeyValuePair<string, SyntaxKind>("continue", SyntaxKind.ContinueKeyword),
            new KeyValuePair<string, SyntaxKind>("break", SyntaxKind.BreakKeyword),
            new KeyValuePair<string, SyntaxKind>("event", SyntaxKind.EventKeyword),
            new KeyValuePair<string, SyntaxKind>("enum", SyntaxKind.EnumKeyword),
            new KeyValuePair<string, SyntaxKind>("every", SyntaxKind.EveryKeyword),
            new KeyValuePair<string, SyntaxKind>("after", SyntaxKind.AfterKeyword),
            new KeyValuePair<string, SyntaxKind>("if", SyntaxKind.IfKeyword),
            new KeyValuePair<string, SyntaxKind>("else", SyntaxKind.ElseKeyword),
            new KeyValuePair<string, SyntaxKind>("while", SyntaxKind.WhileKeyword),
            new KeyValuePair<string, SyntaxKind>("for", SyntaxKind.ForKeyword),
            new KeyValuePair<string, SyntaxKind>("in", SyntaxKind.InKeyword),
            new KeyValuePair<string, SyntaxKind>("nameof", SyntaxKind.NameOfKeyword),
            new KeyValuePair<string, SyntaxKind>("typeof", SyntaxKind.TypeOfKeyword),
            new KeyValuePair<string, SyntaxKind>("keyof", SyntaxKind.KeyOfKeyword),
            new KeyValuePair<string, SyntaxKind>("declare", SyntaxKind.DeclareKeyword),
            new KeyValuePair<string, SyntaxKind>("as", SyntaxKind.AsKeyword),
            new KeyValuePair<string, SyntaxKind>("new", SyntaxKind.NewKeyword),
            new KeyValuePair<string, SyntaxKind>("sealed", SyntaxKind.SealedKeyword),
            new KeyValuePair<string, SyntaxKind>("trait", SyntaxKind.TraitKeyword),
            new KeyValuePair<string, SyntaxKind>("implement", SyntaxKind.ImplementKeyword),
            new KeyValuePair<string, SyntaxKind>("static", SyntaxKind.StaticKeyword),
            new KeyValuePair<string, SyntaxKind>("match", SyntaxKind.MatchKeyword),
            new KeyValuePair<string, SyntaxKind>("when", SyntaxKind.WhenKeyword),
            new KeyValuePair<string, SyntaxKind>("is", SyntaxKind.IsKeyword),
            new KeyValuePair<string, SyntaxKind>("not", SyntaxKind.NotKeyword),
            new KeyValuePair<string, SyntaxKind>("with", SyntaxKind.WithKeyword)
        ]
    );
    public static readonly Dictionary<string, SyntaxKind> OperatorMap = new(
        [
            new KeyValuePair<string, SyntaxKind>("+", SyntaxKind.Plus),
            new KeyValuePair<string, SyntaxKind>("+=", SyntaxKind.PlusEquals),
            new KeyValuePair<string, SyntaxKind>("-", SyntaxKind.Minus),
            new KeyValuePair<string, SyntaxKind>("-=", SyntaxKind.MinusEquals),
            new KeyValuePair<string, SyntaxKind>("*", SyntaxKind.Star),
            new KeyValuePair<string, SyntaxKind>("*=", SyntaxKind.StarEquals),
            new KeyValuePair<string, SyntaxKind>("/", SyntaxKind.Slash),
            new KeyValuePair<string, SyntaxKind>("/=", SyntaxKind.SlashEquals),
            new KeyValuePair<string, SyntaxKind>("//", SyntaxKind.SlashSlash),
            new KeyValuePair<string, SyntaxKind>("//=", SyntaxKind.SlashSlashEquals),
            new KeyValuePair<string, SyntaxKind>("^", SyntaxKind.Caret),
            new KeyValuePair<string, SyntaxKind>("^=", SyntaxKind.CaretEquals),
            new KeyValuePair<string, SyntaxKind>("%", SyntaxKind.Percent),
            new KeyValuePair<string, SyntaxKind>("%=", SyntaxKind.PercentEquals),
            new KeyValuePair<string, SyntaxKind>("&", SyntaxKind.Ampersand),
            new KeyValuePair<string, SyntaxKind>("&=", SyntaxKind.AmpersandEquals),
            new KeyValuePair<string, SyntaxKind>("|", SyntaxKind.Pipe),
            new KeyValuePair<string, SyntaxKind>("|=", SyntaxKind.PipeEquals),
            new KeyValuePair<string, SyntaxKind>("~", SyntaxKind.Tilde),
            new KeyValuePair<string, SyntaxKind>("~=", SyntaxKind.TildeEquals),
            new KeyValuePair<string, SyntaxKind>(">>", SyntaxKind.RArrowRArrow),
            new KeyValuePair<string, SyntaxKind>(">>=", SyntaxKind.RArrowRArrowEquals),
            new KeyValuePair<string, SyntaxKind>(">>>", SyntaxKind.RArrowRArrowRArrow),
            new KeyValuePair<string, SyntaxKind>(">>>=", SyntaxKind.RArrowRArrowRArrowEquals),
            new KeyValuePair<string, SyntaxKind>("<<", SyntaxKind.LArrowLArrow),
            new KeyValuePair<string, SyntaxKind>("<<=", SyntaxKind.LArrowLArrowEquals),
            new KeyValuePair<string, SyntaxKind>("&&", SyntaxKind.AmpersandAmpersand),
            new KeyValuePair<string, SyntaxKind>("&&=", SyntaxKind.AmpersandAmpersandEquals),
            new KeyValuePair<string, SyntaxKind>("||", SyntaxKind.PipePipe),
            new KeyValuePair<string, SyntaxKind>("||=", SyntaxKind.PipePipeEquals),
            new KeyValuePair<string, SyntaxKind>("=", SyntaxKind.Equals),
            new KeyValuePair<string, SyntaxKind>("==", SyntaxKind.EqualsEquals),
            new KeyValuePair<string, SyntaxKind>("!", SyntaxKind.Bang),
            new KeyValuePair<string, SyntaxKind>("!=", SyntaxKind.BangEquals),
            new KeyValuePair<string, SyntaxKind>(">", SyntaxKind.RArrow),
            new KeyValuePair<string, SyntaxKind>(">=", SyntaxKind.RArrowEquals),
            new KeyValuePair<string, SyntaxKind>("<", SyntaxKind.LArrow),
            new KeyValuePair<string, SyntaxKind>("<=", SyntaxKind.LArrowEquals),
            new KeyValuePair<string, SyntaxKind>("?", SyntaxKind.Question),
            new KeyValuePair<string, SyntaxKind>("??", SyntaxKind.QuestionQuestion),
            new KeyValuePair<string, SyntaxKind>("??=", SyntaxKind.QuestionQuestionEquals),
            new KeyValuePair<string, SyntaxKind>("(", SyntaxKind.LParen),
            new KeyValuePair<string, SyntaxKind>(")", SyntaxKind.RParen),
            new KeyValuePair<string, SyntaxKind>("[", SyntaxKind.LBracket),
            new KeyValuePair<string, SyntaxKind>("]", SyntaxKind.RBracket),
            new KeyValuePair<string, SyntaxKind>("{", SyntaxKind.LBrace),
            new KeyValuePair<string, SyntaxKind>("}", SyntaxKind.RBrace),
            new KeyValuePair<string, SyntaxKind>(":", SyntaxKind.Colon),
            new KeyValuePair<string, SyntaxKind>(",", SyntaxKind.Comma),
            new KeyValuePair<string, SyntaxKind>(".", SyntaxKind.Dot),
            new KeyValuePair<string, SyntaxKind>("..", SyntaxKind.DotDot),
            new KeyValuePair<string, SyntaxKind>("?.", SyntaxKind.QuestionDot),
            new KeyValuePair<string, SyntaxKind>("->", SyntaxKind.Arrow),
            new KeyValuePair<string, SyntaxKind>("::", SyntaxKind.ColonColon),
            new KeyValuePair<string, SyntaxKind>("::<", SyntaxKind.ColonColonLArrow),
            new KeyValuePair<string, SyntaxKind>(";", SyntaxKind.Semicolon),
            new KeyValuePair<string, SyntaxKind>("@", SyntaxKind.At)
        ]
    );

    private static readonly HashSet<string> _primitiveTypeNames =
    [
        "number",
        "string",
        "bool",
        "never",
        "unknown",
        "none",
        "void",
        "u8",
        "u16",
        "u32",
        "i8",
        "i16",
        "i32",
        "f32",
        "f64"
    ];
    private static readonly HashSet<SyntaxKind> _triviaSyntaxes =
        [SyntaxKind.Whitespace, SyntaxKind.Comment, SyntaxKind.BlockComment, SyntaxKind.DocComment];
    private static readonly HashSet<SyntaxKind> _literalSyntaxes =
    [
        SyntaxKind.NumberLiteral, SyntaxKind.StringLiteral, SyntaxKind.TrueLiteral, SyntaxKind.FalseLiteral, SyntaxKind.NoneLiteral
    ];
    private static readonly HashSet<SyntaxKind> _exportableKeywords =
    [
        SyntaxKind.FnKeyword,
        SyntaxKind.AsyncKeyword,
        SyntaxKind.LetKeyword,
        SyntaxKind.MutKeyword,
        SyntaxKind.TypeKeyword,
        SyntaxKind.InterfaceKeyword,
        SyntaxKind.SealedKeyword,
        SyntaxKind.EnumKeyword,
        SyntaxKind.TraitKeyword,
        SyntaxKind.EventKeyword
    ];
    private static readonly HashSet<SyntaxKind> _assignmentOperators =
    [
        SyntaxKind.Equals,
        SyntaxKind.PlusEquals,
        SyntaxKind.MinusEquals,
        SyntaxKind.StarEquals,
        SyntaxKind.SlashEquals,
        SyntaxKind.SlashSlashEquals,
        SyntaxKind.PercentEquals,
        SyntaxKind.CaretEquals,
        SyntaxKind.AmpersandEquals,
        SyntaxKind.PipeEquals,
        SyntaxKind.TildeEquals,
        SyntaxKind.LArrowLArrowEquals,
        SyntaxKind.RArrowRArrowEquals,
        SyntaxKind.RArrowRArrowRArrowEquals,
        SyntaxKind.AmpersandAmpersandEquals,
        SyntaxKind.PipePipeEquals,
        SyntaxKind.QuestionQuestionEquals
    ];
    private static readonly HashSet<SyntaxKind> _bitwiseOperators =
    [
        SyntaxKind.Ampersand,
        SyntaxKind.AmpersandEquals,
        SyntaxKind.Pipe,
        SyntaxKind.PipeEquals,
        SyntaxKind.Tilde,
        SyntaxKind.TildeEquals,
        SyntaxKind.RArrowRArrow,
        SyntaxKind.RArrowRArrowEquals,
        SyntaxKind.RArrowRArrowRArrow,
        SyntaxKind.RArrowRArrowRArrowEquals,
        SyntaxKind.LArrowLArrow,
        SyntaxKind.LArrowLArrowEquals
    ];
    private static readonly HashSet<SyntaxKind> _unaryOperators = [SyntaxKind.Minus, SyntaxKind.Tilde, SyntaxKind.Bang];
    private static readonly Dictionary<SyntaxKind, string> _operatorTextByKind = OperatorMap.ToDictionary(kv => kv.Value, kv => kv.Key);
    private static readonly Dictionary<SyntaxKind, string> _keywordTextByKind = KeywordMap.ToDictionary(kv => kv.Value, kv => kv.Key);

    public static bool IsExportableKeyword(SyntaxKind kind) => _exportableKeywords.Contains(kind);
    public static bool IsNotTrivia(SyntaxKind kind) => !IsTrivia(kind);
    public static bool IsTrivia(SyntaxKind kind) => _triviaSyntaxes.Contains(kind);
    public static bool IsLiteral(SyntaxKind kind) => _literalSyntaxes.Contains(kind);
    public static string? GetOperatorText(SyntaxKind syntax) => _operatorTextByKind.GetValueOrDefault(syntax);
    public static string? GetKeywordText(SyntaxKind syntax) => _keywordTextByKind.GetValueOrDefault(syntax);
    public static string? GetText(SyntaxKind syntax) => GetKeywordText(syntax) ?? GetOperatorText(syntax);

    public static bool IsAssignmentOperator(SyntaxKind kind) => _assignmentOperators.Contains(kind);
    public static bool IsBitwiseOperator(SyntaxKind kind) => _bitwiseOperators.Contains(kind);
    public static bool IsUnaryOperator(SyntaxKind kind) => _unaryOperators.Contains(kind);
    public static bool IsPrimitiveType(string name) => _primitiveTypeNames.Contains(name);

    /// <summary>Every built-in type name. They are lexer keywords rather than symbols, so anything enumerating what a type annotation may name has to add them itself.</summary>
    public static IReadOnlyCollection<string> PrimitiveTypeNames => _primitiveTypeNames;

    /// <summary>Every keyword, for callers offering the language's own words alongside the names in scope.</summary>
    public static IReadOnlyCollection<string> KeywordNames => KeywordMap.Keys;
}