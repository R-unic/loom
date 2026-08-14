using Loom.Core.Parsing.AST;
using Loom.Core.Pipeline;
using Loom.Core.Resolving;
using Loom.Core.Resolving.Symbols;
using Loom.Core.Text;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using AstAttribute = Loom.Core.Parsing.AST.Attribute;
using AstTypeParameter = Loom.Core.Parsing.AST.TypeParameter;
using FunctionType = Loom.Core.TypeChecking.Types.FunctionType;
using LoomSymbolKind = Loom.Core.Resolving.Symbols.SymbolKind;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.LanguageServer;

/// <summary>One token of the file and what the compiler knows it to be.</summary>
public sealed record ClassifiedToken(Token Token, SemanticTokenType Type, IReadOnlyList<SemanticTokenModifier> Modifiers);

/// <summary>
///     What each token of a file is, for the client's highlighting. The lexer alone answers most of it - a
///     keyword is a keyword wherever it appears - but not the part that matters: an identifier is a type, a
///     function, a parameter or a property depending on what it resolved to, and a regular-expression grammar
///     cannot tell those apart in a language where <c>T</c> in <c>fn f&lt;T&gt;(x: T)</c> is neither a value
///     nor a name the file declares in the ordinary way.
/// </summary>
/// <remarks>
///     Every token is classified rather than only the interesting ones. A client whose grammar already colours
///     the file merges these over it and loses nothing; a client with no grammar for Loom at all - which is
///     every client until one ships - would otherwise be handed a file of plain text with the identifiers
///     coloured in.
/// </remarks>
public static class SemanticTokenClassifier
{
    /// <summary>
    ///     The types and modifiers this server will ever send, in the order their indices refer to. The client
    ///     is told this once, at registration, and every token afterwards is a pair of offsets into it.
    /// </summary>
    public static readonly SemanticTokensLegend Legend = new()
    {
        TokenTypes = new Container<SemanticTokenType>(
            SemanticTokenType.Namespace,
            SemanticTokenType.Type,
            SemanticTokenType.Interface,
            SemanticTokenType.Enum,
            SemanticTokenType.EnumMember,
            SemanticTokenType.TypeParameter,
            SemanticTokenType.Parameter,
            SemanticTokenType.Variable,
            SemanticTokenType.Property,
            SemanticTokenType.Event,
            SemanticTokenType.Function,
            SemanticTokenType.Method,
            SemanticTokenType.Decorator,
            SemanticTokenType.Keyword,
            SemanticTokenType.Comment,
            SemanticTokenType.String,
            SemanticTokenType.Number,
            SemanticTokenType.Operator
        ),
        TokenModifiers = new Container<SemanticTokenModifier>(
            SemanticTokenModifier.Declaration,
            SemanticTokenModifier.Readonly,
            SemanticTokenModifier.Async,
            SemanticTokenModifier.Deprecated,
            SemanticTokenModifier.Documentation,
            SemanticTokenModifier.DefaultLibrary
        )
    };

    private static readonly IReadOnlyList<SemanticTokenModifier> _none = [];
    private static readonly IReadOnlyList<SemanticTokenModifier> _declaration = [SemanticTokenModifier.Declaration];
    private static readonly IReadOnlyList<SemanticTokenModifier> _documentation = [SemanticTokenModifier.Documentation];

    /// <summary>
    ///     Punctuation that separates rather than computes. Nothing is sent for these: they carry no meaning a
    ///     client could colour differently, and an editor's own bracket matching already reads them.
    /// </summary>
    private static readonly HashSet<SyntaxKind> _delimiters =
    [
        SyntaxKind.LParen, SyntaxKind.RParen, SyntaxKind.LBracket, SyntaxKind.RBracket, SyntaxKind.LBrace, SyntaxKind.RBrace,
        SyntaxKind.Comma, SyntaxKind.Semicolon, SyntaxKind.Dot, SyntaxKind.QuestionDot, SyntaxKind.At, SyntaxKind.Whitespace, SyntaxKind.Eof
    ];

    public static IReadOnlyList<ClassifiedToken> Of(CompiledFile file)
    {
        var names = NameClassifications(file);
        var classified = new List<ClassifiedToken>(file.TokensWithTrivia.Count);
        foreach (var token in file.TokensWithTrivia)
        {
            if (_delimiters.Contains(token.Kind) || token.Span.Length == 0)
                continue;

            if (names.TryGetValue(token.Span.Position, out var name))
            {
                classified.Add(new ClassifiedToken(token, name.Type, name.Modifiers));
                continue;
            }

            if (FromSyntax(token.Kind) is { } type)
                classified.Add(new ClassifiedToken(token, type, token.Kind == SyntaxKind.DocComment ? _documentation : _none));
        }

        return classified;
    }

