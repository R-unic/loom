using Loom.Core.Modules;
using Loom.Core.Parsing.AST;
using Loom.Core.Pipeline;
using Loom.Core.Resolving;
using Loom.Core.Resolving.Symbols;
using Loom.Core.Text;
using Loom.Core.TypeChecking.Types;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using LoomSymbolKind = Loom.Core.Resolving.Symbols.SymbolKind;
using FunctionType = Loom.Core.TypeChecking.Types.FunctionType;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.LanguageServer;

public sealed record MemberScope(TextSpan Range, IReadOnlyList<VisibleSymbol> Members);

public sealed record CompletionSnapshot
{
    public static readonly CompletionSnapshot Empty = new();

    public IReadOnlyList<VisibleSymbol> Identifiers { get; init; } = [];
    public IReadOnlyList<VisibleSymbol> Attributes { get; init; } = [];
    public IReadOnlyList<VisibleSymbol> ModuleSpecifiers { get; init; } = [];
    public IReadOnlyList<MemberScope> MemberScopes { get; init; } = [];
    public IReadOnlyList<TextSpan> TypeRanges { get; init; } = [];
    public IReadOnlyList<TextSpan> AttributeRanges { get; init; } = [];
    public IReadOnlyList<TextSpan> ModuleSpecifierRanges { get; init; } = [];

    public IReadOnlyList<VisibleSymbol> At(int offset)
    {
        if (NarrowestMemberScope(offset) is { } members)
            return members;

        if (Contains(AttributeRanges, offset))
            return Attributes;

        if (Contains(ModuleSpecifierRanges, offset))
            return ModuleSpecifiers;

        var wantsTypes = Contains(TypeRanges, offset);
        return Identifiers.Where(symbol => symbol.IsTypeSymbol == wantsTypes && symbol.Scope.Contains(offset)).ToArray();
    }

