using Loom.Core.Diagnostics;
using Loom.Core.Parsing;
using Loom.Core.Parsing.AST;
using Loom.Core.Pipeline;

namespace Loom.Testing;

internal static partial class Utility
{
    public static Tree GetAST(string source) => Parse(source).Tree;
    public static ParserResult Parse(string source) => Parse(source, out _);
    public static DiagnosticBag GetParserDiagnostics(string source) => Parse(source).Diagnostics;

    /// <summary>
    ///     Parses <paramref name="source" />, handing back the lexer's and parser's diagnostics in
    ///     <paramref name="upstream" /> so a later stage's result can carry them the way
    ///     <see cref="Compiler" /> does. Without that a malformed source silently reaches the stage
    ///     under test: the parser recovers, the stage types the recovered node as <c>never</c>, and an
    ///     assertion on the stage's own bag alone sees no errors at all.
    /// </summary>
    private static ParserResult Parse(string source, out DiagnosticBag upstream)
    {
        var lexerResult = Tokenize(source);
        var parserResult = new Parser(lexerResult).Parse();
        upstream = DiagnosticBag.Concat([lexerResult.Diagnostics, parserResult.Diagnostics]);

        return parserResult;
    }
}
