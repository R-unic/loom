using System.Diagnostics.CodeAnalysis;
using Loom.Core.Diagnostics;
using Loom.Core.Generation.Events;
using Loom.Core.Generation.Macros;
using Loom.Core.Modules;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Core.Resolving.Symbols;
using Loom.Luau;
using Loom.Luau.AST;
using BinaryOperator = Loom.Luau.AST.BinaryOperator;
using Expression = Loom.Core.Parsing.AST.Expression;
using ExpressionStatement = Loom.Luau.AST.ExpressionStatement;
using FunctionDeclaration = Loom.Core.Parsing.AST.FunctionDeclaration;
using Identifier = Loom.Luau.AST.Identifier;
using Return = Loom.Luau.AST.Return;
using Statement = Loom.Core.Parsing.AST.Statement;
using TypeExpression = Loom.Core.Parsing.AST.TypeExpression;
using TypeParameters = Loom.Core.Parsing.AST.TypeParameters;

namespace Loom.Core.Generation;

public sealed partial class LuauGenerator
    : Visitor<LuauNode>
{
    private readonly DiagnosticBag _diagnostics = new();
    private readonly EventConnectionTracker _eventConnections = new();
    private readonly Lazy<HashSet<(EventTarget Target, Symbol Function)>> _localSafeConnections;
    private readonly MacroExpander _macroExpander;
    private readonly ModuleImportExportGenerator _moduleGenerator;
    private readonly ModuleRequirePathResolver? _moduleRequirePaths;
    private readonly RuntimeImport _runtimeImport;
    private readonly SemanticModel _semanticModel;
    private readonly LuauState _state = new();

    public LuauGenerator(SemanticModel semanticModel, RuntimeImport? runtimeImport = null, ModuleRequirePathResolver? moduleRequirePaths = null)
        : base(_ => new NoOpStatement())
    {
        _semanticModel = semanticModel;
        _runtimeImport = runtimeImport ?? RuntimeImport.Default;
        _moduleRequirePaths = moduleRequirePaths;
        _macroExpander = new MacroExpander(semanticModel, _state, _diagnostics);
        _moduleGenerator = new ModuleImportExportGenerator(semanticModel, _diagnostics, moduleRequirePaths);
        _localSafeConnections = new Lazy<HashSet<(EventTarget Target, Symbol Function)>>(
            () => EventConnectionScopeAnalyzer.ComputeLocallySafeConnections(semanticModel)
        );
    }

    public LuauGeneratorResult Generate()
    {
        var moduleImports = _moduleGenerator.GenerateImports();
        var luauTree = VisitTree(_semanticModel.Tree);
        luauTree.Statements.InsertRange(0, _eventConnections.StoreDeclarations);
        luauTree.Statements.InsertRange(0, moduleImports);

        if (_semanticModel.MustImportRuntimeLibrary)
        {
            if (_runtimeImport.Status == RuntimeImportStatus.NotFoundInRojo)
                _diagnostics.Warn(
                    _semanticModel.Tree,
                    InternalCodes.RuntimeLibraryNotFound,
                    "Could not locate the Loom runtime library through the Rojo project; falling back to the default require path.",
                    $"add a $path mapping to your default.project.json that includes the runtime, otherwise requires resolve to '{RuntimeImport.DefaultPath}'."
                );

            luauTree.Statements.Insert(0, LuauFactory.RuntimeLibraryImport(_runtimeImport.Path));
        }

        return new LuauGeneratorResult(luauTree, _diagnostics);
    }

    protected override LuauNode Visit(Node node) => node.Accept(this);

    private Chunk GenerateFunctionBody(FunctionDeclaration functionDeclaration) =>
        functionDeclaration.Body is ExpressionBody expressionBody
            ? new Chunk(GenerateStatements(expressionBody.Expression))
            : GenerateChunk(functionDeclaration.Body);

    private Luau.AST.TypeParameters GenerateTypeParameters(TypeParameters? typeParameters) =>
        MaybeVisit<Luau.AST.TypeParameters>(typeParameters) ?? new Luau.AST.TypeParameters();

    private Chunk GenerateChunk(Statement statement) =>
        statement is Block block
            ? new Chunk(GenerateStatements(block.Statements))
            : new Chunk(GenerateStatements(statement));

    private List<LuauStatement> GenerateStatements(List<Statement> statements)
    {
        var result = new List<LuauStatement>();
        foreach (var statement in statements)
            result.AddRange(GenerateStatements(statement));

        return result.FindAll(s => s is not NoOpStatement);
    }

    private List<LuauStatement> GenerateStatements(Expression expression)
    {
        var result = new List<LuauStatement>();
        var (luauExpression, scope) = _state.Capture(() => Visit(expression));
        ApplyPrereqAndPostreq(result, scope, new Return(luauExpression));

        return result.FindAll(s => s is not NoOpStatement);
    }

    private List<LuauStatement> GenerateStatements(Statement statement)
    {
        if (statement is NamedDeclaration { Name.Text: var name })
            _state.Scope.AddIdentifier(name);

        var result = new List<LuauStatement>();
        var (luauStatement, scope) = _state.Capture(() => Visit(statement));
        if (IsRedundantOrphanBinding(luauStatement, scope))
            luauStatement = new NoOpStatement();

        ApplyPrereqAndPostreq(result, scope, luauStatement);

        return result.FindAll(s => s is not NoOpStatement);
    }

    /// <summary>
    ///     Detects a placeholder '_' binding whose value is nothing more than the identifier
    ///     a prereq statement in the same scope just declared. Emitting both would be redundant, so the
    ///     placeholder is elided in favor of the prereq statement that already exists.
    /// </summary>
    private static bool IsRedundantOrphanBinding(LuauStatement statement, LuauScope scope) =>
        statement is ConstVariable { Name: "_", Initializer: Identifier identifier }
        && scope.PrereqStatements is [.., Variable lastPrereq]
        && lastPrereq.Name == identifier.Name;

    private static void ApplyPrereqAndPostreq(List<LuauStatement> result, LuauScope scope, LuauStatement luauStatement)
    {
        result.AddRange(scope.PrereqStatements);
        result.Add(luauStatement);
        result.AddRange(scope.PostreqStatements);
    }

    private static LuauStatement WrapExpressionAsStatement(LuauExpression expression) =>
        IsUnorphanableExpression(expression)
            ? new ExpressionStatement(expression)
            : new ConstVariable("_", null, expression);

    private static bool IsUnorphanableExpression(LuauExpression expression) =>
        expression is Call
        || expression is BinaryOperator binaryOperator
        && binaryOperator.Operator.EndsWith('=')
        && binaryOperator.Operator is not ("==" or "~=" or "<=" or ">=");

    private bool ValidateLuauNameAttribute(AttributeSymbol luauNameAttribute, [MaybeNullWhen(false)] out StringLiteral nameLiteral)
    {
        var luauName = Visit(luauNameAttribute.Attribute.Arguments.ArgumentList[0]);
        if (luauName is not StringLiteral stringLiteral)
        {
            _diagnostics.Error(
                luauNameAttribute.Attribute,
                InternalCodes.InvalidLuauNameAttribute,
                "May only use string literals for name parameter on 'luau_name' attribute"
            );

            nameLiteral = null;
            return false;
        }

        nameLiteral = stringLiteral;
        return true;
    }

    private LuauType Visit(TypeExpression node) => (LuauType)node.Accept(this);
    private LuauExpression Visit(Expression node) => (LuauExpression)node.Accept(this);
    private LuauStatement Visit(Statement node) => (LuauStatement)node.Accept(this);
}