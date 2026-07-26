using Loom.Core.Parsing.AST;
using Loom.Core.Text;

namespace Loom.Core.Parsing;

using StatementParser = Func<Token, Statement>;

public sealed partial class Parser
{
    private Dictionary<SyntaxKind, StatementParser> StatementParsers =>
        field ??= new Dictionary<SyntaxKind, StatementParser>
        {
            [SyntaxKind.ExportKeyword] = ParseExport,
            [SyntaxKind.ImportKeyword] = ParseImport,
            [SyntaxKind.LBrace] = ParseBlock,
            [SyntaxKind.ReturnKeyword] = ParseReturn,
            [SyntaxKind.FnKeyword] = ParseFunctionDeclaration,
            [SyntaxKind.LetKeyword] = ParseVariableDeclaration,
            [SyntaxKind.MutKeyword] = ParseVariableDeclaration,
            [SyntaxKind.TypeKeyword] = ParseTypeAlias,
            [SyntaxKind.EnumKeyword] = ParseEnumDeclaration,
            [SyntaxKind.EventKeyword] = keyword => ParseEventDeclaration(keyword, null),
            [SyntaxKind.DeclareKeyword] = ParseDeclare,
            [SyntaxKind.ImplementKeyword] = ParseImplement,
            [SyntaxKind.TraitKeyword] = ParseTraitDeclaration,
            [SyntaxKind.InterfaceKeyword] = ParseInterfaceDeclaration,
            [SyntaxKind.SealedKeyword] = ParseInterfaceDeclaration,
            [SyntaxKind.IfKeyword] = ParseIf,
            [SyntaxKind.ForKeyword] = ParseFor,
            [SyntaxKind.AfterKeyword] = ParseAfter,
            [SyntaxKind.WhileKeyword] = ParseWhile,
            [SyntaxKind.BreakKeyword] = ParseBreak,
            [SyntaxKind.ContinueKeyword] = ParseContinue
        };

    private Statement ParseStatement()
    {
        var statement = ParseStatementCore();
        Match(SyntaxKind.Semicolon);
        return statement;
    }

    private Statement ParseStatementCore()
    {
        if (IsEof())
            return new ExpressionStatement(ParseExpression());

        if (Current().Kind == SyntaxKind.LBracket && LooksLikeAttributesBeforeEvent())
        {
            var leftBracket = Advance();
            var attributes = ParseAttributes(leftBracket);
            var eventKeyword = Expect(SyntaxKind.EventKeyword);
            return ParseEventDeclaration(eventKeyword, attributes);
        }

        var token = Advance();
        var statementParser = StatementParsers.GetValueOrDefault(token.Kind);
        if (statementParser != null)
            return statementParser(token);

        _position--;
        return new ExpressionStatement(ParseExpression());
    }

    private bool LooksLikeAttributesBeforeEvent()
    {
        var end = OffsetAfterBrackets();
        return end != null && PeekKind(end.Value + 1) == SyntaxKind.EventKeyword;
    }

    private Block ParseBlock(Token leftBrace)
    {
        var statements = new List<Statement>();

        while (!IsEof())
        {
            if (Match(out var rightBrace, SyntaxKind.RBrace))
                return new Block(leftBrace, rightBrace, statements);

            var previousPosition = _position;
            statements.Add(ParseStatement());

            if (_position != previousPosition) continue;

            Synchronize();
            if (_position == previousPosition)
                Advance();
        }

        return new Block(leftBrace, MissingToken(SyntaxKind.RBrace), statements);
    }

    private Implement ParseImplement(Token keyword)
    {
        var traitNameIdentifier = ExpectIdentifier("trait name");
        var typeArguments = ParseTypeArguments();
        var traitName = new TypeName(traitNameIdentifier, typeArguments);
        var forKeyword = Expect(SyntaxKind.ForKeyword);
        var interfaceName = new TypeName(ExpectIdentifier("interface name"));
        var body = ParseImplementBody();

        return new Implement(keyword, traitName, forKeyword, interfaceName, body);
    }

    private ImplementBody ParseImplementBody()
    {
        var leftBrace = Expect(SyntaxKind.LBrace);
        var implementations = ParseImplementMethods();
        var rightBrace = Expect(SyntaxKind.RBrace);

        return new ImplementBody(leftBrace, rightBrace, implementations);
    }

    private List<FunctionDeclaration> ParseImplementMethods()
    {
        var members = new List<Statement>();
        while (Match(out var fnKeyword, SyntaxKind.FnKeyword))
        {
            members.Add(ParseFunctionDeclaration(fnKeyword));
            Match(SyntaxKind.Comma, SyntaxKind.Semicolon);
        }

        return members.OfType<FunctionDeclaration>().ToList();
    }

    private Return ParseReturn(Token keyword)
    {
        if (IsEof() || Current().Kind is SyntaxKind.RBrace or SyntaxKind.Semicolon || AtStatementKeyword())
            return new Return(keyword, null);

        var expression = ParseExpression();
        return new Return(keyword, expression);
    }

    private bool AtStatementKeyword() => !IsEof() && StatementParsers.ContainsKey(Current().Kind);

    private For ParseFor(Token keyword)
    {
        var names = ParseDelimited(() => new Identifier(ExpectIdentifier()));
        var colon = Expect(SyntaxKind.Colon);
        var expression = ParseExpression();
        var body = ParseStatement();
        return new For(keyword, names, colon, expression, body);
    }

    private After ParseAfter(Token keyword)
    {
        var condition = ParseExpression();
        var body = ParseControlFlowBody(keyword);
        return new After(keyword, condition, body);
    }

    private static Break ParseBreak(Token keyword) => new(keyword);
    private static Continue ParseContinue(Token keyword) => new(keyword);

    private While ParseWhile(Token keyword)
    {
        var condition = ParseExpression();
        var body = ParseControlFlowBody(keyword);
        return new While(keyword, condition, body);
    }

    private If ParseIf(Token keyword)
    {
        var condition = ParseExpression();
        var thenBranch = ParseControlFlowBody(keyword);
        var elseBranch = Match(out var elseKeyword, SyntaxKind.ElseKeyword) ? new ElseBranch(elseKeyword, ParseControlFlowBody(keyword)) : null;
        return new If(keyword, condition, thenBranch, elseBranch);
    }

    private Statement ParseControlFlowBody(Token keyword)
    {
        var statement = ParseStatement();
        return AssertDeclarationInsideOfBlock(statement) ? statement : new NullStatement(keyword);
    }
}