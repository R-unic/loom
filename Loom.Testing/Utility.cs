using Loom.Config;
using Loom.Core.Diagnostics;
using Loom.Core.FlowAnalysis;
using Loom.Core.Generation;
using Loom.Core.Lexing;
using Loom.Core.Parsing;
using Loom.Core.Parsing.AST;
using Loom.Core.Pipeline;
using Loom.Core.Resolving;
using Loom.Core.Text;
using Loom.Core.TypeChecking;
using Loom.LanguageServer;
using Loom.Luau.AST;
using OmniSharp.Extensions.LanguageServer.Protocol;
using Resolver = Loom.Core.Resolving.Resolver;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.Testing;

/// <summary>
///     Shared test scaffolding, one file per pipeline stage - <see cref="Utility.Lexing" />,
///     <see cref="Utility.Parsing" />, and onward mirror <c>Loom.Core</c>'s own stage-by-stage layout, since
///     each helper only ever calls the stage before it. Cross-cutting concerns that are not a single stage -
///     assertions, temp-project scaffolding, the language server - get a file of their own instead.
/// </summary>
internal static partial class Utility
{
    public static readonly LocationSpan Span = LocationSpan.Empty(TestFile(""));

    public static SourceFile TestFile(string source) => new("test", source);

    private static LexerResult Tokenize(string source) => new Lexer(TestFile(source)).Tokenize();
}
