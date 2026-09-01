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

    private NamespaceImport ParseNamespaceImport(Token importKeyword, Token star)
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

    /// <summary>
    ///     Routes an exported declaration to its attribute-aware parser, so <c>[serializable] export
    ///     interface</c> attaches the attributes to the interface the same way an unexported one does.
    ///     Kinds that carry no attributes report rather than dropping them silently.
    /// </summary>
    private Statement ParseExportedDeclaration(Token keyword, Attributes? attributes)
    {
        if (attributes == null)
            return StatementParsers[keyword.Kind](keyword);

        switch (keyword.Kind)
        {
            case SyntaxKind.FnKeyword:
                return ParseFunctionDeclaration(keyword, attributes);
            case SyntaxKind.AsyncKeyword:
                return ParseAsyncFunctionDeclaration(keyword, attributes);
            case SyntaxKind.InterfaceKeyword or SyntaxKind.SealedKeyword:
                return ParseInterfaceDeclaration(keyword, attributes);
            case SyntaxKind.EventKeyword:
                return ParseEventDeclaration(keyword, attributes);
            case SyntaxKind.DeclareKeyword:
                return ParseDeclare(keyword, attributes);
            default:
                _diagnostics.Error(
                    keyword,
                    InternalCodes.AttributesNotSupportedOnDeclaration,
                    $"Attributes are not supported on '{keyword.Text}' declarations."
                );

                return StatementParsers[keyword.Kind](keyword);
        }
    }

    private Statement ParseExport(Token exportKeyword) => ParseExport(exportKeyword, null);

    private Statement ParseExport(Token exportKeyword, Attributes? attributes)
    {
        if (Current().Kind is SyntaxKind.LBrace || Current().Kind is SyntaxKind.TypeKeyword && PeekKind(1) is SyntaxKind.LBrace)
            return ParseExportList(exportKeyword);

        if (Current().Kind is SyntaxKind.Star || Current().Kind is SyntaxKind.TypeKeyword && PeekKind(1) is SyntaxKind.Star)
            return ParseExportAll(exportKeyword);

        if (Match(out var keyword, SyntaxFacts.IsExportableKeyword))
            return WrapExport(exportKeyword, ParseExportedDeclaration(keyword, attributes));

        var verb = exportKeyword.Kind == SyntaxKind.InternalKeyword ? "marked internal" : "exported";
        _diagnostics.Error(
            Current(),
            InternalCodes.ExpectedExportableDeclaration,
            $"Only 'fn', 'let', 'type', 'interface', 'enum', 'trait', 'event', and 'declare' declarations can be {verb}, got {SafeTokenText(Current())}."
        );

        return new NullStatement(exportKeyword);
    }

    /// <remarks>
    ///     A star export always names a module: there is nothing local for it to stand for, so unlike an
    ///     export list the 'from' clause is not optional.
    /// </remarks>
    private ExportAll ParseExportAll(Token exportKeyword)
    {
        Match(out var typeKeyword, SyntaxKind.TypeKeyword);

        var star = Expect(SyntaxKind.Star);
        var fromKeyword = ExpectContextualKeyword("from");
        var pathToken = Expect(SyntaxKind.StringLiteral, "module path");

        return new ExportAll(
            exportKeyword,
            typeKeyword,
            star,
            fromKeyword,
            new Literal(pathToken, LiteralUtility.ResolveValue(pathToken))
        );
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
        declaration is NamedDeclaration or Declare
            ? new ExportDeclaration(exportKeyword, declaration)
            : declaration;
}
