using System.Diagnostics.CodeAnalysis;
using Loom.Core.Diagnostics;
using Loom.Core.Parsing;
using Loom.Core.Parsing.AST;
using Loom.Core.Pipeline;
using Loom.Core.Resolving.Symbols;
using Loom.Core.TypeChecking;

namespace Loom.Core.Resolving;

public sealed partial class Resolver(ParserResult parserResult, CompilationUnit compilationUnit)
    : Visitor<bool>(_ => true)
{
    private readonly SymbolTable _allDeclarations = [];
    private readonly SymbolTable _allReferences = [];
    private readonly DiagnosticBag _diagnostics = new(options: compilationUnit.DiagnosticOptionsFor(parserResult.Tree.File));
    private readonly HashSet<Node> _resolvedImports = [];
    private readonly Stack<ResolverScope> _scopes = [];
    private ResolverScope? _moduleScope;
    private ResolverContext _context = ResolverContext.None;
    private SemanticModel _semanticModel = null!;

    [MemberNotNull(nameof(_semanticModel))]
    public SemanticModel Resolve()
    {
        _semanticModel = new SemanticModel(parserResult.Tree, _diagnostics, _allDeclarations, _allReferences);

        // ambient names live in a scope of their own so that a module declaring 'Vector3' shadows the
        // intrinsic rather than colliding with it — the file's own declarations are the ones it can see
        PushScope();
        DeclareIntrinsicSymbols();
        DeclareGlobalSymbols();

        _moduleScope = PushScope();
        VisitTree(parserResult.Tree);
        ReportUnusedImports();
        PopScope();
        PopScope();

        return _semanticModel;
    }

    protected override bool Visit(Node node) => node.Accept(this);

    /// <remarks>
    ///     Imports are resolved before anything else in the file, so a name can be used above the import that
    ///     brings it in — the generator emits every require at the top of the output regardless, so reading
    ///     them in source order would reject code the output supports. An import that names no module is left
    ///     for its turn in source order: what it binds is only a stand-in, which must not take a name the file
    ///     goes on to declare for itself.
    /// </remarks>
    public override bool VisitTree(Tree tree)
    {
        foreach (var statement in tree.Statements)
            if (statement is ImportDeclaration or NamespaceImport && TryGetModule(statement, out _, out _))
                Visit(statement);

        return ResolveStatements(tree.Statements);
    }

    public override bool VisitBlock(Block block)
    {
        PushScope();
        var result = ResolveStatements(block.Statements);
        PopScope();

        return result;
    }

    private bool ResolveStatements(List<Statement> statements)
    {
        HoistDeclarations(statements);
        return statements.All(ResolveStatement);
    }

    private void HoistDeclarations(List<Statement> statements)
    {
        foreach (var statement in statements)
            switch (statement)
            {
                case TypeAlias typeAlias:
                    DeclareType(typeAlias);
                    break;
                case TraitDeclaration traitDeclaration:
                    DeclareTrait(traitDeclaration);
                    break;
                case InterfaceDeclaration interfaceDeclaration:
                    if (DeclareVariable(interfaceDeclaration, SymbolKind.Variable))
                        DeclareInterface(interfaceDeclaration, interfaceDeclaration.SealedKeyword != null);

                    break;
                case EnumDeclaration enumDeclaration:
                    if (DeclareVariable(enumDeclaration, SymbolKind.Variable))
                        DeclareType(enumDeclaration, SymbolKind.EnumType);

                    break;
                case EventDeclaration enumDeclaration:
                    DeclareVariable(enumDeclaration, SymbolKind.Event);
                    break;
                case Declare { Signature: InterfaceDeclaration nested }:
                    DeclareInterface(nested, nested.SealedKeyword != null);
                    break;
            }
    }

    private bool ResolveStatement(Statement statement)
    {
        if (!IsDeclarationFile() || statement is Declare or TypeAlias or TraitDeclaration)
        {
            Visit(statement);
            return true;
        }

        _diagnostics.Error(statement, InternalCodes.RuntimeInDeclarationFile, "Only type-level declarations are allowed in declaration files.");
        return false;
    }

    /// <summary>
    ///     Ambient names of this file's own project. A dependency's declaration files are none of this file's
    ///     business, so a package cannot put names in the scope of the projects that depend on it.
    /// </summary>
    private void DeclareGlobalSymbols()
    {
        foreach (var (symbol, type) in compilationUnit.Globals.Of(parserResult.Tree.File))
        {
            DeclareSymbol(symbol);
            _semanticModel.TypeSolver.SetType(symbol.Declaration, type);
        }
    }

    private void DeclareIntrinsicSymbols()
    {
        foreach (var (symbol, _) in Intrinsics.Register(_semanticModel, compilationUnit))
            DeclareSymbol(symbol);
    }

    private bool IsDeclarationFile() => parserResult.Tree.File.IsDeclaration;
    private ResolverScope CurrentScope() => _scopes.Peek();
    private void PopScope() => _scopes.Pop();

    private ResolverScope PushScope()
    {
        var scope = new ResolverScope();
        _scopes.Push(scope);

        return scope;
    }

    /// <summary>
    ///     Whether resolution is at the top level of the file, where imports and exports belong. The ambient
    ///     scope sits below it, so scope depth alone does not answer this.
    /// </summary>
    private bool AtModuleScope() => ReferenceEquals(CurrentScope(), _moduleScope);

    protected override bool CombineResults(ReadOnlySpan<bool> results)
    {
        var finalResult = true;
        foreach (var result in results)
            finalResult &= result;

        return finalResult;
    }
}
