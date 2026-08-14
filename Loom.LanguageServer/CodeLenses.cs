using Loom.Core.Parsing.AST;
using Loom.Core.Pipeline;
using Loom.Core.Resolving;
using Loom.Core.Resolving.Symbols;
using Loom.Core.Text;

namespace Loom.LanguageServer;

/// <summary>A declaration worth annotating, and the symbols its name stands for.</summary>
public sealed record CodeLensTarget(Token Name, IReadOnlyList<Symbol> Symbols)
{
    /// <summary>Whether the declaration is a trait, which is the one kind that has implementations to count.</summary>
    public bool IsTrait => Symbols.Any(symbol => symbol is TraitSymbol);
}

/// <summary>
///     Which declarations get a lens above them, and how many places refer to each. Top-level declarations
///     only: a lens on every property of every interface annotates more lines than it leaves alone, and the
///     line above a member is where the member's own documentation goes.
/// </summary>
public static class CodeLenses
{
    public static IReadOnlyList<CodeLensTarget> In(CompiledFile file)
    {
        var semanticModel = file.SemanticModel;
        var targets = new List<CodeLensTarget>();
        foreach (var statement in file.Tree.Statements)
        {
            if (Declared(statement) is not { } declaration)
                continue;

            var symbols = semanticModel.GetDeclarationSymbols(declaration);
            if (symbols.Count > 0)
                targets.Add(new CodeLensTarget(declaration.Name, symbols));
        }

        return targets;
    }

    /// <summary>The named declaration a statement carries, looking through the keywords that only wrap one.</summary>
    private static NamedDeclaration? Declared(Statement statement) =>
        statement switch
        {
            ExportDeclaration export => Declared(export.Declaration),
            Declare declare => Declared(declare.Signature),
            NamedDeclaration declaration => declaration,
            _ => null
        };

    /// <summary>
    ///     Every place the declaration is referred to, its own name left out. A name can stand for more than
    ///     one symbol - an interface declares a type and a value - and both are the same declaration to a
    ///     reader, so the counts are unioned rather than reported separately.
    /// </summary>
    public static int ReferenceCount(CodeLensTarget target, CompilationUnit unit, CancellationToken cancellationToken)
    {
        var seen = new HashSet<(string File, int Position)>();
        foreach (var symbol in target.Symbols)
            foreach (var reference in SymbolReferences.Of(symbol, unit, cancellationToken))
                if (!reference.IsDeclaration)
                    seen.Add((reference.File.AbsolutePath, reference.Name.Span.Position));

        return seen.Count;
    }

    /// <summary>How many <c>implement</c> blocks in the unit name this trait, wherever they were written.</summary>
    public static int ImplementationCount(CodeLensTarget target, CompilationUnit unit)
    {
        var trait = target.Symbols.OfType<TraitSymbol>().FirstOrDefault();
        if (trait == null)
            return 0;

        var count = 0;
        foreach (var model in unit.AnalyzedModules.Values)
            foreach (var block in model.Tree.EnumerateDescendants<Implement>())
                if (model.GetSymbol(block.TraitName) == trait)
                    count++;

        return count;
    }

    /// <summary>The lens text, singular where the count is one - a lens is read at a glance and "1 references" snags.</summary>
    public static string Describe(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}
