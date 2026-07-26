using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Text;

namespace Loom.Core.Resolving;

public sealed partial class Resolver
{
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
}
