using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Text;

namespace Loom.Core.Parsing;

public sealed partial class Parser
{
    private Statement ParseImport(Token importKeyword)
    {
        if (Match(out var star, SyntaxKind.Star))
            return ParseNamespaceImport(importKeyword, star);

        Match(out var typeKeyword, SyntaxKind.TypeKeyword);

        var leftBrace = Expect(SyntaxKind.LBrace, "'{' after 'import'");
        var specifiers = !IsEof() && Current() is { Kind: SyntaxKind.Identifier }
            ? ParseDelimited(ParseImportSpecifier).OfType<ImportSpecifier>().ToList()
            : [];

        var rightBrace = Expect(SyntaxKind.RBrace);
        if (specifiers.Count == 0)
            _diagnostics.Error(importKeyword, InternalCodes.EmptyImportClause, "Import declaration must name at least one member.");

        var fromKeyword = ExpectContextualKeyword("from");
        var pathToken = Expect(SyntaxKind.StringLiteral, "module path");

        return new ImportDeclaration(
            importKeyword,
            typeKeyword,
            leftBrace,
            specifiers,
            rightBrace,
            fromKeyword,
            new Literal(pathToken, LiteralUtility.ResolveValue(pathToken))
        );
    }

    private Statement ParseNamespaceImport(Token importKeyword, Token star)
    {
        var asKeyword = Expect(SyntaxKind.AsKeyword, "'as' after 'import *'");
        var name = ExpectIdentifier("namespace name");
        var fromKeyword = ExpectContextualKeyword("from");
        var pathToken = Expect(SyntaxKind.StringLiteral, "module path");

        return new NamespaceImport(
            importKeyword,
            star,
            asKeyword,
            name,
            fromKeyword,
            new Literal(pathToken, LiteralUtility.ResolveValue(pathToken))
        );
    }

    private ImportSpecifier? ParseImportSpecifier() =>
        !Match(out var name, SyntaxKind.Identifier)
            ? null
            : Match(out var asKeyword, SyntaxKind.AsKeyword)
                ? new ImportSpecifier(name, asKeyword, ExpectIdentifier("import alias"))
                : new ImportSpecifier(name, null, null);

    private Statement ParseExport(Token exportKeyword)
    {
        if (Current().Kind is SyntaxKind.LBrace || Current().Kind is SyntaxKind.TypeKeyword && PeekKind(1) is SyntaxKind.LBrace)
            return ParseExportList(exportKeyword);

        if (Match(out var keyword, SyntaxFacts.IsExportableKeyword))
            return WrapExport(exportKeyword, StatementParsers[keyword.Kind](keyword));

        _diagnostics.Error(
            Current(),
            InternalCodes.ExpectedExportableDeclaration,
            $"Only 'fn', 'let', 'type', 'interface', 'enum', and 'trait' declarations can be exported, got {SafeTokenText(Current())}."
        );

        return new NullStatement(exportKeyword);
    }

    private ExportList ParseExportList(Token exportKeyword)
    {
        Match(out var typeKeyword, SyntaxKind.TypeKeyword);

        var leftBrace = Expect(SyntaxKind.LBrace);
        var specifiers = !IsEof() && Current() is { Kind: SyntaxKind.Identifier }
            ? ParseDelimited(ParseExportSpecifier).OfType<ExportSpecifier>().ToList()
            : [];

        var rightBrace = Expect(SyntaxKind.RBrace);
        if (specifiers.Count == 0)
            _diagnostics.Error(exportKeyword, InternalCodes.EmptyExportList, "Export list must name at least one member.");

        Token? fromKeyword = null;
        Literal? moduleSpecifier = null;
        if (AtContextualKeyword("from"))
        {
            fromKeyword = Advance();
            var pathToken = Expect(SyntaxKind.StringLiteral, "module path");
            moduleSpecifier = new Literal(pathToken, LiteralUtility.ResolveValue(pathToken));
        }

        return new ExportList(
            exportKeyword,
            typeKeyword,
            leftBrace,
            specifiers,
            rightBrace,
            fromKeyword,
            moduleSpecifier
        );
    }

    private ExportSpecifier? ParseExportSpecifier() =>
        !Match(out var name, SyntaxKind.Identifier)
            ? null
            : Match(out var asKeyword, SyntaxKind.AsKeyword)
                ? new ExportSpecifier(name, asKeyword, ExpectIdentifier("export alias"))
                : new ExportSpecifier(name, null, null);

    private static Statement WrapExport(Token exportKeyword, Statement declaration) =>
        declaration is NamedDeclaration named
            ? new ExportDeclaration(exportKeyword, named)
            : declaration;
}
