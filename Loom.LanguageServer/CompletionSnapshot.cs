using Loom.Core.Modules;
using Loom.Core.Parsing.AST;
using Loom.Core.Pipeline;
using Loom.Core.Resolving;
using Loom.Core.Resolving.Symbols;
using Loom.Core.Text;
using Loom.Core.TypeChecking.Types;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using FunctionType = Loom.Core.TypeChecking.Types.FunctionType;
using LoomSymbolKind = Loom.Core.Resolving.Symbols.SymbolKind;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.LanguageServer;

public sealed record MemberScope(TextSpan Range, IReadOnlyList<VisibleSymbol> Members);

/// <summary>A range where only one fixed set of names may be written, whatever else happens to be in scope.</summary>
public sealed record ExclusiveScope(TextSpan Range, IReadOnlyList<VisibleSymbol> Names);

public sealed record CompletionSnapshot
{
    public static readonly CompletionSnapshot Empty = new();

    public IReadOnlyList<VisibleSymbol> Identifiers { get; init; } = [];
    public IReadOnlyList<VisibleSymbol> Attributes { get; init; } = [];
    public IReadOnlyList<VisibleSymbol> ModuleSpecifiers { get; init; } = [];
    public IReadOnlyList<MemberScope> MemberScopes { get; init; } = [];

    /// <summary>Import lists, each offering the exports of the module its own declaration names.</summary>
    public IReadOnlyList<ExclusiveScope> ImportScopes { get; init; } = [];

    /// <summary>Names other modules export that this file has not imported, offered alongside what is in scope.</summary>
    public IReadOnlyList<VisibleSymbol> Importable { get; init; } = [];
    public IReadOnlyList<TextSpan> TypeRanges { get; init; } = [];
    public IReadOnlyList<TextSpan> AttributeRanges { get; init; } = [];
    public IReadOnlyList<TextSpan> ModuleSpecifierRanges { get; init; } = [];

    public IReadOnlyList<VisibleSymbol> At(int offset)
    {
        if (Narrowest(MemberScopes.Select(scope => new ExclusiveScope(scope.Range, scope.Members)), offset) is { } members)
            return members;

        if (Narrowest(ImportScopes, offset) is { } imported)
            return imported;

        if (Contains(AttributeRanges, offset))
            return Attributes;

        if (Contains(ModuleSpecifierRanges, offset))
            return ModuleSpecifiers;

        // keywords are not symbols - the lexer gives them their own token kinds - so no scope will ever
        // produce them, yet they are exactly what is being typed half the time
        var wantsTypes = Contains(TypeRanges, offset);
        return Identifiers
            .Where(symbol => symbol.IsTypeSymbol == wantsTypes && symbol.Scope.Contains(offset))
            .Concat(wantsTypes ? LanguageKeywords.Types : LanguageKeywords.Values)
            .Concat(Importable.Where(symbol => symbol.IsTypeSymbol == wantsTypes))
            .ToArray();
    }

    /// <summary>The symbol a resolve request is asking about, looked up where the request that offered it looked.</summary>
    public VisibleSymbol? Find(int offset, string name) => At(offset).FirstOrDefault(symbol => symbol.Name == name);

    private static IReadOnlyList<VisibleSymbol>? Narrowest(IEnumerable<ExclusiveScope> scopes, int offset)
    {
        ExclusiveScope? narrowest = null;
        foreach (var scope in scopes)
        {
            if (!scope.Range.Contains(offset)) continue;
            if (narrowest == null || scope.Range.Length < narrowest.Range.Length)
                narrowest = scope;
        }

        return narrowest?.Names;
    }

    private static bool Contains(IReadOnlyList<TextSpan> ranges, int offset)
    {
        foreach (var range in ranges)
            if (range.Contains(offset))
                return true;

        return false;
    }
}

public static class CompletionSnapshotBuilder
{
    /// <summary>The one file a snapshot is built for, and the compile it belongs to - what every description of a name is written from the point of view of.</summary>
    private sealed record FileContext(CompiledFile File, CompilationUnit Unit, ModuleResolver Modules)
    {
        public SemanticModel SemanticModel => File.SemanticModel;
        public SourceFile SourceFile => File.SourceFile;
    }

