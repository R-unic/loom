using Loom.Core.Parsing.AST;
using Loom.Core.Pipeline;
using Loom.Core.Resolving.Symbols;
using Loom.Core.Text;
using Attribute = Loom.Core.Parsing.AST.Attribute;

namespace Loom.LanguageServer;

/// <summary>One function calling another, and every place in the caller that does it.</summary>
public sealed record CallEdge(FunctionSymbol Symbol, IReadOnlyList<Token> CallSites);

/// <summary>
///     Who calls a function, and who it calls. A trait method's body is written in an <c>implement</c> block
///     the trait itself never names, so - like <see cref="ImplementationHandler" /> - this reads off the
///     resolved symbol rather than the syntax under the cursor.
/// </summary>
public static class CallHierarchy
{
    /// <summary>The function-shaped symbol under the cursor, or null when there is none to build a hierarchy from.</summary>
    public static FunctionSymbol? At(CompiledFile file, int offset) => SymbolReferences.At(file, offset) as FunctionSymbol;

    /// <summary>Every named function that calls <paramref name="symbol" />, grouped by which one and where.</summary>
    public static IReadOnlyList<CallEdge> IncomingCalls(FunctionSymbol symbol, CompilationUnit unit, CancellationToken cancellationToken)
    {
        var byCaller = new Dictionary<FunctionSymbol, List<Token>>();
        foreach (var reference in SymbolReferences.Of(symbol, unit, cancellationToken))
        {
            if (reference.IsDeclaration || !unit.AnalyzedModules.TryGetValue(reference.File, out var model))
                continue;

            var node = NodeFinder.FindAt(model.Tree, reference.Name.Span.Position);
            if (node == null || EnclosingNamedFunction(node) is not { } enclosing)
                continue;

            if (model.GetDeclarationSymbol(enclosing) is not FunctionSymbol caller)
                continue;

            Add(byCaller, caller, reference.Name);
        }

        return byCaller.Select(entry => new CallEdge(entry.Key, entry.Value)).ToArray();
    }

    /// <summary>Every named function <paramref name="symbol" /> itself calls, grouped by which one and where.</summary>
    public static IReadOnlyList<CallEdge> OutgoingCalls(FunctionSymbol symbol, CompilationUnit unit)
    {
        if (!unit.AnalyzedModules.TryGetValue(symbol.File, out var model))
            return [];

        var byCallee = new Dictionary<FunctionSymbol, List<Token>>();
        foreach (var invocation in symbol.Declaration.EnumerateDescendants<Invocation>())
        {
            // an attribute is an invocation too, but one nothing ever calls
            if (invocation is Attribute { IsInvoked: false } || NameOf(invocation.Expression) is not { } name)
                continue;

            foreach (var callee in CallSiteFinder.SymbolsOf(invocation.Expression, model).OfType<FunctionSymbol>())
                Add(byCallee, callee, name);
        }

        return byCallee.Select(entry => new CallEdge(entry.Key, entry.Value)).ToArray();
    }

    /// <summary>Whether the declaration sits inside a type rather than at the top level - a trait method or an implementation of one.</summary>
    public static bool IsMethod(Node declaration)
    {
        for (var node = declaration.Parent; node != null; node = node.Parent)
            if (node is InterfaceBody or TraitBody or ImplementBody)
                return true;

        return false;
    }

    private static void Add(Dictionary<FunctionSymbol, List<Token>> byOther, FunctionSymbol other, Token site)
    {
        if (!byOther.TryGetValue(other, out var sites))
            byOther[other] = sites = [];

        sites.Add(site);
    }

    private static Token? NameOf(Expression callee) =>
        callee switch
        {
            Identifier identifier => identifier.Name,
            QualifiedName { Names: [.., var last] } => last.Name,
            PropertyAccess { Names: [.., var last] } => last.Name,
            _ => null
        };

    /// <summary>
    ///     The nearest named function containing the node, stopping at an anonymous one - a function
    ///     expression's caller cannot be determined from its declaration, so neither can the hierarchy
    ///     through it.
    /// </summary>
    private static FunctionDeclaration? EnclosingNamedFunction(Node node)
    {
        for (var current = node.Parent; current != null; current = current.Parent)
            switch (current)
            {
                case FunctionExpression:
                    return null;
                case FunctionDeclaration declaration:
                    return declaration;
            }

        return null;
    }
}