    private IReadOnlyList<VisibleSymbol>? NarrowestMemberScope(int offset)
    {
        MemberScope? narrowest = null;
        foreach (var scope in MemberScopes)
        {
            if (!scope.Range.Contains(offset)) continue;
            if (narrowest == null || scope.Range.Length < narrowest.Range.Length)
                narrowest = scope;
        }

        return narrowest?.Members;
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
    public static CompletionSnapshot Build(CompiledFile file, CompilationUnit unit)
    {
        var semanticModel = file.SemanticModel;
        var sourceFile = file.SourceFile;
        var wholeFile = new TextSpan(0, sourceFile.SourceText.Length);
        var descendants = file.Tree.GetDescendants();

        var declared = semanticModel.Declarations.Values
            .SelectMany(symbols => symbols)
            .Concat(unit.Globals.Of(sourceFile).Keys)
            .GroupBy(symbol => (symbol.Name, symbol.IsTypeSymbol))
            .Select(group => group.OrderByDescending(symbol => IsLocalTo(symbol, sourceFile)).First())
            .ToArray();

        return new CompletionSnapshot
        {
            Identifiers = declared
                .Where(symbol => symbol.Kind is not (LoomSymbolKind.Attribute or LoomSymbolKind.Property))
                .Select(symbol => ToVisibleSymbol(symbol, semanticModel, sourceFile, wholeFile))
                .ToArray(),
            Attributes = declared
                .Where(symbol => symbol.Kind is LoomSymbolKind.Attribute or LoomSymbolKind.Function)
                .Select(symbol => ToVisibleSymbol(symbol, semanticModel, sourceFile, wholeFile))
                .ToArray(),
            ModuleSpecifiers = CollectModuleSpecifiers(sourceFile, unit),
            MemberScopes = CollectMemberScopes(descendants, semanticModel),
            TypeRanges = CollectTypeRanges(descendants),
            AttributeRanges = descendants.OfType<Attributes>().Select(node => BracketRange(node.LeftBracket, node.RightBracket)).ToArray(),
            ModuleSpecifierRanges = descendants.OfType<ImportDeclaration>().Select(node => node.ModuleSpecifier.Span).ToArray()
        };
    }

    private static IReadOnlyList<VisibleSymbol> CollectModuleSpecifiers(SourceFile importingFile, CompilationUnit unit)
    {
        var resolver = new ModuleResolver(unit.SourceFiles, unit.Roots);
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
            .Select(specifier => new VisibleSymbol(specifier, CompletionItemKind.Module, ""))
            .ToArray();
    }

    private static VisibleSymbol ToVisibleSymbol(Symbol symbol, SemanticModel semanticModel, SourceFile file, TextSpan wholeFile)
    {
        var isLocal = IsLocalTo(symbol, file);
        return new VisibleSymbol(symbol.Name, ToCompletionItemKind(symbol.Kind), Describe(semanticModel, symbol.Declaration))
        {
            IsTypeSymbol = symbol.IsTypeSymbol,
            IsLocal = isLocal,
            Scope = isLocal ? ScopeOf(symbol.Declaration, wholeFile) : wholeFile
        };
    }

    private static bool IsLocalTo(Symbol symbol, SourceFile file) => PathComparer.Equals(symbol.File.AbsolutePath, file.AbsolutePath);

    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

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

    private static IReadOnlyList<TextSpan> CollectTypeRanges(IReadOnlyList<Node> descendants)
    {
        var ranges = new List<TextSpan>();
        foreach (var node in descendants)
        {
            if (node is TypeExpression)
                ranges.Add(node.Span);

            if (node is ColonTypeClause clause)
                ranges.Add(TextSpan.FromStartEnd(clause.ColonToken.Span.End, Math.Max(clause.ColonToken.Span.End, clause.Type.Span.End)));
        }

        return ranges;
    }

    private static IReadOnlyList<MemberScope> CollectMemberScopes(IReadOnlyList<Node> descendants, SemanticModel semanticModel)
    {
        var scopes = new List<MemberScope>();
        foreach (var node in descendants)
            switch (node)
            {
                case QualifiedName qualifiedName:
                    AddDottedScopes(scopes, semanticModel, qualifiedName.Identifier, qualifiedName.Names);
                    break;
                case PropertyAccess propertyAccess:
                    AddDottedScopes(scopes, semanticModel, propertyAccess.Expression, propertyAccess.Names);
                    break;
                case ElementAccess { IndexExpression: Literal { Value: string } literal } elementAccess:
                    AddScope(scopes, literal.Span, TypeOf(semanticModel, elementAccess.Expression));
                    break;
            }

        return scopes;
    }

    private static void AddDottedScopes(List<MemberScope> scopes, SemanticModel semanticModel, Node receiver, List<DotName> names)
    {
        var type = TypeOf(semanticModel, receiver);
        foreach (var dotName in names)
        {
            AddScope(scopes, WrittenNameRange(dotName), type);
            type = TypeMembers.PropertyType(type, dotName.Name.Text);
        }
    }

    private static void AddScope(List<MemberScope> scopes, TextSpan range, Type? type)
    {
        var members = TypeMembers.Of(type);
        if (members.Count == 0) return;

        scopes.Add(new MemberScope(range, members.Select(ToMemberSymbol).ToArray()));
    }

    private static TextSpan WrittenNameRange(DotName dotName)
    {
        var afterDot = dotName.Dot.Span.End;
        return dotName.Name.Text.Length == 0
            ? new TextSpan(afterDot, 0)
            : TextSpan.FromStartEnd(afterDot, dotName.Name.Span.End);
    }

    private static VisibleSymbol ToMemberSymbol(ObjectProperty property) =>
        new(
            property.Name,
            property.ValueType is FunctionType ? CompletionItemKind.Method : CompletionItemKind.Property,
            property.ValueType.ToString()
        );

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

    private static string Describe(SemanticModel semanticModel, Node declaration)
    {
        try
        {
            return semanticModel.GetType(declaration).ToString();
        }
        catch (Exception)
        {
            return "";
        }
    }
}