    /// <summary>
    ///     What the lexer alone can say about a token. An identifier that reached here resolved to nothing -
    ///     it is misspelled, or its declaration failed to compile - and is coloured as a plain variable rather
    ///     than left blank, so a file mid-edit does not flicker between highlighted and not.
    /// </summary>
    private static SemanticTokenType? FromSyntax(SyntaxKind kind)
    {
        if (kind is SyntaxKind.Comment or SyntaxKind.BlockComment or SyntaxKind.DocComment)
            return SemanticTokenType.Comment;

        if (kind is SyntaxKind.StringLiteral or SyntaxKind.InterpolatedStringStart or SyntaxKind.InterpolatedStringText or SyntaxKind.InterpolatedStringEnd)
            return SemanticTokenType.String;

        if (kind == SyntaxKind.NumberLiteral)
            return SemanticTokenType.Number;

        if (kind is SyntaxKind.TrueLiteral or SyntaxKind.FalseLiteral or SyntaxKind.NoneLiteral)
            return SemanticTokenType.Keyword;

        if (SyntaxFacts.GetKeywordText(kind) != null)
            return SemanticTokenType.Keyword;

        if (kind == SyntaxKind.Identifier)
            return SemanticTokenType.Variable;

        return SemanticTokenType.Operator;
    }

    /// <summary>
    ///     Every name in the file that the compiler resolved, keyed by where its token starts. Walking the
    ///     tree once and indexing by position is what lets the token list be classified in one pass: the
    ///     tokens are already in source order, and asking the tree about each one separately would search it
    ///     from the root as many times as the file has names.
    /// </summary>
    private static Dictionary<int, (SemanticTokenType Type, IReadOnlyList<SemanticTokenModifier> Modifiers)> NameClassifications(CompiledFile file)
    {
        var semanticModel = file.SemanticModel;
        var classifications = new Dictionary<int, (SemanticTokenType, IReadOnlyList<SemanticTokenModifier>)>();

        foreach (var node in file.Tree.EnumerateDescendants().Prepend<Node>(file.Tree))
            switch (node)
            {
                // an attribute is written as an invocation, so its name would otherwise classify as whatever
                // the declaration behind it is - a function, most of the time
                case AstAttribute attribute:
                    Record(classifications, attribute.Expression.LastToken(), SemanticTokenType.Decorator, _none);
                    break;
                case EnumMember member:
                    Record(classifications, member.Name, SemanticTokenType.EnumMember, _declaration);
                    break;
                case NamedDeclaration declaration:
                    Record(classifications, declaration.Name, Best(semanticModel.GetDeclarationSymbols(declaration)), isDeclaration: true);
                    break;
                case Identifier identifier:
                    Record(classifications, identifier.Name, Best(semanticModel.References.GetValueOrDefault(identifier.Id, [])), isDeclaration: false);
                    break;
                case TypeName typeName:
                    Record(classifications, typeName.Name, Best(semanticModel.References.GetValueOrDefault(typeName.Id, [])), isDeclaration: false);
                    break;
                case ImportSpecifier specifier:
                    RecordSpecifier(
                        classifications,
                        semanticModel.ImportBindings.Find(binding => binding.Specifier == specifier)?.Symbol,
                        specifier.Name,
                        specifier.Alias
                    );

                    break;
                case ExportSpecifier specifier:
                    RecordSpecifier(
                        classifications,
                        semanticModel.Exports.Find(export => export.SourceName == specifier.Name.Text)?.Symbol,
                        specifier.Name,
                        specifier.Alias
                    );

                    break;
                case QualifiedName qualified:
                    RecordMembers(classifications, semanticModel, qualified.Identifier, qualified.Names);
                    break;
                case PropertyAccess access:
                    RecordMembers(classifications, semanticModel, access.Expression, access.Names);
                    break;
            }

        return classifications;
    }

    /// <summary>
    ///     Which of the symbols one name stands for decides its colour. An interface or an enum declares a
    ///     type and a value under the same name, and it is the type half a reader is looking at - the value
    ///     half is what makes <c>new Packet { … }</c> resolve, not something the name is ever written as.
    /// </summary>
    private static Symbol? Best(IReadOnlyList<Symbol> symbols)
    {
        foreach (var symbol in symbols)
            if (symbol.IsTypeSymbol)
                return symbol;

        return symbols.Count > 0 ? symbols[0] : null;
    }

