using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving.Symbols;
using Loom.Core.TypeChecking.Types;

namespace Loom.Core.TypeChecking;

using Type = Types.Type;

public sealed partial class TypeChecker
{
    public override Type VisitFunctionDeclaration(FunctionDeclaration functionDeclaration)
    {
        MaybeVisit(functionDeclaration.Attributes);

        var typeParameters = functionDeclaration.TypeParameters?.ParameterList.ConvertAll(VisitTypeParameter) ?? [];
        var parameterTypes = functionDeclaration.Parameters?.ParameterList.ConvertAll(Visit) ?? [];
        MaybeVisit(functionDeclaration.ReturnType);

        var returnType = GetReturnType(functionDeclaration);
        var functionType = BindType(
            functionDeclaration,
            new Types.FunctionType(typeParameters, parameterTypes, returnType, HasRestParameter(functionDeclaration.Parameters))
        );

        if (functionDeclaration.Body is ExpressionBody body)
        {
            if (functionDeclaration.ReturnType != null)
                Check(body.Expression, returnType);

            // Else... GetReturnType had already visited expression body
        }
        else
        {
            Visit(functionDeclaration.Body);
        }

        if (functionDeclaration.Attributes != null)
            foreach (var attribute in functionDeclaration.Attributes.AttributeList)
            {
                if (IsMetadataOnlyDecorator(attribute))
                    CheckPassiveDecorator(attribute);
                else
                    CheckDecoratorAttribute(attribute, functionDeclaration.Name.Text, functionType.ReturnType);

                CheckAttributeUsage(attribute, AttributeTargetsFlag.Function);
            }

        return functionType;
    }

    public override Type VisitFunctionExpression(FunctionExpression functionExpression)
    {
        var typeParameters = functionExpression.TypeParameters?.ParameterList.ConvertAll(VisitTypeParameter) ?? [];
        var parameterTypes = functionExpression.Parameters?.ParameterList.ConvertAll(Visit) ?? [];
        MaybeVisit(functionExpression.ReturnType);

        var returnType = GetReturnType(functionExpression);
        var functionType = BindType(
            functionExpression,
            new Types.FunctionType(typeParameters, parameterTypes, returnType, HasRestParameter(functionExpression.Parameters))
        );

        if (functionExpression.Body is ExpressionBody body)
        {
            if (functionExpression.ReturnType != null)
                Check(body.Expression, returnType);
        }
        else
        {
            Visit(functionExpression.Body);
        }

        return functionType;
    }

    public override Type VisitTypeAlias(TypeAlias typeAlias)
    {
        if (typeAlias.TypeParameters == null)
        {
            var type = Visit(typeAlias.EqualsTypeClause);
            _semanticModel.TypeSolver.CheckCircular(ref type, typeAlias.Name);
            return BindType(typeAlias, TypeSimplifier.Simplify(type));
        }

        // Published before its body is resolved, because the body may reach back to the alias: with
        // 'type R<T, E> = A<T, E> | B<T, E>', resolving A's members references R, which resolves B,
        // whose members reference R again. Binding first means that innermost reference finds this
        // instance instead of an unbound type variable that would settle as 'never'.
        var parameters = typeAlias.TypeParameters.ParameterList.ConvertAll(VisitTypeParameter);
        var genericType = new GenericType(typeAlias, parameters, Types.PrimitiveType.Never);
        BindType(typeAlias, genericType);

        var underlyingType = Visit(typeAlias.EqualsTypeClause);
        _semanticModel.TypeSolver.CheckCircular(ref underlyingType, typeAlias.Name);
        genericType.UnderlyingType = TypeSimplifier.Simplify(underlyingType);

        return BindType(typeAlias, VarianceInferrer.ApplyInferredVariance(genericType));
    }

    public override Type VisitVariableDeclaration(VariableDeclaration variableDeclaration)
    {
        Type? declaredType = null;
        if (variableDeclaration.ColonTypeClause != null)
            declaredType = Visit(variableDeclaration.ColonTypeClause);

        var initializerType = variableDeclaration.EqualsValueClause != null
            ? declaredType != null
                ? Check(variableDeclaration.EqualsValueClause.Value, declaredType, _flowState)
                : Visit(variableDeclaration.EqualsValueClause)
            : null;

        Type finalType;
        if (declaredType != null)
            finalType = declaredType;
        else if (initializerType != null)
            finalType = initializerType;
        else
            finalType = Types.PrimitiveType.Unknown;

        if (variableDeclaration.Keyword.Kind == Text.SyntaxKind.MutKeyword)
            finalType = finalType.Widen();

        return BindType(variableDeclaration, TypeSimplifier.Simplify(finalType));
    }

