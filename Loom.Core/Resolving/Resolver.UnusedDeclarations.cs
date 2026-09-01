using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving.Symbols;

namespace Loom.Core.Resolving;

public sealed partial class Resolver
{
    /// <remarks>
    ///     Runs once the whole tree is resolved, mirroring <see cref="ReportUnusedImports" /> - a name may be
    ///     read below its declaration. Each kind is its own pass so one can be dropped or extended without
    ///     touching the others.
    /// </remarks>
    private void ReportUnusedDeclarations()
    {
        var used = _allReferences.Values.SelectMany(symbols => symbols).ToHashSet();
        var declared = _allDeclarations.Values.SelectMany(symbols => symbols).ToList();
        ReportUnusedVariables(declared, used);
        ReportUnusedParameters(declared, used);
        ReportUnusedTypeParameters(declared, used);
        ReportUnusedTraits(declared);
    }

    /// <summary>
    ///     A module-scope <c>let</c> is not a local binding - it is one statement away from being exported,
    ///     and the language server aside, nothing here can tell "nobody reads this" from "this is the file's
    ///     own top-level state" the way it can inside a function body. Only a <c>let</c> nested in a block is
    ///     checked.
    /// </summary>
    private void ReportUnusedVariables(IEnumerable<Symbol> declared, HashSet<Symbol> used)
    {
        foreach (var symbol in declared)
        {
            if (symbol is not VariableSymbol { Declaration: VariableDeclaration { Parent: not (Tree or ExportDeclaration) } variableDeclaration }
                || used.Contains(symbol) || IsIgnoredName(symbol.Name))
                continue;

            _diagnostics.Warn(variableDeclaration.Name, InternalCodes.UnusedVariable, $"'{symbol.Name}' is never used.");
        }
    }

    /// <summary>
    ///     A signature-only declaration - an interface or trait method, a <c>declare fn</c> - has no body for
    ///     a parameter to go unused in, so only a <see cref="FunctionDeclaration" />'s or
    ///     <see cref="FunctionExpression" />'s own parameters are checked.
    /// </summary>
    private void ReportUnusedParameters(IEnumerable<Symbol> declared, HashSet<Symbol> used)
    {
        foreach (var symbol in declared)
        {
            if (symbol is not ParameterSymbol { Declaration: Parameter { Parent.Parent: FunctionDeclaration or FunctionExpression } parameter }
                || used.Contains(symbol) || IsIgnoredName(symbol.Name))
                continue;

            _diagnostics.Warn(parameter.Name, InternalCodes.UnusedParameter, $"'{symbol.Name}' is never used.");
        }
    }

    private void ReportUnusedTypeParameters(IEnumerable<Symbol> declared, HashSet<Symbol> used)
    {
        foreach (var symbol in declared)
        {
            if (symbol is not TypeAliasSymbol { Declaration: TypeParameter typeParameter } || used.Contains(symbol) || IsIgnoredName(symbol.Name))
                continue;

            _diagnostics.Warn(typeParameter.Name, InternalCodes.UnusedTypeParameter, $"'{symbol.Name}' is never used.");
        }
    }

    /// <remarks>
    ///     Only a trait this file never exports is checked: one it does export could still be implemented by
    ///     a file resolved later in the same build, whose <c>implement</c> block adds to
    ///     <see cref="TraitSymbol.ImplementedBy" /> only once that file's own resolution reaches it - after
    ///     this pass would already have run. A private trait cannot be implemented anywhere but here, so
    ///     nothing later can prove this pass wrong.
    /// </remarks>
    private void ReportUnusedTraits(IEnumerable<Symbol> declared)
    {
        foreach (var symbol in declared)
        {
            if (symbol is not TraitSymbol { ImplementedBy.Count: 0 } traitSymbol || IsIgnoredName(traitSymbol.Name) || IsExported(traitSymbol))
                continue;

            _diagnostics.Warn(traitSymbol.Declaration, InternalCodes.UnusedTrait, $"'{traitSymbol.Name}' is never implemented.");
        }
    }

    /// <summary>The escape hatch for "intentionally unused", the same convention as every other language with this warning.</summary>
    private static bool IsIgnoredName(string name) => name.StartsWith('_');

    private bool IsExported(Symbol symbol) => _semanticModel.Exports.Any(export => export.Symbol == symbol);
}
