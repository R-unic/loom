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
    private readonly DiagnosticBag _diagnostics = new();
    private readonly Stack<ResolverScope> _scopes = [];
    private ResolverContext _context = ResolverContext.None;
    private SemanticModel _semanticModel = null!;

    [MemberNotNull(nameof(_semanticModel))]
    public SemanticModel Resolve()
    {
        _semanticModel = new SemanticModel(parserResult.Tree, _diagnostics, _allDeclarations, _allReferences)
        {
            EmitDebugDiagnostics = compilationUnit.Config.Debug
        };

        PushScope();
        DeclareIntrinsicSymbols();
        DeclareGlobalSymbols();
        VisitTree(parserResult.Tree);
        ReportUnusedImports();
        PopScope();

        return _semanticModel;
    }

    protected override bool Visit(Node node) => node.Accept(this);

    public override bool VisitTree(Tree tree) => ResolveStatements(tree.Statements);

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

    private void DeclareGlobalSymbols()
    {
        foreach (var (symbol, type) in compilationUnit.Globals)
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
    private void PushScope() => _scopes.Push(new ResolverScope());

    protected override bool CombineResults(ReadOnlySpan<bool> results)
    {
        var finalResult = true;
        foreach (var result in results)
            finalResult &= result;

        return finalResult;
    }
}
