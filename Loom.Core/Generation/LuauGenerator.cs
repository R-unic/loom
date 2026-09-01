using System.Diagnostics.CodeAnalysis;
using Loom.Core.Diagnostics;
using Loom.Core.Generation.Events;
using Loom.Core.Generation.Macros;
using Loom.Core.Generation.Modules;
using Loom.Core.Generation.Serialization;
using Loom.Core.Modules;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Core.Resolving.Symbols;
using Loom.Luau;
using Loom.Luau.AST;
using BinaryOperator = Loom.Luau.AST.BinaryOperator;
using Expression = Loom.Core.Parsing.AST.Expression;
using ExpressionStatement = Loom.Luau.AST.ExpressionStatement;
using Identifier = Loom.Luau.AST.Identifier;
using Parameter = Loom.Core.Parsing.AST.Parameter;
using Return = Loom.Luau.AST.Return;
using Statement = Loom.Core.Parsing.AST.Statement;
using TypeExpression = Loom.Core.Parsing.AST.TypeExpression;
using TypeParameters = Loom.Core.Parsing.AST.TypeParameters;

namespace Loom.Core.Generation;

public sealed partial class LuauGenerator
    : Visitor<LuauNode>
{
    private readonly DiagnosticBag _diagnostics;
    private readonly EventConnectionTracker _eventConnections = new();
    private readonly Dictionary<Is, List<LuauStatement>> _isPreludes = [];
    private readonly Dictionary<Is, LuauExpression> _isSubjects = [];
    private readonly Dictionary<FunctionDeclaration, Identifier> _traitDefaultFunctions = [];

    /// <summary>
    ///     Every 'implement' block whose own table/method statements have already been emitted, in this
    ///     file's generation order so far - checked by <see cref="WrapWithImplementationMetatable" /> before
    ///     merging two or more trait tables into one <c>setmetatable</c> call, since Luau (unlike Loom's own
    ///     type checker, which hoists interfaces and their implementations) requires a local to be declared
    ///     before anything can reference it: a construction site earlier in the file than one of its
    ///     interface's 'implement' blocks would otherwise reference that trait's table before its own
    ///     <c>local X_for_Y = {}</c> runs, silently resolving to a global 'nil'.
    /// </summary>
    private readonly HashSet<Implement> _generatedImplements = [];

    /// <summary>
    ///     Every multi-trait interface actually constructed somewhere in this file ('new X { ... }' or
    ///     'x with { ... }'), determined by <see cref="CollectMultiTraitConstructions" /> before the main
    ///     tree walk starts. 'implement' is restricted to module scope (<see
    ///     cref="Resolver.VisitImplement" />), so its trait tables are always top-level locals -
    ///     but a CONSTRUCTION site may still legally sit inside a function body nested arbitrarily deep, so
    ///     <see cref="VisitImplement" /> cannot decide lazily, at the first site it happens to reach,
    ///     whether to build a shared metatable local: an ordinary <c>Prereq</c> there would attach to that
    ///     site's own (possibly nested) scope, invisible to a sibling function's own construction site.
    ///     Knowing the full set up front lets the merged local be emitted once, right where its interface's
    ///     own trait tables already are (a postreq of whichever 'implement' block is the last of its
    ///     interface's to generate) - the file's top level, visible everywhere a construction site could be.
    /// </summary>
    private readonly HashSet<InterfaceSymbol> _multiTraitInterfacesConstructed = [];

    /// <summary>
    ///     One shared merged metatable per multi-trait interface actually constructed (see <see
    ///     cref="_multiTraitInterfacesConstructed" />), built once as a top-level local right after that
    ///     interface's last 'implement' block generates, and reused by every construction site instead of
    ///     each calling <c>Loom.merge_meta</c> (fresh closure + table) for itself.
    /// </summary>
    private readonly Dictionary<InterfaceSymbol, Identifier> _mergedMetatables = [];

    private readonly Lazy<HashSet<(EventTarget Target, Symbol Function)>> _localSafeConnections;

    private readonly ArrayPipeline _arrayPipeline;
    private readonly MacroExpander _macroExpander;
    private readonly ModuleImportExportGenerator _moduleGenerator;
    private readonly RuntimeImport _runtimeImport;
    private readonly SemanticModel _semanticModel;
    private readonly LuauState _state = new();

    /// <summary>Buffer library members the file's serializers touched, hoisted into constants in <see cref="Generate" />.</summary>
    private readonly List<string> _bufferMembers = [];
    private readonly List<LuauStatement> _serializerStatements = [];

    public LuauGenerator(
        SemanticModel semanticModel,
        RuntimeImport? runtimeImport = null,
        ModuleRequirePathResolver? moduleRequirePaths = null,
        DiagnosticBag? typeCheckerDiagnostics = null)
        : this(semanticModel, runtimeImport, moduleRequirePaths, typeCheckerDiagnostics, null)
    {
    }

    /// <summary>Lets a test substitute a throwing test double for the real macro providers, to exercise <see cref="MacroExpander" />'s handling of a provider failure deterministically.</summary>
    internal LuauGenerator(
        SemanticModel semanticModel,
        RuntimeImport? runtimeImport,
        ModuleRequirePathResolver? moduleRequirePaths,
        DiagnosticBag? typeCheckerDiagnostics,
        IReadOnlyCollection<Macros.IMacroProvider>? macroProviders)
        : base(_ => new NoOpStatement())
    {
        _semanticModel = semanticModel;
        _diagnostics = new DiagnosticBag(options: semanticModel.Diagnostics.Options);
        _runtimeImport = runtimeImport ?? RuntimeImport.Default;
        _macroExpander = new MacroExpander(semanticModel, _state, _diagnostics, typeCheckerDiagnostics, macroProviders);
        _arrayPipeline = new ArrayPipeline(semanticModel, _state, Visit);
        _moduleGenerator = new ModuleImportExportGenerator(semanticModel, _diagnostics, moduleRequirePaths);
        _localSafeConnections = new Lazy<HashSet<(EventTarget Target, Symbol Function)>>(
            () => EventConnectionScopeAnalyzer.ComputeLocallySafeConnections(semanticModel)
        );
    }

    public LuauGeneratorResult Generate()
    {
        _serializerStatements.Clear();
        var moduleImports = _moduleGenerator.GenerateImports();
        CollectSerializationUsage();
        CollectMultiTraitConstructions();

        var luauTree = VisitTree(_semanticModel.Tree);
        foreach (var mapType in _semanticModel.SerializerMaps)
            if (SerializationEmitter.EmitSerializerMap(mapType, ResolveSerializerName) is { } map)
                luauTree.Statements.Insert(0, map);
        
        luauTree.Statements.InsertRange(0, _serializerStatements);
        if (_bufferMembers.Count > 0)
            luauTree.Statements.InsertRange(0, SerializationEmitter.DeclareBufferConstants(_bufferMembers));

        luauTree.Statements.InsertRange(0, _eventConnections.StoreDeclarations);
        luauTree.Statements.InsertRange(0, moduleImports);
        if (!_semanticModel.MustImportRuntimeLibrary)
            return new LuauGeneratorResult(luauTree, _diagnostics);

        if (_runtimeImport.Status == RuntimeImportStatus.NotFoundInRojo)
            _diagnostics.Warn(
                _semanticModel.Tree,
                InternalCodes.RuntimeLibraryNotFound,
                "Could not locate the Loom runtime library through the Rojo project; falling back to the default require path.",
                $"add a $path mapping to your default.project.json that includes the runtime, otherwise requires resolve to '{RuntimeImport.DefaultPath}'."
            );

        luauTree.Statements.Insert(0, LuauFactory.RuntimeLibraryImport(_runtimeImport.Path));
        return new LuauGeneratorResult(luauTree, _diagnostics);
    }

    protected override LuauNode Visit(Node node) => node.Accept(this);

    private Chunk GenerateFunctionBody(IFunctionLike functionLike)
    {
        var chunk = _state.CaptureIsolated(() =>
            functionLike.Body is ExpressionBody expressionBody
                ? new Chunk(GenerateStatements(expressionBody.Expression))
                : GenerateChunk(functionLike.Body)
        );

        var defaultGuards = (functionLike.Parameters?.ParameterList ?? [])
            .Where(parameter => parameter.DotDot == null && parameter.EqualsValueClause != null)
            .Select(GenerateParameterDefaultGuard)
            .ToList();

        if (defaultGuards.Count > 0)
            chunk.Statements.InsertRange(0, defaultGuards);

        return chunk;
    }

    private IfStatement GenerateParameterDefaultGuard(Parameter parameter) => GenerateDefaultGuard(parameter.Name.Text, parameter.EqualsValueClause!);

    /// <summary>Emits <c>if name == nil then name = &lt;default&gt; end</c> - the guard a parameter default and a destructuring default both compile to, since Luau does not distinguish an omitted argument or a short source array from an explicit <c>nil</c>.</summary>
    private IfStatement GenerateDefaultGuard(string name, EqualsValueClause equalsValueClause)
    {
        var identifier = new Identifier(name);
        var condition = new BinaryOperator(identifier, "==", new NilLiteral());

        var statements = new List<LuauStatement>();
        var (value, scope) = _state.Capture(() => Visit(equalsValueClause.Value));
        ApplyPrereqAndPostreq(statements, scope, new ExpressionStatement(new BinaryOperator(identifier, "=", value)));

        return new IfStatement(condition, new Chunk(statements), [], null);
    }

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
    ///     Detects a binding whose value is nothing more than the identifier a prereq statement in the
    ///     same scope already declared. Emitting both would be redundant, so the binding is elided in
    ///     favour of the prereq that exists.
    /// </summary>
    /// <remarks>
    ///     Two shapes reach here. A placeholder '_' binding, where the value was only ever wrapped to be
    ///     a statement; and a binding of a name to itself, which is what a macro leaves behind when it
    ///     accumulated straight into the name it was being bound to rather than into a temporary.
    /// </remarks>
    private static bool IsRedundantOrphanBinding(LuauStatement statement, LuauScope scope) =>
        IsRedundantPlaceholder(statement, scope) || IsSelfBinding(statement, scope);

    private static bool IsRedundantPlaceholder(LuauStatement statement, LuauScope scope) =>
        statement is ConstVariable { Name: "_", Initializer: Identifier identifier }
        && scope.PrereqStatements is [.., Variable lastPrereq]
        && lastPrereq.Name == identifier.Name;

    /// <summary>
    ///     'const kept = kept', where a prereq already declared 'kept'. Only a macro that accumulated
    ///     into the binding's own name produces this - the name is in scope before the initializer is
    ///     generated, so nothing else can bind a name to the one it is declaring.
    /// </summary>
    private static bool IsSelfBinding(LuauStatement statement, LuauScope scope) =>
        statement is Variable { Name: var name } variable
        && InitializerOf(variable) is Identifier { Name: var initialized }
        && name == initialized
        && scope.PrereqStatements.Exists(prereq => prereq is Variable declared && declared.Name == name);

    private static LuauExpression? InitializerOf(Variable variable) =>
        variable switch
        {
            ConstVariable constant => constant.Initializer,
            LocalVariable local => local.Initializer,
            _ => null
        };

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
    
    private LuauType Visit(TypeExpression node) => node.Accept(this) as LuauType ?? UnknownType;
    private LuauExpression Visit(Expression node) => node.Accept(this) as LuauExpression ?? new NilLiteral();
    private LuauStatement Visit(Statement node) => node.Accept(this) as LuauStatement ?? new NoOpStatement();

    private static LuauType UnknownType => Luau.AST.PrimitiveType.Unknown;
}