    public static CompletionSnapshot Build(CompiledFile file, CompilationUnit unit, ModuleResolver modules)
    {
        var context = new FileContext(file, unit, modules);
        var sourceFile = file.SourceFile;
        var wholeFile = new TextSpan(0, sourceFile.SourceText.Length);
        var descendants = file.Tree.GetDescendants();

        var declared = file.SemanticModel.Declarations.Values
            .SelectMany(symbols => symbols)
            .Concat(unit.Globals.Of(sourceFile).Keys)
            .GroupBy(symbol => (symbol.Name, symbol.IsTypeSymbol))
            .Select(group => group.OrderByDescending(symbol => IsLocalTo(symbol, sourceFile)).First())
            .ToArray();

        return new CompletionSnapshot
        {
            Identifiers = declared
                .Where(symbol => symbol.Kind is not (LoomSymbolKind.Attribute or LoomSymbolKind.Property))
                .Select(symbol => ToVisibleSymbol(symbol, context, wholeFile))
                .ToArray(),
            Attributes = declared
                .Where(symbol => symbol.Kind is LoomSymbolKind.Attribute or LoomSymbolKind.Function)
                .Select(symbol => ToVisibleSymbol(symbol, context, wholeFile))
                .ToArray(),
            Importable = CollectImportable(declared, context),
            ModuleSpecifiers = CollectModuleSpecifiers(context),
            MemberScopes = CollectMemberScopes(descendants, context),
            ImportScopes = CollectImportScopes(descendants, context),
            TypeRanges = CollectTypeRanges(descendants, sourceFile.SourceText),
            AttributeRanges = descendants.OfType<Attributes>().Select(node => BracketRange(node.LeftBracket, node.RightBracket)).ToArray(),
            ModuleSpecifierRanges = descendants.OfType<ImportDeclaration>().Select(node => node.ModuleSpecifier.Span).ToArray()
        };
    }

    /// <summary>
    ///     Names another module exports that this file cannot yet write. Anything already in scope is left out:
    ///     the name is offered from the scope it is in, and offering it twice would put an import beside a
    ///     candidate that needs none.
    /// </summary>
    private static IReadOnlyList<VisibleSymbol> CollectImportable(IReadOnlyList<Symbol> declared, FileContext context)
    {
        var inScope = declared.Select(symbol => (symbol.Name, symbol.IsTypeSymbol)).ToHashSet();
        var importable = new List<VisibleSymbol>();

        foreach (var candidate in ImportCatalog.For(context.SourceFile, context.Unit, context.Modules))
        {
            var symbol = candidate.Symbol;
            if (!inScope.Add((candidate.Name, symbol.IsTypeSymbol)))
                continue;

            importable.Add(
                new VisibleSymbol(candidate.Name, ToCompletionItemKind(symbol.Kind))
                {
                    IsTypeSymbol = symbol.IsTypeSymbol,
                    ImportedFrom = candidate.Specifier,
                    Detail = () => DeclarationDisplay.CompletionDetail(symbol, TypeOf(candidate.Model, symbol.Declaration)),
                    Documentation = () => SymbolMarkdown.Describe(symbol, TypeOf(candidate.Model, symbol.Declaration), symbol.File, context.Unit, context.Modules)
                }
            );
        }

        return importable;
    }

    private static IReadOnlyList<VisibleSymbol> CollectModuleSpecifiers(FileContext context)
    {
        var (importingFile, unit, resolver) = (context.SourceFile, context.Unit, context.Modules);
        var root = unit.Roots.Of(importingFile);
        var siblings = root.Files
            .Where(module => module != importingFile && !module.IsDeclaration)
            .Select(module => resolver.SpecifierOf(importingFile, module));

        var packages = unit.Roots
            .Where(dependency => dependency != unit.Roots.Entry)
            .Select(dependency => dependency.Package?.Name?.ToString())
            .OfType<string>();

        return siblings.Concat(packages)
            .Where(specifier => !string.IsNullOrEmpty(specifier))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .Select(specifier => new VisibleSymbol(specifier, CompletionItemKind.Module))
            .ToArray();
    }