    public override Type VisitDestructuringDeclaration(DestructuringDeclaration destructuringDeclaration)
    {
        Type? declaredType = null;
        if (destructuringDeclaration.ColonTypeClause != null)
            declaredType = Visit(destructuringDeclaration.ColonTypeClause);

        var initializerType = destructuringDeclaration.EqualsValueClause != null
            ? declaredType != null
                ? Check(destructuringDeclaration.EqualsValueClause.Value, declaredType, _flowState)
                : Visit(destructuringDeclaration.EqualsValueClause)
            : null;

        var subjectType = declaredType ?? initializerType ?? Types.PrimitiveType.Unknown;
        switch (destructuringDeclaration.Target)
        {
            case ArrayDestructuringTarget arrayTarget:
                CheckArrayDestructuringTarget(arrayTarget, subjectType);
                break;
            case ObjectDestructuringTarget objectTarget:
                CheckObjectDestructuringTarget(objectTarget, subjectType);
                break;
            case TupleDestructuringTarget tupleTarget:
                CheckTupleDestructuringTarget(tupleTarget, subjectType);
                break;
        }

        return BindType(destructuringDeclaration, Types.PrimitiveType.Void);
    }

    private void CheckTupleDestructuringTarget(TupleDestructuringTarget target, Type subjectType)
    {
        if (subjectType is not Types.TupleType tupleType)
        {
            if (Type.IsNotUnknown(subjectType) && Type.IsNotNever(subjectType))
                _diagnostics.Error(
                    target,
                    InternalCodes.InvalidDestructureSource,
                    $"Cannot destructure value of type '{subjectType}' with a tuple pattern."
                );

            foreach (var element in target.Elements)
                BindType(element, Types.PrimitiveType.Unknown);

            return;
        }

        if (target.Elements.Count != tupleType.ElementTypes.Count)
        {
            _diagnostics.Error(
                target,
                InternalCodes.TupleArityMismatch,
                $"Tuple type '{tupleType}' expects {tupleType.ElementTypes.Count} element(s), but {target.Elements.Count} were provided."
            );

            foreach (var element in target.Elements)
                BindType(element, Types.PrimitiveType.Unknown);

            return;
        }

        for (var i = 0; i < target.Elements.Count; i++)
            BindType(target.Elements[i], tupleType.ElementTypes[i]);
    }

    private void CheckArrayDestructuringTarget(ArrayDestructuringTarget target, Type subjectType)
    {
        var elementType = GetArrayElementType(subjectType);
        if (elementType == null)
        {
            if (Type.IsNotUnknown(subjectType) && Type.IsNotNever(subjectType))
                _diagnostics.Error(
                    target,
                    InternalCodes.InvalidDestructureSource,
                    $"Cannot destructure value of type '{subjectType}' with an array pattern."
                );

            elementType = Types.PrimitiveType.Unknown;
        }

        foreach (var element in target.Elements)
            BindType(element, elementType);
    }

    private void CheckObjectDestructuringTarget(ObjectDestructuringTarget target, Type subjectType)
    {
        foreach (var field in target.Fields)
        {
            var propertyType = TypeSimplifier.GetMemberPropertyType(subjectType, field.Name.Text);
            if (propertyType == null)
            {
                if (Type.IsNotUnknown(subjectType) && Type.IsNotNever(subjectType))
                    _diagnostics.Error(
                        field,
                        InternalCodes.UnknownDestructureProperty,
                        $"Property '{field.Name.Text}' does not exist on type '{subjectType}'."
                    );

                propertyType = Types.PrimitiveType.Unknown;
            }

            BindType(field, propertyType);
        }
    }

    /// <remarks>
    ///     Imported declarations are typed by the module that exports them, and the base implementation would
    ///     return the type of the module path string — which would become the file's type when a module ends
    ///     with an import.
    /// </remarks>
    public override Type VisitImportDeclaration(ImportDeclaration import) => BindType(import, Types.PrimitiveType.Void);

