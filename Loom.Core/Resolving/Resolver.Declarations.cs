using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving.Symbols;
using Loom.Core.Text;

namespace Loom.Core.Resolving;

public sealed partial class Resolver
{
    public override bool VisitFunctionDeclaration(FunctionDeclaration functionDeclaration)
    {
        var name = functionDeclaration.Name.Text;
        if (!DeclareVariable(functionDeclaration, new FunctionSymbol(functionDeclaration, name)))
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

    public override bool VisitFunctionExpression(FunctionExpression functionExpression)
    {
        PushScope();
        var lastContext = _context;
        _context = ResolverContext.Function;
        if (functionExpression.Body is Block { Statements: [Return] })
            _diagnostics.Warn(functionExpression, InternalCodes.RedundantCode, "Use expression body.");

        base.VisitFunctionExpression(functionExpression);
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
        if (!DeclareVariable(variableDeclaration, isMutable))
            return false;

        base.VisitVariableDeclaration(variableDeclaration);
        if (variableDeclaration.EqualsValueClause != null || isMutable)
            return true;

        _diagnostics.Error(variableDeclaration, InternalCodes.MustHaveInitializer, "Immutable declarations must be initialized.");
        return false;
    }

    public override bool VisitDestructuringDeclaration(DestructuringDeclaration destructuringDeclaration)
    {
        if (destructuringDeclaration.Keyword.Kind == SyntaxKind.MutKeyword)
            _diagnostics.Error(
                destructuringDeclaration,
                InternalCodes.InvalidDestructureTarget,
                "Destructuring declarations must use 'let', not 'mut'."
            );

        var declared = destructuringDeclaration.Target switch
        {
            ArrayDestructuringTarget arrayTarget =>
                arrayTarget.Elements.All(element => DeclareVariable(element, element.Name.Text)),
            ObjectDestructuringTarget objectTarget =>
                objectTarget.Fields.All(field => DeclareVariable(field, field.BindingName.Text)),
            TupleDestructuringTarget tupleTarget =>
                tupleTarget.Elements.All(element => DeclareVariable(element, element.Name.Text)),
            _ => true
        };

        if (!declared)
            return false;

        base.VisitDestructuringDeclaration(destructuringDeclaration);
        if (destructuringDeclaration.EqualsValueClause != null)
            return true;

        _diagnostics.Error(destructuringDeclaration, InternalCodes.MustHaveInitializer, "Destructuring declarations must be initialized.");
        return false;
    }

    public override bool VisitDeclareFunctionSignature(DeclareFunctionSignature declareFunctionSignature)
    {
        var attributes = declareFunctionSignature.Attributes?.AttributeList.Select(DeclareAttribute).ToList();
        if (!DeclareVariable(declareFunctionSignature, new FunctionSymbol(declareFunctionSignature, declareFunctionSignature.Name.Text, attributes)))
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
        return DeclareVariable(declareVariableSignature, isMutable) && base.VisitDeclareVariableSignature(declareVariableSignature);
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

        var symbol = new ParameterSymbol(parameter, name);
        DeclareSymbol(symbol);

        if (parameter.EqualsValueClause != null
            || parameter.ColonTypeClause != null
            || parameter.Parent?.Parent?.Parent is ImplementBody
            || IsEventConnectionHandler(parameter)
            || IsContextuallyTypedArgument(parameter))
            return base.VisitParameter(parameter);

        _diagnostics.Error(parameter, InternalCodes.MustHaveDefaultOrType, "Parameter must have a declared type or default value to infer from.");
        return false;
    }

    private static bool IsContextuallyTypedArgument(Parameter parameter)
    {
        if (parameter.Parent?.Parent is not FunctionExpression functionExpression)
            return false;

        Node? node = functionExpression;
        while (node?.Parent is Parenthesized)
            node = node.Parent;

        return node?.Parent is Arguments { Parent: Invocation };
    }

    private static bool IsEventConnectionHandler(Parameter parameter) =>
        parameter.Parent?.Parent is FunctionExpression
        {
            Parent: AssignmentOperator { Operator.Kind: SyntaxKind.PlusEquals or SyntaxKind.MinusEquals } assignment
        } functionExpression
        && assignment.Right == functionExpression;

    public override bool VisitEnumDeclaration(EnumDeclaration enumDeclaration) =>
        DeclareVariable(enumDeclaration)
        && DeclareType(enumDeclaration, new EnumTypeSymbol(enumDeclaration, enumDeclaration.Name.Text))
        && base.VisitEnumDeclaration(enumDeclaration);

    public override bool VisitEventDeclaration(EventDeclaration eventDeclaration)
    {
        // No attributes: an attribute on a module-level event has never meant anything (unlike one on an
        // interface's event member), and recording it here would start feeding it to the generator.
        if (!DeclareVariable(eventDeclaration, new EventSymbol(eventDeclaration)))
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
            _semanticModel.MarkUnresolved(identifier);
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
            _semanticModel.MarkUnresolved(typeName);
            return false;
        }

        base.VisitTypeName(typeName);
        AddReference(typeName, symbol);
        return true;
    }

    public override bool VisitTypeParameter(TypeParameter typeParameter) => DeclareType(typeParameter) && base.VisitTypeParameter(typeParameter);
}