    /// <summary>
    ///     What each <c>import { … } from "…"</c> list may name: the exports of the module it imports from,
    ///     and nothing else. Without this the braces offer every name already in scope, which is precisely
    ///     the set that cannot be written there.
    /// </summary>
    private static IReadOnlyList<ExclusiveScope> CollectImportScopes(IReadOnlyList<Node> descendants, FileContext context)
    {
        var imports = descendants.OfType<ImportDeclaration>().ToArray();
        if (imports.Length == 0)
            return [];

        var resolver = context.Modules;
        var scopes = new List<ExclusiveScope>();
        foreach (var import in imports)
        {
            if (import.ModulePath is not { Length: > 0 } specifier)
                continue;

            if (resolver.Resolve(context.SourceFile, specifier).File is not { } module
                || !context.Unit.AnalyzedModules.TryGetValue(module, out var moduleModel))
                continue;

            var exports = moduleModel.Exports
                .Where(export => !import.IsTypeOnly || export.Symbol.IsTypeSymbol)
                // an interface exports a type and a value under one name, but an import list names it once
                .GroupBy(export => export.Name)
                .Select(group => ToImportedSymbol(group.First(), moduleModel, context.Unit, resolver))
                .ToArray();

            if (exports.Length > 0)
                scopes.Add(new ExclusiveScope(BracketRange(import.LeftBrace, import.RightBrace), exports));
        }

        return scopes;
    }

    private static VisibleSymbol ToImportedSymbol(ExportBinding export, SemanticModel moduleModel, CompilationUnit unit, ModuleResolver resolver)
    {
        var symbol = export.Symbol;
        return new VisibleSymbol(export.Name, ToCompletionItemKind(symbol.Kind))
        {
            IsTypeSymbol = symbol.IsTypeSymbol,
            Detail = () => DeclarationDisplay.CompletionDetail(symbol, TypeOf(moduleModel, symbol.Declaration)),
            Documentation = () => SymbolMarkdown.Describe(symbol, TypeOf(moduleModel, symbol.Declaration), symbol.File, unit, resolver)
        };
    }

    private static VisibleSymbol ToVisibleSymbol(Symbol symbol, FileContext context, TextSpan wholeFile)
    {
        var isLocal = IsLocalTo(symbol, context.SourceFile);
        return new VisibleSymbol(symbol.Name, ToCompletionItemKind(symbol.Kind))
        {
            IsTypeSymbol = symbol.IsTypeSymbol,
            IsLocal = isLocal,
            Scope = isLocal ? ScopeOf(symbol.Declaration, wholeFile) : wholeFile,
            Detail = () => DeclarationDisplay.CompletionDetail(symbol, TypeOf(context.SemanticModel, symbol.Declaration)),
            Documentation = () => SymbolMarkdown.Describe(symbol, TypeOf(context.SemanticModel, symbol.Declaration), context.SourceFile, context.Unit, context.Modules)
        };
    }

    private static bool IsLocalTo(Symbol symbol, SourceFile file) => FilePaths.Same(symbol.File.AbsolutePath, file.AbsolutePath);

    private static CompletionItemKind ToCompletionItemKind(LoomSymbolKind kind) =>
        kind switch
        {
            LoomSymbolKind.Function => CompletionItemKind.Function,
            LoomSymbolKind.Variable or LoomSymbolKind.Parameter => CompletionItemKind.Variable,
            LoomSymbolKind.Property or LoomSymbolKind.InjectedPropertyVariable => CompletionItemKind.Property,
            LoomSymbolKind.Type => CompletionItemKind.Class,
            LoomSymbolKind.EnumType => CompletionItemKind.Enum,
            LoomSymbolKind.Interface or LoomSymbolKind.Trait => CompletionItemKind.Interface,
            LoomSymbolKind.Event => CompletionItemKind.Event,
            LoomSymbolKind.Attribute => CompletionItemKind.Function,
            _ => CompletionItemKind.Text
        };

    private static TextSpan ScopeOf(Node declaration, TextSpan wholeFile)
    {
        for (var node = declaration.Parent; node != null; node = node.Parent)
            if (node is Block or IFunctionLike or For or MatchArm or Tree)
                return node.Span;

        return wholeFile;
    }

    private static IReadOnlyList<TextSpan> CollectTypeRanges(IReadOnlyList<Node> descendants, string text)
    {
        var ranges = new List<TextSpan>();
        foreach (var node in descendants)
        {
            if (node is TypeExpression)
                ranges.Add(node.Span);

            if (node is ColonTypeClause clause)
                ranges.Add(
                    TextSpan.FromStartEnd(
                        clause.ColonToken.Span.End,
                        TypeSyntaxEnd(text, Math.Max(clause.ColonToken.Span.End, clause.Type.Span.End))
                    )
                );
        }

        return ranges;
    }

