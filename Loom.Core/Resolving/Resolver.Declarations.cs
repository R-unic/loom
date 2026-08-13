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

        // 'async' is the signature saying this yields, and [no_yield] is the signature saying it must not.
        // One of the two was meant, and nothing here can say which.
        if (functionDeclaration.AsyncKeyword != null && NoYieldContext(functionDeclaration) != null)
            _diagnostics.Error(
                functionDeclaration.AsyncKeyword,
                InternalCodes.YieldInNoYieldContext,
                $"'{name}' is both 'async' and '[no_yield]'.",
                "an async function yields by definition - drop whichever of the two was not meant"
            );

        ResolveFunctionBody(functionDeclaration, () => base.VisitFunctionDeclaration(functionDeclaration));
        return true;
    }

    public override bool VisitFunctionExpression(FunctionExpression functionExpression)
    {
        ResolveFunctionBody(functionExpression, () => base.VisitFunctionExpression(functionExpression));
        return true;
    }

    /// <summary>
    ///     Resolves a function's parameters and body in a scope of their own, inside
    ///     <see cref="ResolverContext.Function" />. A declaration and an expression differ only in whether
    ///     the name was declared first, so everything after that is shared.
    /// </summary>
    private void ResolveFunctionBody<T>(T functionLike, Action resolveChildren)
        where T : Node, IFunctionLike
    {
        using var _ = InScope();
        using var __ = InContext(ResolverContext.Function);

        if (functionLike.Body is Block { Statements: [Return] })
            _diagnostics.Warn(functionLike, InternalCodes.RedundantCode, "Use expression body.");

        resolveChildren();
    }

    public override bool VisitTypeAlias(TypeAlias typeAlias)
    {
        if (!DeclareType(typeAlias))
            return false;

        using var _ = InScope();
        base.VisitTypeAlias(typeAlias);

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

        using var _ = InScope();
        base.VisitDeclareFunctionSignature(declareFunctionSignature);

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
        using var _ = InScope();
        base.VisitFunctionType(functionType);

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

        using var _ = InScope();
        base.VisitEventDeclaration(eventDeclaration);

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

    /// <summary>
    ///     A binder is in scope for the branch its pattern chose, and nowhere else - so the target and the
    ///     'then' branch share a scope the 'else' branch is outside of. Two arms of the same match may
    ///     therefore reuse a name, and reusing one within a single arm is the duplicate-name error.
    /// </summary>
    public override bool VisitConditionalType(ConditionalType conditionalType)
    {
        var resolved = Visit(conditionalType.CheckType);
        using (var _ = InScope())
        {
            resolved &= DeclarePatternBinders(conditionalType.TargetType);
            resolved &= Visit(conditionalType.TargetType);
            resolved &= Visit(conditionalType.ThenType);
        }

        return Visit(conditionalType.ElseType) && resolved;
    }

    public override bool VisitTypeMatch(TypeMatch typeMatch)
    {
        var resolved = Visit(typeMatch.Subject);
        foreach (var arm in typeMatch.Arms)
            resolved &= Visit(arm);

        return resolved;
    }

    public override bool VisitTypeMatchArm(TypeMatchArm typeMatchArm)
    {
        using var _ = InScope();
        return DeclarePatternBinders(typeMatchArm.Pattern) & Visit(typeMatchArm.Pattern) & Visit(typeMatchArm.Result);
    }

    /// <summary>
    ///     Every <c>let</c> the pattern declares, put in the arm's own scope ahead of the pattern being
    ///     walked.
    /// </summary>
    /// <remarks>
    ///     Declaring one where it is written would put it in whatever scope it happens to sit in -
    ///     <see cref="VisitFunctionType" /> opens one of its own - and that scope is gone by the time the
    ///     arm's result is resolved. A binder belongs to the arm, wherever inside the pattern it appears.
    /// </remarks>
    private bool DeclarePatternBinders(TypeExpression pattern)
    {
        var declared = true;
        foreach (var binder in pattern.EnumerateDescendants<InferType>().Prepend(pattern as InferType).OfType<InferType>())
            declared &= DeclareType(binder, binder.Name.Text);

        return declared;
    }

    public override bool VisitMappedTypeDeclaration(MappedTypeDeclaration mappedTypeDeclaration)
    {
        // The source keys are read in the interface's own scope - the binder does not exist yet there, and
        // 'keyof(K)' naming the binder it is about to declare would otherwise resolve to itself.
        var resolved = Visit(mappedTypeDeclaration.SourceType);

        using var _ = InScope();
        return DeclareType(mappedTypeDeclaration, mappedTypeDeclaration.Name.Text)
            && Visit(mappedTypeDeclaration.ColonTypeClause)
            && resolved;
    }
}
