using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Core.Text;
using Loom.Luau.AST;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.Core.Pipeline;

public sealed class CompiledFile(SourceFile sourceFile)
{
    public SourceFile SourceFile { get; } = sourceFile;

    /// <summary>The project the file came from, whose config decided <see cref="Path" /> and whether the file is written at all.</summary>
    public required SourceRoot Root { get; init; }

    public required string Path { get; init; }
    public required DiagnosticBag Diagnostics { get; init; }
    public required string RenderedLuau { get; init; }
    public required LuauTree LuauTree { get; init; }
    public required Type ReturnType { get; init; }
    public required SemanticModel SemanticModel { get; init; }
    public required Tree Tree { get; init; }
    public required IReadOnlyList<Token> Tokens { get; init; }

    /// <summary>
    ///     Every token the lexer produced, comments and whitespace included, in source order. The parser
    ///     never sees these - <see cref="Tokens" /> is what it was built from - but a caller describing the
    ///     file as written rather than as parsed needs the trivia back, and re-lexing to get it would run the
    ///     lexer a second time over text the pipeline has already read.
    /// </summary>
    public required IReadOnlyList<Token> TokensWithTrivia { get; init; }
}