    /// <remarks>The resolver already bound the namespace's object type; re-binding here would discard it.</remarks>
    public override Type VisitNamespaceImport(NamespaceImport import) => _semanticModel.GetType(import);

    public override Type VisitDeclare(Declare declare)
    {
        var type = declare.Signature switch
        {
            InterfaceDeclaration interfaceDeclaration => Visit(interfaceDeclaration),
            DeclareVariableSignature variableSignature => Visit(variableSignature.ColonTypeClause!),
            DeclareFunctionSignature functionSignature => Visit(functionSignature),
            EventDeclaration eventDeclaration => Visit(eventDeclaration),
            _ => Types.PrimitiveType.Never
        };

        BindType(declare.Signature, type);
        return BindType(declare, type);
    }

    public override Type VisitDeclareFunctionSignature(DeclareFunctionSignature declareFunctionSignature)
    {
        MaybeVisit(declareFunctionSignature.Attributes);
        return new Types.FunctionType(
            declareFunctionSignature.TypeParameters?.ParameterList.ConvertAll(VisitTypeParameter) ?? [],
            declareFunctionSignature.Parameters?.ParameterList.ConvertAll(Visit) ?? [],
            Visit(declareFunctionSignature.ReturnType),
            HasRestParameter(declareFunctionSignature.Parameters)
        );
    }

    private static bool HasRestParameter(Parameters? parameters) => parameters?.ParameterList is [.., { DotDot: not null }];

    public override Type VisitParameter(Parameter parameter)
    {
        var declaredType = MaybeVisit(parameter.ColonTypeClause);
        var initializerType = MaybeVisit(parameter.EqualsValueClause);
        if (declaredType != null && parameter.EqualsValueClause != null)
            _semanticModel.TypeSolver.AddConstraint(initializerType!, declaredType, parameter.EqualsValueClause.Value);

        if (parameter.DotDot != null && declaredType != null && !IsArrayType(declaredType) && !IsTupleRestType(declaredType))
            _diagnostics.Error(
                parameter,
                InternalCodes.InvalidRestParameterType,
                $"Rest parameter '{parameter.Name.Text}' must have an array type, but got '{declaredType}'."
            );

        return BindType(parameter, declaredType ?? initializerType ?? Types.PrimitiveType.Unknown);
    }

    private static bool IsArrayType(Type type) => (type is InstantiatedType instantiated ? instantiated.Expand() : type) is Types.ArrayType;

    private static bool IsTupleRestType(Type type) =>
        (type is InstantiatedType instantiated ? instantiated.Expand() : type) switch
        {
            Types.TupleType => true,
            Types.TypeParameter { Constraint: TupleMarkerType } => true,
            _ => false
        };

    private Type GetReturnType(IFunctionLike functionLike)
    {
        if (functionLike.ReturnType != null)
            return Visit(functionLike.ReturnType);

        var possibleReturnTypes = functionLike.Body is ExpressionBody body
            ? [Visit(body)]
            : GetOwnReturnStatements(functionLike.Body).Select(Visit).ToList();

        return possibleReturnTypes.Count == 0
            ? Types.PrimitiveType.Void
            : TypeSimplifier.Simplify(new Types.UnionType(possibleReturnTypes));
    }

    private static List<Return> GetOwnReturnStatements(Node node)
    {
        var result = new List<Return>();
        CollectOwnReturnStatements(node, result);
        return result;
    }

    private static void CollectOwnReturnStatements(Node node, List<Return> result)
    {
        foreach (var child in node.Children)
        {
            if (child is Return returnStatement)
                result.Add(returnStatement);

            if (child is not (FunctionDeclaration or FunctionExpression))
                CollectOwnReturnStatements(child, result);
        }
    }

    private Type ResolveHoistedType(Symbol symbol)
    {
        if (!_resolvingHoisted.Add(symbol))
            return _semanticModel.GetType(symbol.Declaration);

        try
        {
            var type = GetTypeFromSymbol(symbol);
            return type is TypeVariable ? Visit(symbol.Declaration) : type;
        }
        finally
        {
            _resolvingHoisted.Remove(symbol);
        }
    }

    private Type GetTypeFromSymbol(Symbol symbol) =>
        symbol is { IsGlobal: true, IsIntrinsic: false } && symbol.File.AbsolutePath != _semanticModel.Tree.File.AbsolutePath
            ? Visit(symbol.Declaration)
            : _semanticModel.GetType(symbol.Declaration);
}
