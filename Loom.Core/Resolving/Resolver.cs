using System.Diagnostics.CodeAnalysis;
using Loom.Core.Diagnostics;
using Loom.Core.Parsing;
using Loom.Core.Parsing.AST;
using Loom.Core.Pipeline;
using Loom.Core.Resolving.Symbols;

namespace Loom.Core.Resolving;

public sealed partial class Resolver(ParserResult parserResult, CompilationUnit compilationUnit)
    : Visitor<bool>(_ => true)
{
    private readonly SymbolTable _allDeclarations = [];
    private readonly SymbolTable _allReferences = [];
    private readonly DiagnosticBag _diagnostics = new(options: compilationUnit.DiagnosticOptionsFor(parserResult.Tree.File));
    private readonly HashSet<Node> _resolvedImports = [];
    private readonly List<ExportAll> _starExports = [];
    private readonly Stack<ResolverScope> _scopes = [];
    private ResolverScope? _moduleScope;
    private ResolverContext _context = ResolverContext.None;
    private SemanticModel _semanticModel = null!;

    [MemberNotNull(nameof(_semanticModel))]
    public SemanticModel Resolve()
    {
        _semanticModel = new SemanticModel(parserResult.Tree, _diagnostics, _allDeclarations, _allReferences);

        // ambient names live in a scope of their own so that a module declaring 'Vector3' shadows the
        // intrinsic rather than colliding with it — the file's own declarations are the ones it can see.
        // The intrinsics are a scope the whole project shares; the root's own '.d.loom' globals sit in one
        // above it, since those differ per root and may shadow an intrinsic name.
        var intrinsics = AmbientIntrinsics.For(compilationUnit);
        _semanticModel.Ambient = intrinsics;
        _semanticModel.TypeSolver.AmbientTypes = intrinsics.Types;

        using var intrinsicScope = InScope(intrinsics.Scope);
        using var globalScope = InScope();
        DeclareGlobalSymbols();

        using var moduleScope = InScope();
        _moduleScope = moduleScope.Scope;
        VisitTree(parserResult.Tree);
        ReportUnusedImports();

        return _semanticModel;
    }

    protected override bool Visit(Node node) => node.Accept(this);

    /// <remarks>
    ///     Imports are resolved before anything else in the file, so a name can be used above the import that
    ///     brings it in — the generator emits every require at the top of the output regardless, so reading
    ///     them in source order would reject code the output supports. An import that names no module is left
    ///     for its turn in source order: what it binds is only a stand-in, which must not take a name the file
    ///     goes on to declare for itself.
    ///     <para>
    ///         Star exports go last for the mirrored reason: what one forwards is whatever the file did not
    ///         export itself, which is only known once every statement has been resolved.
    ///     </para>
    /// </remarks>
    public override bool VisitTree(Tree tree)
    {
        foreach (var statement in tree.Statements)
            if (statement is ImportDeclaration or NamespaceImport && TryGetModule(statement, out _, out _))
                Visit(statement);

        var resolved = ResolveStatements(tree.Statements);
        ResolveStarExports();

        return resolved;
    }

    public override bool VisitBlock(Block block)
    {
        using var _ = InScope();
        return ResolveStatements(block.Statements);
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
                    if (DeclareVariable(interfaceDeclaration))
                        DeclareInterface(interfaceDeclaration, interfaceDeclaration.SealedKeyword != null);

                    break;
                case EnumDeclaration enumDeclaration:
                    if (DeclareVariable(enumDeclaration))
                        DeclareType(enumDeclaration, new EnumTypeSymbol(enumDeclaration, enumDeclaration.Name.Text));

                    break;
                case EventDeclaration eventDeclaration:
                    DeclareVariable(eventDeclaration, new EventSymbol(eventDeclaration));
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

    private bool IsDeclarationFile() => parserResult.Tree.File.IsDeclaration;
    private ResolverScope CurrentScope() => _scopes.Peek();

    /// <summary>
    ///     Pushes a scope that pops itself at the end of the enclosing block: <c>using var _ = InScope();</c>.
    ///     This is the only way to open one — pairing a push with a pop by hand works right up until somebody
    ///     adds a <c>return</c> between them, and a scope left on the stack silently changes what every later
    ///     name in the file resolves to.
    /// </summary>
    private ScopeHandle InScope() => InScope(new ResolverScope());

    /// <summary>
    ///     Enters a scope that already exists, for one shared with other files - see
    ///     <see cref="AmbientIntrinsics" />. Nothing is declared into it here; it is only made visible.
    /// </summary>
    private ScopeHandle InScope(ResolverScope scope)
    {
        _scopes.Push(scope);
        return new ScopeHandle(this, scope);
    }

    /// <summary>Pops the scope <see cref="InScope()" /> pushed. Use its <see cref="Scope" /> to reach the scope itself.</summary>
    private readonly ref struct ScopeHandle(Resolver resolver, ResolverScope scope)
    {
        public ResolverScope Scope { get; } = scope;
        public void Dispose() => resolver._scopes.Pop();
    }

    /// <summary>
    ///     Enters <paramref name="context" /> until the end of the enclosing block:
    ///     <c>using var _ = InContext(ResolverContext.Loop);</c>. Restores whatever was current before rather
    ///     than clearing, since these nest — a loop inside a function is still inside the function.
    /// </summary>
    private ContextHandle InContext(ResolverContext context)
    {
        var handle = new ContextHandle(this, _context);
        _context = context;

        return handle;
    }

    private readonly ref struct ContextHandle(Resolver resolver, ResolverContext previous)
    {
        public void Dispose() => resolver._context = previous;
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
