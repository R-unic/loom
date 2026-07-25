using System.Diagnostics.CodeAnalysis;
using Loom.Core.Diagnostics;
using Loom.Core.Parsing;
using Loom.Core.Parsing.AST;
using Loom.Core.Text;
using Loom.Core.TypeChecking;
using Loom.Luau;
using Attribute = Loom.Core.Parsing.AST.Attribute;

namespace Loom.Core.Resolving;

public sealed class Resolver(ParserResult parserResult, CompilationUnit compilationUnit)
    : Visitor<bool>(_ => true)
{
    private readonly DiagnosticBag _diagnostics = new();
    private readonly SymbolTable _allDeclarations = [];
    private readonly SymbolTable _allReferences = [];
    private readonly Stack<ResolverScope> _scopes = [];
    private SemanticModel _semanticModel = null!;
    private ResolverContext _context = ResolverContext.None;

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

    public override bool VisitImplement(Implement implement)
    {
        var traitNameSymbol = LookupTypeSymbol(implement.TraitName.Name.Text);
        if (traitNameSymbol is not TraitSymbol traitSymbol)
        {
            _diagnostics.Error(implement.TraitName, InternalCodes.NonInterfaceImplementation, "Interfaces may only implement traits.");
            return false;
        }

        AddReference(implement.TraitName, traitSymbol);

        var interfaceNameSymbol = LookupTypeSymbol(implement.InterfaceName.Name.Text);
        if (interfaceNameSymbol is not InterfaceSymbol interfaceSymbol)
        {
            _diagnostics.Error(implement.InterfaceName, InternalCodes.NonInterfaceImplementation, "Traits may only be implemented by interfaces.");
            return false;
        }

        if (interfaceSymbol.IsIntrinsic)
        {
            _diagnostics.Error(
                implement.InterfaceName,
                InternalCodes.IntrinsicImplementation,
                $"Trait '{implement.TraitName}' may not be implemented on intrinsic interface '{implement.InterfaceName}'."
            );

            return false;
        }

        AddReference(implement.InterfaceName, interfaceSymbol);

        if (interfaceSymbol.Implements.Contains(traitSymbol))
        {
            _diagnostics.Error(
                implement.TraitName,
                InternalCodes.DuplicateImplementation,
                $"Interface '{interfaceSymbol.Name}' already has an implementation for trait '{traitSymbol.Name}'"
            );

            return false;
        }

        foreach (var implementation in implement.Body.Implementations.Where(implementation => !traitSymbol.MethodNames.Contains(implementation.Name.Text)))
        {
            _diagnostics.Error(
                implementation,
                InternalCodes.InvalidImplementation,
                $"Trait '{traitSymbol.Name}' does not contain a signature for method '{implementation.Name.Text}'"
            );

            return false;
        }

        foreach (var methodName in traitSymbol.MethodNames.Where(methodName => implement.Body.Implementations.All(i => methodName != i.Name.Text)))
        {
            _diagnostics.Error(
                implement,
                InternalCodes.MissingImplementation,
                $"Implementation of trait '{traitSymbol.Name}' on interface '{interfaceSymbol.Name}' is missing method '{methodName}'"
            );

            return false;
        }

        PushScope();
        interfaceSymbol.Implementations.Add(implement);
        interfaceSymbol.Implements.Add(traitSymbol);
        traitSymbol.ImplementedBy.Add(interfaceSymbol);
        if (interfaceSymbol.Properties
            .Any(property => !DeclareVariable(implement, new InjectedPropertyVariableSymbol(implement, property.Name, interfaceSymbol, property.IsMutable))))
        {
            return false;
        }

        Visit(implement.Body);
        PopScope();
        return true;
    }

    public override bool VisitTraitDeclaration(TraitDeclaration traitDeclaration)
    {
        if (!DeclareTrait(traitDeclaration) || !ResolveTraitBody(traitDeclaration.Body, traitDeclaration.Name.Text))
            return false;

        PushScope();
        base.VisitTraitDeclaration(traitDeclaration);
        PopScope();

        return true;
    }

    public override bool VisitAfter(After after)
    {
        Visit(after.Duration);

        var lastContext = _context;
        _context = ResolverContext.Scheduler;
        Visit(after.Body);
        _context = lastContext;

        return true;
    }

    public override bool VisitFor(For @for)
    {
        Visit(@for.CollectionExpression);
        PushScope();
        if (@for.Names.Any(name => !DeclareVariable(name, name.Token.Text, SymbolKind.Variable)))
            return false;

        var lastContext = _context;
        _context = ResolverContext.Loop;
        Visit(@for.Body);
        _context = lastContext;

        PopScope();
        return true;
    }

    public override bool VisitMatchExpression(MatchExpression matchExpression)
    {
        if (!Visit(matchExpression.Expression))
            return false;

        foreach (var arm in matchExpression.Arms)
            if (!Visit(arm))
                return false;

        return true;
    }

    public override bool VisitMatchArm(MatchArm matchArm)
    {
        PushScope();

        var success =
            Visit(matchArm.Pattern)
            && (matchArm.Guard == null || Visit(matchArm.Guard))
            && Visit(matchArm.Body);

        PopScope();
        return success;
    }

    public override bool VisitIdentifierPattern(IdentifierPattern identifierPattern)
        => DeclareVariable(identifierPattern, identifierPattern.Name.Text, SymbolKind.Variable);

    public override bool VisitLetPattern(LetPattern letPattern)
        => DeclareVariable(letPattern, letPattern.Name.Text, SymbolKind.Variable);

    public override bool VisitTypedPattern(TypedPattern typedPattern)
        => DeclareVariable(typedPattern, typedPattern.Name.Text, SymbolKind.Variable)
        && Visit(typedPattern.Type)
        && (typedPattern.ObjectPattern == null || Visit(typedPattern.ObjectPattern));

    public override bool VisitTypePattern(TypePattern typePattern)
        => Visit(typePattern.Type)
        && (typePattern.ObjectPattern == null || Visit(typePattern.ObjectPattern));

    public override bool VisitObjectPattern(ObjectPattern objectPattern)
    {
        foreach (var field in objectPattern.Fields)
            if (!Visit(field))
                return false;

        return true;
    }

    public override bool VisitObjectPatternField(ObjectPatternField objectPatternField)
        => Visit(objectPatternField.Pattern);

    public override bool VisitArrayPattern(ArrayPattern arrayPattern)
    {
        foreach (var element in arrayPattern.Elements)
            if (!Visit(element))
                return false;

        return arrayPattern.Rest == null || Visit(arrayPattern.Rest);
    }

    public override bool VisitRestPattern(RestPattern restPattern)
        => Visit(restPattern.Pattern);

    public override bool VisitOrPattern(OrPattern orPattern)
    {
        foreach (var pattern in orPattern.Patterns)
            if (!Visit(pattern))
                return false;

        return true;
    }

    public override bool VisitWildcardPattern(WildcardPattern wildcardPattern) => true;

    public override bool VisitLiteralPattern(LiteralPattern literalPattern) => true;

    public override bool VisitRangePattern(RangePattern rangePattern)
        => Visit(rangePattern.Minimum) && Visit(rangePattern.Maximum);

    public override bool VisitNullPattern(NullPattern nullPattern) => true;

    public override bool VisitWhile(While @while)
    {
        Visit(@while.Condition);

        var lastContext = _context;
        _context = ResolverContext.Loop;
        Visit(@while.Body);
        _context = lastContext;

        return true;
    }

    public override bool VisitContinue(Continue @continue)
    {
        if (_context == ResolverContext.Loop)
            return base.VisitContinue(@continue);

        _diagnostics.Error(@continue, InternalCodes.ContinueOutsideLoop, "Continue statements can only be used inside of loops.");
        return false;
    }

    public override bool VisitBreak(Break @break)
    {
        if (_context == ResolverContext.Loop)
            return base.VisitBreak(@break);

        _diagnostics.Error(@break, InternalCodes.BreakOutsideLoop, "Break statements can only be used inside of loops.");
        return false;
    }

    public override bool VisitReturn(Return @return)
    {
        if (@return.FirstAncestorOfType<FunctionDeclaration>() is { } functionDeclaration)
        {
            var after = @return.FirstAncestorOfType<After>();
            if (after == null || functionDeclaration.FirstAncestorOfType<After>() == after)
                return base.VisitReturn(@return);

            _diagnostics.Error(@return, InternalCodes.ReturnInAfter, "Cannot return a value from an 'after' statement body.");
            return false;
        }

        _diagnostics.Error(@return, InternalCodes.ReturnOutsideFunction, "Return statements can only be used inside of functions.");
        return false;
    }

    public override bool VisitFunctionDeclaration(FunctionDeclaration functionDeclaration)
    {
        var scope = CurrentScope();
        var name = functionDeclaration.Name.Text;
        if (scope.VariableLookup.ContainsKey(name))
        {
            _diagnostics.Error(functionDeclaration, InternalCodes.DuplicateName, $"Variable '{name}' is already declared in this scope.");
            return false;
        }

        if (!DeclareVariable(functionDeclaration, SymbolKind.Function))
            return false;

        PushScope();
        var lastContext = _context;
        _context = ResolverContext.Function;
        if (functionDeclaration.Body is Block { Statements: [Return] })
            _diagnostics.Warn(functionDeclaration, InternalCodes.RedundantCode, "Use expression body.");

        base.VisitFunctionDeclaration(functionDeclaration);
        _context = lastContext;
        PopScope();

        return true;
    }

    public override bool VisitTypeAlias(TypeAlias typeAlias)
    {
        if (!DeclareType(typeAlias))
            return false;

        PushScope();
        base.VisitTypeAlias(typeAlias);
        PopScope();

        return true;
    }

    public override bool VisitVariableDeclaration(VariableDeclaration variableDeclaration)
    {
        var isMutable = variableDeclaration.Keyword.Kind == SyntaxKind.MutKeyword;
        if (!DeclareVariable(variableDeclaration, SymbolKind.Variable, isMutable))
            return false;

        base.VisitVariableDeclaration(variableDeclaration);
        if (variableDeclaration.EqualsValueClause != null || isMutable)
            return true;

        _diagnostics.Error(variableDeclaration, InternalCodes.MustHaveInitializer, "Immutable declarations must be initialized.");
        return false;
    }

    public override bool VisitInterfaceDeclaration(InterfaceDeclaration interfaceDeclaration)
    {
        var isSealed = interfaceDeclaration.SealedKeyword != null;
        if (!DeclareVariable(interfaceDeclaration, SymbolKind.Variable)
            || DeclareInterface(interfaceDeclaration, isSealed) is not { } symbol
            || !ResolveInterfaceConstraints(interfaceDeclaration.ColonTypeListClause, symbol))
        {
            return false;
        }

        PushScope();
        base.VisitInterfaceDeclaration(interfaceDeclaration);
        PopScope();

        return ResolveInterfaceBody(interfaceDeclaration.Body, symbol);
    }

    public override bool VisitDeclare(Declare declare)
    {
        var lastContext = _context;
        _context = ResolverContext.Ambient;

        bool result;
        if (declare.Signature is InterfaceDeclaration interfaceDeclaration)
        {
            var isSealed = interfaceDeclaration.SealedKeyword != null;
            var interfaceSymbol = DeclareInterface(interfaceDeclaration, isSealed);
            result = interfaceSymbol is not null
                && ResolveInterfaceConstraints(interfaceDeclaration.ColonTypeListClause, interfaceSymbol);

            if (result)
            {
                interfaceSymbol!.IsAmbient = true;
                PushScope();
                result &= base.VisitInterfaceDeclaration(interfaceDeclaration);
                PopScope();
                result &= ResolveInterfaceBody(interfaceDeclaration.Body, interfaceSymbol);
            }
        }
        else
        {
            result = Visit(declare.Signature);
        }

        _context = lastContext;
        return result;
    }

    public override bool VisitDeclareFunctionSignature(DeclareFunctionSignature declareFunctionSignature)
    {
        if (!DeclareVariable(declareFunctionSignature, SymbolKind.Function))
            return false;

        PushScope();
        base.VisitDeclareFunctionSignature(declareFunctionSignature);
        PopScope();

        return true;
    }

    public override bool VisitDeclareVariableSignature(DeclareVariableSignature declareVariableSignature)
    {
        if (declareVariableSignature.ColonTypeClause == null && declareVariableSignature.Parent is not For)
        {
            _diagnostics.Error(
                declareVariableSignature,
                InternalCodes.MissingDeclareVariableType,
                "Declared variable signatures must have a type."
            );

            return false;
        }

        var isMutable = declareVariableSignature.Keyword.Kind == SyntaxKind.MutKeyword;
        return DeclareVariable(declareVariableSignature, SymbolKind.Variable, isMutable) && base.VisitDeclareVariableSignature(declareVariableSignature);
    }

    public override bool VisitFunctionType(FunctionType functionType)
    {
        PushScope();
        base.VisitFunctionType(functionType);
        PopScope();

        return true;
    }

    public override bool VisitParameter(Parameter parameter)
    {
        var name = parameter.Name.Text;
        var existingSymbol = LookupSymbolCurrentScope(name, SymbolKind.Parameter);
        if (existingSymbol != null)
        {
            _diagnostics.Error(
                parameter,
                InternalCodes.DuplicateName,
                existingSymbol.Kind == SymbolKind.Parameter
                    ? $"Parameter '{name}' is already declared for this function."
                    : $"Variable '{name}' is already declared in this scope."
            );

            return false;
        }

        var symbol = new Symbol(parameter, SymbolKind.Parameter, name);
        DeclareSymbol(symbol);

        if (parameter.EqualsValueClause != null || parameter.ColonTypeClause != null || parameter.Parent?.Parent?.Parent is ImplementBody)
            return base.VisitParameter(parameter);

        _diagnostics.Error(parameter, InternalCodes.MustHaveDefaultOrType, "Parameter must have a declared type or default value to infer from.");
        return false;
    }

    public override bool VisitEnumDeclaration(EnumDeclaration enumDeclaration) =>
        DeclareVariable(enumDeclaration, SymbolKind.Variable)
        && DeclareType(enumDeclaration, SymbolKind.EnumType)
        && base.VisitEnumDeclaration(enumDeclaration);

    public override bool VisitEventDeclaration(EventDeclaration eventDeclaration)
    {
        if (!DeclareVariable(eventDeclaration, SymbolKind.Event))
            return false;
                
        PushScope();
        base.VisitEventDeclaration(eventDeclaration);
        PopScope();

        return true;
    }

    public override bool VisitInterfaceInvocation(InterfaceInvocation interfaceInvocation)
    {
        var name = interfaceInvocation.Name.Token.Text;
        var typeSymbol = LookupTypeSymbol(name);
        var symbol = LookupValueSymbol(name) ?? typeSymbol;
        switch (symbol)
        {
            case null:
                _diagnostics.Error(interfaceInvocation.Name, InternalCodes.CannotFindSymbol, $"Cannot find interface symbol '{name}'.");
                return false;
            case InterfaceSymbol:
                _diagnostics.Error(
                    interfaceInvocation,
                    InternalCodes.InvokeDeclaredInterface,
                    $"Cannot invoke interface '{name}' because it was declared as a type."
                );

                return false;
        }

        AddReference(interfaceInvocation.Name, symbol);
        if (typeSymbol != null)
            AddReference(interfaceInvocation.Name, typeSymbol);

        return base.VisitInterfaceInvocation(interfaceInvocation);
    }

    public override bool VisitIdentifier(Identifier identifier)
    {
        var name = identifier.Name.Text;
        var symbol = LookupValueSymbol(name);
        if (symbol == null)
        {
            _diagnostics.Error(identifier, InternalCodes.CannotFindName, $"Cannot find name '{name}'.");
            return false;
        }

        if (symbol.Declaration is EnumDeclaration && identifier.Parent is not (QualifiedName or PropertyAccess or ElementAccess))
        {
            _diagnostics.Error(identifier, InternalCodes.DynamicEnumAccess, "Cannot use enums dynamically because they are compile-time constants.");
            return false;
        }

        AddReference(identifier, symbol);
        return true;
    }

    public override bool VisitTypeName(TypeName typeName)
    {
        var name = typeName.Name.Text;
        var symbol = LookupTypeSymbol(name);
        if (symbol == null)
        {
            _diagnostics.Error(typeName, InternalCodes.CannotFindName, $"Cannot find type '{name}'.");
            return false;
        }

        base.VisitTypeName(typeName);
        AddReference(typeName, symbol);
        return true;
    }

    public override bool VisitTypeParameter(TypeParameter typeParameter) => DeclareType(typeParameter) && base.VisitTypeParameter(typeParameter);

    private bool ResolveTraitBody(TraitBody body, string name)
    {
        var methodNames = body.Members.Select(p => p.Name.Text).ToList();
        var duplicates = methodNames.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count <= 0)
            return true;

        foreach (var duplicate in duplicates)
        {
            var property = body.Members.FindLast(m => m.Name.Text == duplicate)!;
            _diagnostics.Error(property, InternalCodes.DuplicateName, $"Method '{duplicate}' already exists on trait '{name}'");
        }

        return false;
    }

    private bool ResolveInterfaceBody(InterfaceBody? body, InterfaceSymbol interfaceSymbol)
    {
        if (body == null)
            return true;

        var indexers = body.Members.OfType<IndexerDeclaration>().ToList();
        if (indexers.Count > 1)
        {
            foreach (var extraIndexer in indexers.Skip(1))
                _diagnostics.Error(extraIndexer, InternalCodes.DuplicateIndexer, $"Type '{interfaceSymbol.Name}' may only have one indexer.");

            return false;
        }

        var properties = body.Members.OfType<PropertyDeclaration>().ToList();
        foreach (var symbol in
                 from property in properties
                 let attributeSymbols = property.Attributes?.AttributeList.Select(DeclareAttribute).ToList() ?? []
                 let pointsTo = _semanticModel.GetSymbol(property.ColonTypeClause.Type, SymbolKind.Interface) as InterfaceSymbol
                 select new PropertySymbol(property, pointsTo, attributeSymbols) { IsIntrinsic = interfaceSymbol.IsIntrinsic })
        {
            interfaceSymbol.Properties.Add(symbol);
            AddDeclaration(symbol);
        }

        var events = body.Members.OfType<EventDeclaration>().ToList();
        foreach (var symbol in
                 from eventDeclaration in events
                 let attributeSymbols = eventDeclaration.Attributes?.AttributeList.Select(DeclareAttribute).ToList() ?? []
                 select new PropertySymbol(eventDeclaration, null, attributeSymbols, SymbolKind.Event) { IsIntrinsic = interfaceSymbol.IsIntrinsic })
        {
            interfaceSymbol.Properties.Add(symbol);
            AddDeclaration(symbol);
        }

        var duplicateGroups = properties.GroupBy(p => p.Name.Text).Where(g => g.Count() > 1).ToList();
        var invalidDuplicateGroups = duplicateGroups.Where(g => !g.All(p => p.ColonTypeClause.Type is FunctionType)).ToList();
        if (invalidDuplicateGroups.Count <= 0)
            return true;

        foreach (var group in invalidDuplicateGroups)
            _diagnostics.Error(group.Last(), InternalCodes.DuplicateName, $"Property '{group.Key}' already exists on type '{interfaceSymbol.Name}'");

        return false;
    }

    private bool ResolveInterfaceConstraints(ColonTypeListClause? colonTypeListClause, InterfaceSymbol symbol)
    {
        if (colonTypeListClause == null)
            return true;

        foreach (var constraint in colonTypeListClause.Types)
        {
            if (constraint is not TypeName typeName)
                return ReportNonInterfaceConstraint(constraint);

            var constraintSymbol = LookupTypeSymbol(typeName.Name.Text);
            if (constraintSymbol is not InterfaceSymbol interfaceSymbol)
                return ReportNonInterfaceConstraint(constraint);

            if (!interfaceSymbol.IsSealed) continue;
            _diagnostics.Error(
                constraint,
                InternalCodes.InheritFromSealed,
                $"Cannot constrain interface '{symbol.Name}' with sealed interface '{interfaceSymbol.Name}'."
            );

            return false;
        }

        return true;
    }

    private bool ResolveStatements(List<Statement> statements)
    {
        HoistDeclarations(statements);
        return statements.All(ResolveStatement);
    }

    private void HoistDeclarations(List<Statement> statements)
    {
        foreach (var statement in statements)
        {
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

    private bool DeclareTrait(TraitDeclaration traitDeclaration)
    {
        var scope = CurrentScope();
        var name = traitDeclaration.Name.Text;
        if (scope.TypeLookup.TryGetValue(name, out var symbols))
        {
            if (IsAlreadyHoisted(traitDeclaration, symbols))
                return true;

            var kindName = symbols is [.., TraitSymbol] ? "Trait" : "Type";
            _diagnostics.Error(traitDeclaration.Name, InternalCodes.DuplicateName, $"{kindName} '{name}' is already declared in this scope.");
            return false;
        }

        DeclareSymbol(new TraitSymbol(traitDeclaration, name));
        return true;
    }

    private InterfaceSymbol? DeclareInterface(InterfaceDeclaration interfaceDeclaration, bool isSealed)
    {
        var scope = CurrentScope();
        var name = interfaceDeclaration.Name.Text;
        if (scope.TypeLookup.TryGetValue(name, out var symbols))
        {
            if (IsAlreadyHoisted<InterfaceSymbol>(interfaceDeclaration, symbols, out var interfaceSymbol))
                return interfaceSymbol;

            var kindName = symbols is [.., InterfaceSymbol] ? "Interface" : "Type";
            _diagnostics.Error(interfaceDeclaration.Name, InternalCodes.DuplicateName, $"{kindName} '{name}' is already declared in this scope.");
            return null;
        }

        var constraints = interfaceDeclaration.ColonTypeListClause?.Types
            .Select(t => t is TypeName c ? LookupSymbol(c.Name.Text, SymbolKind.Interface) : null)
            .OfType<InterfaceSymbol>()
            .ToList();

        var finalSymbol = new InterfaceSymbol(interfaceDeclaration, name, isSealed, constraints);
        DeclareSymbol(finalSymbol);
        return finalSymbol;
    }

    private AttributeSymbol DeclareAttribute(Attribute attribute)
    {
        var name = attribute.Expression.Tokens.Last(t => t.Kind == SyntaxKind.Identifier).Text;
        var declarationSymbol = LookupSymbol(name, SymbolKind.Function);
        return new AttributeSymbol(attribute, name) { IsIntrinsic = declarationSymbol?.IsIntrinsic ?? false };
    }

    private bool DeclareVariable(NamedDeclaration node, SymbolKind symbolKind, bool isMutable = false) =>
        DeclareVariable(node, node.Name.Text, symbolKind, isMutable);

    private bool DeclareVariable(Node node, string name, SymbolKind symbolKind, bool isMutable = false) =>
        DeclareVariable(node, new Symbol(node, symbolKind, name, isMutable));

    private bool DeclareVariable(Node node, Symbol symbol)
    {
        if (HasDuplicateSymbol(node, symbol.Name, true, $"Variable '{symbol.Name}' is already declared in this scope."))
            return true;

        DeclareSymbol(symbol);
        return true;
    }

    private bool DeclareType(NamedDeclaration node, SymbolKind symbolKind = SymbolKind.Type)
    {
        var name = node.Name.Text;
        if (HasDuplicateSymbol(node, false, $"Type '{name}' is already declared in this scope."))
            return true;

        var symbol = new Symbol(node, symbolKind, name);
        DeclareSymbol(symbol);
        return true;
    }

    private bool HasDuplicateSymbol(NamedDeclaration node, bool isVariable, string error) => HasDuplicateSymbol(node, node.Name.Text, isVariable, error);

    private bool HasDuplicateSymbol(Node node, string name, bool isVariable, string error)
    {
        var scope = CurrentScope();
        var lookup = isVariable ? scope.VariableLookup : scope.TypeLookup;
        if (!lookup.TryGetValue(name, out var existing) || IsAlreadyHoisted(node, existing))
            return false;

        _diagnostics.Error(node, InternalCodes.DuplicateName, error);
        return true;
    }

    private void DeclareSymbol(Symbol symbol)
    {
        AddToLookup(symbol);
        AddDeclaration(symbol);
        if (LuauFactory.Keywords.Contains(symbol.Name))
        {
            _diagnostics.Error(
                symbol.Declaration,
                InternalCodes.ReservedLuauKeyword,
                $"'{symbol.Name}' is a reserved Luau keyword and cannot be used as a declaration name."
            );
        }

        if (IsDeclarationFile())
            symbol.IsGlobal = true;

        if (_context == ResolverContext.Ambient)
            symbol.IsAmbient = true;

        if (parserResult.Tree.File.IsIntrinsic)
            symbol.IsIntrinsic = true;

        if (_semanticModel.EmitDebugDiagnostics)
            _diagnostics.Debug(symbol.Declaration, DescribeDeclaration(symbol));
    }

    private static string DescribeDeclaration(Symbol symbol)
    {
        var flags = new List<string>();
        if (symbol.IsGlobal) flags.Add("global");
        if (symbol.IsAmbient) flags.Add("ambient");
        if (symbol.IsIntrinsic) flags.Add("intrinsic");

        var suffix = flags.Count > 0 ? $" [{string.Join(", ", flags)}]" : "";
        return $"Declared '{symbol.Name}' ({symbol.Kind}){suffix}";
    }

    private void AddToLookup(Symbol symbol)
    {
        var scope = CurrentScope();
        var lookup = GetLookup(symbol.Kind, scope);
        if (!lookup.ContainsKey(symbol.Name))
            lookup[symbol.Name] = [];

        lookup[symbol.Name].Add(symbol);
    }

    private void AddDeclaration(Symbol symbol)
    {
        var id = symbol.Declaration.Id;
        if (!_allDeclarations.ContainsKey(id))
            _allDeclarations[id] = [];

        _allDeclarations[id].Add(symbol);
    }

    private void AddReference(Node node, Symbol symbol)
    {
        if (!_allReferences.ContainsKey(node.Id))
            _allReferences[node.Id] = [];

        _allReferences[node.Id].Add(symbol);
        if (!node.File.IsIntrinsic)
            _semanticModel.NonIntrinsicReferenceNodes.Add(node.Id);
    }

    private Symbol? LookupTypeSymbol(string name) =>
        LookupSymbol(name, SymbolKind.Type)
        ?? LookupSymbol(name, SymbolKind.EnumType)
        ?? LookupSymbol(name, SymbolKind.Trait)
        ?? LookupSymbol(name, SymbolKind.Interface);

    private Symbol? LookupValueSymbol(string name) =>
        LookupSymbol(name, SymbolKind.Variable)
        ?? LookupSymbol(name, SymbolKind.InjectedPropertyVariable)
        ?? LookupSymbol(name, SymbolKind.Function)
        ?? LookupSymbol(name, SymbolKind.Parameter);

    private Symbol? LookupSymbol(string name, SymbolKind kind)
    {
        var lookups = _scopes.Select(scope => GetLookup(kind, scope));
        foreach (var lookup in lookups)
        {
            if (!lookup.TryGetValue(name, out var symbols)) continue;
            return symbols.First();
        }

        return null;
    }

    private Symbol? LookupSymbolCurrentScope(string name, SymbolKind kind)
    {
        var lookup = GetLookup(kind, CurrentScope());
        return !lookup.TryGetValue(name, out var symbols) ? null : symbols.First();
    }

    private static bool IsAlreadyHoisted(Node node, List<Symbol> symbolsForName) => IsAlreadyHoisted<Symbol>(node, symbolsForName, out _);

    private static bool IsAlreadyHoisted<T>(Node node, List<Symbol> symbolsForName, [MaybeNullWhen(false)] out T hoistedSymbol)
        where T : Symbol
    {
        hoistedSymbol = symbolsForName.OfType<T>().FirstOrDefault(s => s.Declaration == node);
        return hoistedSymbol != null;
    }

    private static SymbolLookup GetLookup(SymbolKind kind, ResolverScope scope) => Symbol.IsTypeKind(kind) ? scope.TypeLookup : scope.VariableLookup;

    private bool ReportNonInterfaceConstraint(TypeExpression constraint)
    {
        _diagnostics.Error(constraint, InternalCodes.NonInterfaceConstraint, "Interfaces may only be constrained by other interfaces.");
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