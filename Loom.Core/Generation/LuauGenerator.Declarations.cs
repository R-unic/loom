using Loom.Core.Parsing.AST;
using Loom.Core.Text;
using Loom.Core.TypeChecking.Types;
using Loom.Luau;
using Loom.Luau.AST;
using ArrayType = Loom.Core.Parsing.AST.ArrayType;
using Attribute = Loom.Core.Parsing.AST.Attribute;
using Identifier = Loom.Luau.AST.Identifier;
using LiteralType = Loom.Core.TypeChecking.Types.LiteralType;
using OptionalType = Loom.Luau.AST.OptionalType;
using Parameter = Loom.Core.Parsing.AST.Parameter;
using PrimitiveType = Loom.Core.TypeChecking.Types.PrimitiveType;
using TypeAlias = Loom.Core.Parsing.AST.TypeAlias;
using TypeParameters = Loom.Luau.AST.TypeParameters;
using UnionType = Loom.Core.TypeChecking.Types.UnionType;

namespace Loom.Core.Generation;

public sealed partial class LuauGenerator
{
    public override LuauNode VisitDeclare(Declare declare) => declare.Signature is InterfaceDeclaration ? Visit(declare.Signature) : new NoOpStatement();
    public override LuauNode VisitImportDeclaration(ImportDeclaration import) => new NoOpStatement();
    public override LuauNode VisitNamespaceImport(NamespaceImport import) => new NoOpStatement();
    public override LuauNode VisitExportList(ExportList export) => new NoOpStatement();
    public override LuauNode VisitExportAll(ExportAll export) => new NoOpStatement();

    public override LuauNode VisitExportDeclaration(ExportDeclaration export)
    {
        var generated = Visit(export.Declaration);
        if (generated is Luau.AST.TypeAlias typeAlias)
            typeAlias.IsExported = true;

        return generated;
    }

    public override LuauNode VisitVariableDeclaration(VariableDeclaration variableDeclaration)
    {
        var isConst = variableDeclaration.Keyword is { Kind: SyntaxKind.LetKeyword };
        var name = variableDeclaration.Name.Text;
        var type = variableDeclaration.ColonTypeClause != null ? Visit(variableDeclaration.ColonTypeClause.Type) : null;
        var initializer = variableDeclaration.EqualsValueClause != null ? Visit(variableDeclaration.EqualsValueClause.Value) : null;
        if (initializer != null)
            initializer = LuauFactory.UnwrapParentheses(initializer);

        return isConst
            ? new ConstVariable(name, type, initializer ?? new NilLiteral())
            : new LocalVariable(name, type, initializer);
    }
    
    public override LuauNode VisitFunctionDeclaration(FunctionDeclaration functionDeclaration)
    {
        var typeParameters = MaybeVisit<TypeParameters>(functionDeclaration.TypeParameters);
        if (typeParameters != null)
            foreach (var typeParameter in typeParameters.Parameters)
                typeParameter.OfFunction = true;

        var parameters = functionDeclaration.Parameters?.ParameterList.ConvertAll(VisitParameter) ?? [];
        var returnType = MaybeVisit<LuauType>(functionDeclaration.ReturnType);
        var statements = GenerateFunctionBody(functionDeclaration);
        if (functionDeclaration.Parameters?.ParameterList is [.., { DotDot: not null } restParameter])
            statements.Statements.Insert(0, GenerateRestParameterBinding(restParameter));

        if (functionDeclaration.Attributes == null || !HasDecoratorAttributes(functionDeclaration.Attributes))
            return new Function(functionDeclaration.Name.Text, typeParameters, parameters, returnType, statements);

        return GenerateDecoratedFunction(functionDeclaration, typeParameters, parameters, returnType, statements);
    }

    private Function GenerateDecoratedFunction(
        FunctionDeclaration functionDeclaration,
        TypeParameters? typeParameters,
        List<Luau.AST.Parameter> parameters,
        LuauType? returnType,
        Chunk statements)
    {
        var call = ApplyDecorators(functionDeclaration.Attributes!, statements, functionDeclaration.Name.Text);
        return new Function(functionDeclaration.Name.Text, typeParameters, parameters, returnType, new Chunk([new Luau.AST.Return(call)]));
    }