    /// <summary>
    ///     Where an annotation stops, counting the type syntax the parser dropped. A half-written generic -
    ///     <c>let a: Array&lt;n</c> - leaves no node past <c>Array</c>, so the annotation's parsed range ends
    ///     before the name being typed and the cursor reads as a value position. Scanning on from the parsed
    ///     end recovers that, and stops at the first character that could not continue a type, so a finished
    ///     annotation keeps its own bounds.
    /// </summary>
    private static int TypeSyntaxEnd(string text, int start)
    {
        var position = start;
        var depth = 0;
        while (position < text.Length)
        {
            var character = text[position];
            if (char.IsLetterOrDigit(character) || character is '_' or '.' or '?' or '[' or ']' or '|' or '&' or ' ' or '\t')
            {
                position++;
                continue;
            }

            switch (character)
            {
                case '<':
                    depth++;
                    break;
                case '>' when depth > 0:
                    depth--;
                    break;
                // a comma separates type arguments inside a list, but parameters or declarations outside one
                case ',' when depth > 0:
                    break;
                default:
                    return position;
            }

            position++;
        }

        return position;
    }

    private static IReadOnlyList<MemberScope> CollectMemberScopes(IReadOnlyList<Node> descendants, FileContext context)
    {
        var scopes = new List<MemberScope>();
        foreach (var node in descendants)
            switch (node)
            {
                case QualifiedName qualifiedName:
                    AddDottedScopes(scopes, context, qualifiedName.Identifier, qualifiedName.Names);
                    break;
                case PropertyAccess propertyAccess:
                    AddDottedScopes(scopes, context, propertyAccess.Expression, propertyAccess.Names);
                    break;
                case ElementAccess { IndexExpression: Literal { Value: string } literal } elementAccess:
                    AddScope(scopes, context, literal.Span, TypeOf(context.SemanticModel, elementAccess.Expression));
                    break;
            }

        return scopes;
    }

    private static void AddDottedScopes(List<MemberScope> scopes, FileContext context, Node receiver, List<DotName> names)
    {
        var type = TypeOf(context.SemanticModel, receiver);
        foreach (var dotName in names)
        {
            AddScope(scopes, context, WrittenNameRange(dotName), type);
            type = TypeMembers.PropertyType(type, dotName.Name.Text);
        }
    }

    private static void AddScope(List<MemberScope> scopes, FileContext context, TextSpan range, Type? receiver)
    {
        var members = TypeMembers.Of(receiver);
        if (members.Count == 0) return;

        scopes.Add(new MemberScope(range, members.Select(property => ToMemberSymbol(property, receiver, context)).ToArray()));
    }

    private static TextSpan WrittenNameRange(DotName dotName)
    {
        var afterDot = dotName.Dot.Span.End;
        return dotName.Name.Text.Length == 0
            ? new TextSpan(afterDot, 0)
            : TextSpan.FromStartEnd(afterDot, dotName.Name.Span.End);
    }

    private static VisibleSymbol ToMemberSymbol(ObjectProperty property, Type? receiver, FileContext context) =>
        new(property.Name, property.ValueType is FunctionType ? CompletionItemKind.Method : CompletionItemKind.Property)
        {
            Detail = () => property.ValueType.ToString(),
            Documentation = () => MemberDocumentation(property, receiver, context)
        };

    /// <summary>
    ///     What a member's own declaration says about it, which only a declared interface has - a structural
    ///     type's property is a name and a type and nothing more.
    /// </summary>
    private static string? MemberDocumentation(ObjectProperty property, Type? receiver, FileContext context)
    {
        if (receiver == null || context.SemanticModel.GetPropertySymbol(receiver, [property.Name]) is not { } symbol)
            return null;

        return SymbolMarkdown.Describe(symbol, property.ValueType, context.SourceFile, context.Unit, context.Modules);
    }

    private static TextSpan BracketRange(Token leftBracket, Token rightBracket) =>
        TextSpan.FromStartEnd(leftBracket.Span.End, rightBracket.Span.Position);

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
