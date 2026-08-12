using Loom.Core.Parsing.AST;
using Loom.Core.Pipeline;
using Loom.Core.Text;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Loom.LanguageServer;

/// <summary>
///     What the editor may collapse: the bracketed regions of the syntax, the runs of comments between them,
///     and the import block at the top of the file.
/// </summary>
public static class FoldingRanges
{
    public static IReadOnlyList<FoldingRange> Of(CompiledFile file)
    {
        var ranges = new List<FoldingRange>();
        foreach (var node in file.Tree.EnumerateDescendants())
            if (BracketsOf(node) is var (open, close))
                AddBrackets(ranges, open, close);

        AddImports(ranges, file.Tree);
        AddComments(ranges, file.Tree);

        return ranges;
    }

    /// <summary>The pair of brackets a node is written between, or a pair of nulls for a node written without any.</summary>
    private static (Token? Open, Token? Close) BracketsOf(Node node) =>
        node switch
        {
            Block block => (block.LeftBrace, block.RightBrace),
            InterfaceBody body => (body.LeftBrace, body.RightBrace),
            TraitBody body => (body.LeftBrace, body.RightBrace),
            ImplementBody body => (body.LeftBrace, body.RightBrace),
            EnumDeclaration @enum => (@enum.LeftBrace, @enum.RightBrace),
            MatchExpression match => (match.LeftBrace, match.RightBrace),
            InterfaceInvocationBody body => (body.LeftBrace, body.RightBrace),
            ObjectPattern pattern => (pattern.LeftBrace, pattern.RightBrace),
            ArrayLiteral array => (array.LeftBracket, array.RightBracket),
            _ => (null, null)
        };

    /// <summary>
    ///     The run of imports a file opens with, as one region. Only a leading run: an import written further
    ///     down is somewhere the reader went looking for, and folding it away would hide what they wanted.
    /// </summary>
    private static void AddImports(List<FoldingRange> ranges, Tree tree)
    {
        var imports = tree.Statements.TakeWhile(statement => statement is ImportDeclaration).ToArray();
        if (imports.Length < 2)
            return;

        AddLines(ranges, LineOf(imports[0].AllTokens()[0]), LineOf(imports[^1].LastToken()!), FoldingRangeKind.Imports);
    }

    /// <summary>
    ///     Comment regions. No node stands for a comment - the parser drops trivia - but the tree keeps the
    ///     whole token stream it was built from, trivia included, which is where the comments still are.
    /// </summary>
    private static void AddComments(List<FoldingRange> ranges, Tree tree)
    {
        Token? runStart = null;
        Token? runEnd = null;

        foreach (var token in tree.Tokens)
            switch (token.Kind)
            {
                case SyntaxKind.BlockComment:
                    AddLines(ranges, LineOf(token), token.GetLocation().End.Line - 1, FoldingRangeKind.Comment);
                    break;
                // a run of line comments folds as one region; whitespace inside it does not break the run,
                // but any real token does - the comments after it are about something else
                case SyntaxKind.Comment or SyntaxKind.DocComment:
                    runStart ??= token;
                    runEnd = token;
                    break;
                case SyntaxKind.Whitespace:
                    break;
                default:
                    AddRun(ranges, runStart, runEnd);
                    runStart = null;
                    runEnd = null;
                    break;
            }

        AddRun(ranges, runStart, runEnd);
    }

    private static void AddRun(List<FoldingRange> ranges, Token? start, Token? end)
    {
        if (start != null && end != null && start != end)
            AddLines(ranges, LineOf(start), LineOf(end), FoldingRangeKind.Comment);
    }

    /// <summary>
    ///     A bracketed region, ending on the line before the closing bracket so that folding leaves the bracket
    ///     visible and the shape of the code readable while collapsed.
    /// </summary>
    private static void AddBrackets(List<FoldingRange> ranges, Token? open, Token? close)
    {
        if (open != null && close != null)
            AddLines(ranges, LineOf(open), LineOf(close) - 1, null);
    }

    /// <summary>Records a region, unless it fits on one line - there is nothing to collapse there.</summary>
    private static void AddLines(List<FoldingRange> ranges, int startLine, int endLine, FoldingRangeKind? kind)
    {
        if (endLine > startLine)
            ranges.Add(new FoldingRange { StartLine = startLine, EndLine = endLine, Kind = kind });
    }

    private static int LineOf(Token token) => token.GetLocation().Start.Line - 1;
}