    // Each decorator (and, for a factory like `log("info")`, the factory invocation that produces it) must
    // run exactly once, at the point the function is declared - not on every call. So the attribute
    // expression is hoisted into a local ahead of the function statement (PushToVariable no-ops for a
    // bare decorator reference like `[log]`, since re-reading a name costs nothing to redo per call) and
    // only that cached reference is embedded in the wrapper body that actually runs per-call.
    private LuauExpression ApplyDecorators(Attributes attributes, Chunk originalBody, string name)
    {
        var wrappingAttributes = attributes.AttributeList.Where(a => !IsIntrinsicAttribute(a) && !IsMetadataOnlyDecorator(a)).ToList();
        var decorators = wrappingAttributes.ConvertAll(attribute => _state.PushToVariable($"_{name}_decorator", Visit<LuauExpression>(attribute)));

        LuauExpression value = new Call(decorators[0], [new AnonymousFunction(null, [], null, originalBody), new StringLiteral(name)]);
        foreach (var decorator in decorators.Skip(1))
        {
            var thunk = new AnonymousFunction(null, [], null, new Chunk([new Luau.AST.Return(value)]));
            value = new Call(decorator, [thunk, new StringLiteral(name)]);
        }

        return value;
    }

    private bool HasDecoratorAttributes(Attributes attributes) => attributes.AttributeList.Exists(a => !IsIntrinsicAttribute(a) && !IsMetadataOnlyDecorator(a));

    private bool IsIntrinsicAttribute(Attribute attribute) => _semanticModel.GetSymbol(attribute.Expression)?.IsIntrinsic == true;

    private bool IsMetadataOnlyDecorator(Attribute attribute) =>
        _semanticModel.GetSymbol(attribute.Expression)?.Declaration is IWithAttributes { Attributes: { } declaredAttributes }
        && declaredAttributes.AttributeList.Exists(a => a.Name == "metadata_only");

    public override LuauNode VisitFunctionExpression(FunctionExpression functionExpression)
    {
        var typeParameters = MaybeVisit<TypeParameters>(functionExpression.TypeParameters);
        if (typeParameters != null)
            foreach (var typeParameter in typeParameters.Parameters)
                typeParameter.OfFunction = true;

        var parameters = functionExpression.Parameters?.ParameterList.ConvertAll(VisitParameter) ?? [];
        var returnType = MaybeVisit<LuauType>(functionExpression.ReturnType);
        var statements = GenerateFunctionBody(functionExpression);
        if (functionExpression.Parameters?.ParameterList is [.., { DotDot: not null } restParameter])
            statements.Statements.Insert(0, GenerateRestParameterBinding(restParameter));

        return new AnonymousFunction(typeParameters, parameters, returnType, statements);
    }

    private static LocalVariable GenerateRestParameterBinding(Parameter restParameter) =>
        new(restParameter.Name.Text, null, new Table([new TableInitializer(new Identifier("..."))]));

    public override Luau.AST.Parameter VisitParameter(Parameter parameter)
    {
        if (parameter.DotDot != null)
        {
            var elementType = parameter.ColonTypeClause?.Type is ArrayType arrayType
                ? MaybeVisit<LuauType>(arrayType.ElementType)
                : null;

            return Luau.AST.Parameter.Vararg(elementType);
        }

        var type = MaybeVisit<LuauType>(parameter.ColonTypeClause?.Type);
        if (type != null && parameter.EqualsValueClause != null && type is not OptionalType)
            type = new OptionalType(type);

        return new Luau.AST.Parameter(parameter.Name.Text, type);
    }

    public override LuauNode VisitTypeAlias(TypeAlias typeAlias)
    {
        var typeParameters = typeAlias.TypeParameters != null
            ? Visit<TypeParameters>(typeAlias.TypeParameters)
            : new TypeParameters();

        // A branching alias still waiting on its subject has no Luau body: every use of it emits what it
        // worked out to instead (see LuauGenerator.Types.cs), so the alias is only still here to keep an
        // export of the name valid. One whose subject was already concrete resolved to an ordinary type and
        // is emitted as one.
        var type = IsUnresolvedConditional(typeAlias) ? Luau.AST.PrimitiveType.Unknown : Visit(typeAlias.EqualsTypeClause.Type);

        return new Luau.AST.TypeAlias(typeAlias.Name.Text, typeParameters, type);
    }

    public override LuauNode VisitEnumDeclaration(EnumDeclaration enumDeclaration)
    {
        if (_semanticModel.GetType(enumDeclaration) is not ObjectType objectType)
            return LuauFactory.EmptyVariable();

        var propertyUnion = objectType.PropertyUnion();
        return new Luau.AST.TypeAlias(
            enumDeclaration.Name.Text,
            new TypeParameters(),
            propertyUnion switch
            {
                UnionType union =>
                    union.Types.Any(t => t.IsAssignableTo(PrimitiveType.Number))
                        ? Luau.AST.PrimitiveType.Number
                        : new Luau.AST.UnionType(
                            union.Types.ConvertAll(t => t is LiteralType { Value: string s }
                                    ? new StringLiteralType(s)
                                    : Luau.AST.PrimitiveType.Number
                                )
                                .OfType<LuauType>()
                                .ToList()
                        ),
                LiteralType { Value: string s } => new StringLiteralType(s),
                _ => Luau.AST.PrimitiveType.Number
            }
        );
    }
}