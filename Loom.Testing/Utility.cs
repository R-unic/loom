using Loom.Core.Lexing;
using Loom.Core.Text;

namespace Loom.Testing;

/// <summary>
///     Shared test scaffolding, one file per pipeline stage - <c>Utility.Lexing.cs</c>, <c>Utility.Parsing.cs</c>,
///     and onward mirror <c>Loom.Core</c>'s own stage-by-stage layout, since each helper only ever calls the
///     stage before it. Cross-cutting concerns that are not a single stage - assertions, temp-project
///     scaffolding, the language server - get a file of their own instead.
/// </summary>
internal static partial class Utility
{
    public static readonly LocationSpan Span = LocationSpan.Empty(TestFile(""));

    public static SourceFile TestFile(string source) => new("test", source);

    private static LexerResult Tokenize(string source) => new Lexer(TestFile(source)).Tokenize();
}
