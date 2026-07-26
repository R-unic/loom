using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Core.Resolving.Symbols;
using Loom.Core.TypeChecking.Types;
using FunctionType = Loom.Core.Parsing.AST.FunctionType;

namespace Loom.Core.TypeChecking;

using Type = Types.Type;

public sealed partial class TypeChecker
{
    public override Type VisitFunctionDeclaration(FunctionDeclaration functionDeclaration)
    {
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

        var parameters = typeAlias.TypeParameters.ParameterList.ConvertAll(VisitTypeParameter);
        var underlyingType = Visit(typeAlias.EqualsTypeClause);
        _semanticModel.TypeSolver.CheckCircular(ref underlyingType, typeAlias.Name);

        var genericType = VarianceInferrer.ApplyInferredVariance(new GenericType(typeAlias, parameters, TypeSimplifier.Simplify(underlyingType)));
        return BindType(typeAlias, genericType);
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
            _ => Types.PrimitiveType.Never
        };

        BindType(declare.Signature, type);
        return BindType(declare, type);
    }

    public override Type VisitDeclareFunctionSignature(DeclareFunctionSignature declareFunctionSignature) =>
        new Types.FunctionType(
            declareFunctionSignature.TypeParameters?.ParameterList.ConvertAll(VisitTypeParameter) ?? [],
            declareFunctionSignature.Parameters?.ParameterList.ConvertAll(Visit) ?? [],
            Visit(declareFunctionSignature.ReturnType),
            HasRestParameter(declareFunctionSignature.Parameters)
        );

    private static bool HasRestParameter(Parameters? parameters) => parameters?.ParameterList is [.., { DotDot: not null }];

    public override Type VisitParameter(Parameter parameter)
    {
        var declaredType = MaybeVisit(parameter.ColonTypeClause);
        var initializerType = MaybeVisit(parameter.EqualsValueClause);
        if (declaredType != null && parameter.EqualsValueClause != null)
            _semanticModel.TypeSolver.AddConstraint(initializerType!, declaredType, parameter.EqualsValueClause.Value);

        if (parameter.DotDot != null && declaredType != null && !IsArrayType(declaredType))
            _diagnostics.Error(
                parameter,
                InternalCodes.InvalidRestParameterType,
                $"Rest parameter '{parameter.Name.Text}' must have an array type, but got '{declaredType}'."
            );

        return BindType(parameter, declaredType ?? initializerType!);
    }

    private static bool IsArrayType(Type type) => (type is InstantiatedType instantiated ? instantiated.Expand() : type) is Types.ArrayType;

    private Type GetReturnType(FunctionDeclaration functionDeclaration)
    {
        if (functionDeclaration.ReturnType != null)
            return Visit(functionDeclaration.ReturnType);

        var possibleReturnTypes = functionDeclaration.Body is ExpressionBody body
            ? [Visit(body)]
            : GetOwnReturnStatements(functionDeclaration.Body).Select(Visit).ToList();

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

            if (child is not FunctionDeclaration)
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