    /// <summary>
    ///     A specifier names a symbol another module declared; the alias beside it is this file's own name for
    ///     it, and stands for the same thing.
    /// </summary>
    private static void RecordSpecifier(
        Dictionary<int, (SemanticTokenType, IReadOnlyList<SemanticTokenModifier>)> classifications,
        Symbol? symbol,
        Token name,
        Token? alias)
    {
        Record(classifications, name, symbol, isDeclaration: false);
        Record(classifications, alias, symbol, isDeclaration: true);
    }

    /// <summary>
    ///     The links of a dotted name. A member name is a token of its access expression rather than a node of
    ///     its own, so the receiver's type is walked forward one link at a time - the same walk the completion
    ///     snapshot makes, and for the same reason.
    /// </summary>
    private static void RecordMembers(
        Dictionary<int, (SemanticTokenType, IReadOnlyList<SemanticTokenModifier>)> classifications,
        SemanticModel semanticModel,
        Node receiver,
        List<DotName> names)
    {
        var type = TypeOf(semanticModel, receiver);
        foreach (var dotName in names)
        {
            var memberType = TypeMembers.PropertyType(type, dotName.Name.Text);
            Record(
                classifications,
                dotName.Name,
                memberType is FunctionType ? SemanticTokenType.Method : SemanticTokenType.Property,
                _none
            );

            type = memberType;
        }
    }

    private static void Record(
        Dictionary<int, (SemanticTokenType, IReadOnlyList<SemanticTokenModifier>)> classifications,
        Token? name,
        Symbol? symbol,
        bool isDeclaration)
    {
        if (symbol == null)
            return;

        Record(classifications, name, TypeOf(symbol), ModifiersOf(symbol, isDeclaration));
    }

    private static void Record(
        Dictionary<int, (SemanticTokenType, IReadOnlyList<SemanticTokenModifier>)> classifications,
        Token? name,
        SemanticTokenType type,
        IReadOnlyList<SemanticTokenModifier> modifiers)
    {
        // a declaration is visited before the names under it, and a name resolved from the tree beats one
        // guessed from a receiver's type, so the first answer for a position is the one kept
        if (name is { Span.Length: > 0 })
            classifications.TryAdd(name.Span.Position, (type, modifiers));
    }

    private static SemanticTokenType TypeOf(Symbol symbol)
    {
        // a type parameter is declared as an ordinary type, but it stands for whatever a use site passes -
        // which is the distinction a reader wants drawn against the concrete types beside it
        if (symbol.Declaration is AstTypeParameter)
            return SemanticTokenType.TypeParameter;

        return symbol.Kind switch
        {
            LoomSymbolKind.Function => CallHierarchy.IsMethod(symbol.Declaration) ? SemanticTokenType.Method : SemanticTokenType.Function,
            LoomSymbolKind.Parameter => SemanticTokenType.Parameter,
            LoomSymbolKind.Property or LoomSymbolKind.InjectedPropertyVariable => SemanticTokenType.Property,
            LoomSymbolKind.Attribute => SemanticTokenType.Decorator,
            LoomSymbolKind.Type => SemanticTokenType.Type,
            LoomSymbolKind.EnumType => SemanticTokenType.Enum,
            LoomSymbolKind.Interface or LoomSymbolKind.Trait => SemanticTokenType.Interface,
            LoomSymbolKind.Event => SemanticTokenType.Event,
            _ => SemanticTokenType.Variable
        };
    }

    private static IReadOnlyList<SemanticTokenModifier> ModifiersOf(Symbol symbol, bool isDeclaration)
    {
        var modifiers = new List<SemanticTokenModifier>(3);
        if (isDeclaration)
            modifiers.Add(SemanticTokenModifier.Declaration);

        // immutability is the default in Loom, so this marks the ordinary case rather than the exception -
        // which is the point: what a reader needs picked out is the binding that can be written through
        if (symbol is { IsMutable: false, Kind: LoomSymbolKind.Variable or LoomSymbolKind.Parameter or LoomSymbolKind.Property })
            modifiers.Add(SemanticTokenModifier.Readonly);

        if (symbol.Declaration is IFunctionLike { AsyncKeyword: not null })
            modifiers.Add(SemanticTokenModifier.Async);

        if (DeclarationDisplay.DeprecationOf(symbol) != null)
            modifiers.Add(SemanticTokenModifier.Deprecated);

        if (symbol.IsIntrinsic)
            modifiers.Add(SemanticTokenModifier.DefaultLibrary);

        return modifiers.Count == 0 ? _none : modifiers;
    }

    private static Type? TypeOf(SemanticModel semanticModel, Node node)
    {
        try
        {
            return semanticModel.GetType(node);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
