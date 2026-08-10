using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Core.Resolving.Symbols;
using Loom.Core.Text;
using Attribute = Loom.Core.Parsing.AST.Attribute;

namespace Loom.LanguageServer;

/// <summary>An in-progress call the cursor sits inside, and which argument of it the cursor is on.</summary>
public sealed record CallSite(Invocation Invocation, int ArgumentIndex);

public static class CallSiteFinder
{
    /// <summary>
    ///     Every declaration a call's callee names. More than one only for an overload set, whose shapes the
    ///     type checker merged into a single type - each shape's parameter names live on its own declaration.
    /// </summary>
    public static IReadOnlyList<Symbol> SymbolsOf(Expression callee, SemanticModel semanticModel)
    {
        if (semanticModel.GetPropertySymbols(callee) is { Count: > 0 } properties)
            return properties;

        var symbol = semanticModel.GetSymbol(callee) ?? semanticModel.GetNamespaceMemberSymbol(callee);
        return symbol == null ? [] : [symbol];
    }

    /// <summary>The name a call should be rendered under, which for a member is the member's rather than the receiver's.</summary>
    public static string NameOf(Expression callee, Symbol? symbol) =>
        callee switch
        {
            QualifiedName { Names: [.., var last] } => last.Name.Text,
            PropertyAccess { Names: [.., var last] } => last.Name.Text,
            _ => symbol?.Name ?? callee.ToString()
        };

    /// <summary>
    ///     The innermost call whose argument list contains <paramref name="offset" />, or null when the cursor
    ///     is not writing an argument. Innermost wins so that <c>outer(inner(|))</c> describes <c>inner</c>.
    /// </summary>
    public static CallSite? FindAt(Tree tree, int offset)
    {
        Invocation? innermost = null;
        foreach (var invocation in tree.EnumerateDescendants<Invocation>())
        {
            // an attribute is an invocation too, but one whose arguments are written before its own
            // parentheses exist when uninvoked - and there is nothing to help with in '[serializable]'
            if (invocation is Attribute { IsInvoked: false })
                continue;

            var arguments = invocation.Arguments;
            if (!Between(arguments.LeftParen, arguments.RightParen, offset))
                continue;

            if (innermost == null || arguments.Span.Length < innermost.Arguments.Span.Length)
                innermost = invocation;
        }

        return innermost == null ? null : new CallSite(innermost, ArgumentIndexAt(innermost, offset));
    }

    /// <summary>The cursor is inside the list when it is past the opening paren and no further than the closing one.</summary>
    private static bool Between(Token leftParen, Token rightParen, int offset) => offset >= leftParen.Span.End && offset <= rightParen.Span.Position;

    /// <summary>
    ///     Which argument the cursor is writing, counted as the commas before it. Counted over the source text
    ///     rather than over the parsed argument list, because a half-written call has fewer arguments parsed
    ///     than it has commas typed - <c>add(1, </c> is at argument two with only one argument in the tree.
    /// </summary>
    private static int ArgumentIndexAt(Invocation invocation, int offset)
    {
        var text = invocation.Arguments.LeftParen.File.SourceText;
        var index = 0;
        var depth = 0;
        var quote = '\0';

        for (var position = invocation.Arguments.LeftParen.Span.End; position < offset && position < text.Length; position++)
        {
            var character = text[position];
            if (quote != '\0')
            {
                if (character == '\\')
                    position++;
                else if (character == quote)
                    quote = '\0';

                continue;
            }

            switch (character)
            {
                case '"' or '\'':
                    quote = character;
                    break;
                case '(' or '[' or '{':
                    depth++;
                    break;
                case ')' or ']' or '}':
                    depth--;
                    break;
                case ',' when depth == 0:
                    index++;
                    break;
            }
        }

        return index;
    }
}
