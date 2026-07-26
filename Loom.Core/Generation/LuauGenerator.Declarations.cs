using Loom.Core.Parsing.AST;
using Loom.Core.Text;
using Loom.Core.TypeChecking.Types;
using Loom.Luau;
using Loom.Luau.AST;
using ArrayType = Loom.Core.Parsing.AST.ArrayType;
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
            ? new ConstVariable(name, type, initializer!)
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

        return new Function(functionDeclaration.Name.Text, typeParameters, parameters, returnType, statements);
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

        var type = Visit(typeAlias.EqualsTypeClause.Type);
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