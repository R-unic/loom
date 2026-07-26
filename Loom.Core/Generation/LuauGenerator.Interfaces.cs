using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving;
using Loom.Luau;
using Loom.Luau.AST;
using BinaryOperator = Loom.Luau.AST.BinaryOperator;
using ExpressionStatement = Loom.Luau.AST.ExpressionStatement;
using FunctionType = Loom.Luau.AST.FunctionType;
using Identifier = Loom.Luau.AST.Identifier;
using IntersectionType = Loom.Luau.AST.IntersectionType;
using Parameter = Loom.Luau.AST.Parameter;
using PropertyAccess = Loom.Luau.AST.PropertyAccess;
using TypeAlias = Loom.Luau.AST.TypeAlias;
using TypeName = Loom.Luau.AST.TypeName;
using TypeParameters = Loom.Luau.AST.TypeParameters;

namespace Loom.Core.Generation;

public sealed partial class LuauGenerator
{
    public override LuauNode VisitImplement(Implement implement)
    {
        var interfaceName = Visit(implement.InterfaceName);
        var metaName = GetImplementationMetaName(implement);
        var identifier = new Identifier(metaName);
        var variable = new LocalVariable(metaName, null, Table.Empty);
        _state.Postreq(new ExpressionStatement(new BinaryOperator(new PropertyAccess(identifier, ["__index"]), "=", identifier)));
        _state.Postreq(new ExpressionStatement(new BinaryOperator(identifier, "=", new TypeCast(identifier, interfaceName))));

        foreach (var declaration in implement.Body.Implementations)
        {
            var typeParameters = MaybeVisit<TypeParameters>(declaration.TypeParameters);
            var parameters = declaration.Parameters?.ParameterList.ConvertAll(Visit<Parameter>) ?? [];
            parameters.Insert(0, new Parameter("self", interfaceName));

            var returnType = MaybeVisit<LuauType>(declaration.ReturnType);
            var body = GenerateFunctionBody(declaration);
            _state.Postreq(
                new Function(
                    $"{metaName}.{declaration.Name.Text}",
                    typeParameters,
                    parameters,
                    returnType,
                    body,
                    false
                )
            );
        }

        return variable;
    }

    public override LuauNode VisitTraitDeclaration(TraitDeclaration traitDeclaration)
    {
        var signatures = traitDeclaration.Body.Members;
        var properties = signatures.ConvertAll(signature =>
            {
                var parameterTypes = signature.Parameters?.ParameterList.FindAll(p => p.ColonTypeClause != null).ConvertAll(p => Visit(p.ColonTypeClause!.Type))
                    ?? [];

                var typeArguments = traitDeclaration.TypeParameters?.ParameterList.ConvertAll(LuauType (p) => new TypeName(p.Name.Text));
                parameterTypes.Insert(0, new TypeName(traitDeclaration.Name.Text, typeArguments));

                return new TableTypeProperty(
                    null,
                    signature.Name.Text,
                    new FunctionType(
                        GenerateTypeParameters(signature.TypeParameters),
                        parameterTypes,
                        Visit(signature.ReturnType)
                    )
                );
            }
        );

        var tableType = new TableType(null, properties);
        var typeParameters = GenerateTypeParameters(traitDeclaration.TypeParameters);
        return new TypeAlias(traitDeclaration.Name.Text, typeParameters, tableType);
    }

    public override LuauNode VisitInterfaceDeclaration(InterfaceDeclaration interfaceDeclaration)
    {
        var symbol = _semanticModel.GetDeclarationSymbol(interfaceDeclaration, SymbolKind.Interface);
        if (symbol is not InterfaceSymbol interfaceSymbol)
            return new NoOpStatement();

        var indexer = interfaceDeclaration.Body?.Members.OfType<IndexerDeclaration>().FirstOrDefault();
        var propertyDeclarations = interfaceDeclaration.Body?.Members.OfType<PropertyDeclaration>() ?? [];
        var eventDeclarations = interfaceDeclaration.Body?.Members.OfType<EventDeclaration>() ?? [];
        var tableIndexer = indexer != null
            ? new TableTypeIndexer(indexer.MutKeyword == null ? LuauVisibility.Read : null, Visit(indexer.IndexType), Visit(indexer.ColonTypeClause))
            : null;

        var typeParameters = GenerateTypeParameters(interfaceDeclaration.TypeParameters);
        var properties = MergeOverloadedPropertyTypes(
                propertyDeclarations.Select(property => GenerateInterfacePropertyType(interfaceDeclaration, property, typeParameters)).ToList()
            )
            .Concat(eventDeclarations.Select(GenerateInterfaceEventType))
            .ToList();

        var tableType = new TableType(tableIndexer, properties);
        return new TypeAlias(
            interfaceDeclaration.Name.Text,
            typeParameters,
            interfaceDeclaration.ColonTypeListClause != null || interfaceSymbol.Implementations.Count > 0
                ? new IntersectionType(
                    [
                        ..interfaceDeclaration.ColonTypeListClause?.Types.ConvertAll(Visit) ?? [],
                        tableType,
                        ..interfaceSymbol.Implementations.ConvertAll(i => Visit(i.TraitName))
                    ]
                )
                : tableType
        );
    }

    // Mirrors TypeChecker.Interfaces.cs's MergeOverloadedProperties: same-named function-typed table properties become one field typed as their intersection.
    private static List<TableTypeProperty> MergeOverloadedPropertyTypes(List<TableTypeProperty> properties)
    {
        var merged = new List<TableTypeProperty>();
        var indexByName = new Dictionary<string, int>();
        foreach (var property in properties)
        {
            if (indexByName.TryGetValue(property.Name, out var index)
                && property.Type is FunctionType
                && merged[index].Type is FunctionType or IntersectionType)
            {
                var existingSignatures = merged[index].Type is IntersectionType existingIntersection ? existingIntersection.Types : [merged[index].Type];
                merged[index] = new TableTypeProperty(merged[index].Visibility, merged[index].Name, new IntersectionType([..existingSignatures, property.Type]));
                continue;
            }

            indexByName[property.Name] = merged.Count;
            merged.Add(property);
        }

        return merged;
    }

    private TableTypeProperty GenerateInterfacePropertyType(InterfaceDeclaration interfaceDeclaration, PropertyDeclaration property, TypeParameters typeParameters)
    {
        LuauVisibility? visibility = property.MutKeyword == null ? LuauVisibility.Read : null;
        var name = property.Name.Text;
        var type = Visit(property.ColonTypeClause);
        var tableProperty = new TableTypeProperty(visibility, name, type);
        if (property.TryGetIntrinsicAttribute(_semanticModel, "luau_name", out var luauNameAttribute)
            && ValidateLuauNameAttribute(luauNameAttribute, out var nameLiteral))
            tableProperty = new TableTypeProperty(visibility, nameLiteral.Value, type);

        if (!property.TryGetIntrinsicAttribute(_semanticModel, "luau_method", out var luauMethodAttribute))
            return tableProperty;

        if (type is not FunctionType functionType)
        {
            _diagnostics.Error(
                luauMethodAttribute.Attribute,
                InternalCodes.InvalidLuauMethodAttribute,
                "May only use function types directly when using 'luau_method' attribute"
            );

            return tableProperty;
        }

        var typeArguments = typeParameters.Parameters.Count > 0
            ? typeParameters.Parameters.ConvertAll(LuauType (param) => new TypeName(param.Name))
            : null;

        var selfType = new TypeName(interfaceDeclaration.Name.Text, typeArguments);
        functionType.ParameterTypes.Insert(0, selfType);

        return tableProperty;
    }

    private TableTypeProperty GenerateInterfaceEventType(EventDeclaration eventDeclaration)
    {
        _semanticModel.RuntimeReferences += 1;
        var parameterTypes = eventDeclaration.Parameters?.ParameterList.ConvertAll(p => Visit(p.ColonTypeClause!.Type)) ?? [];
        var eventType = LuauFactory.QualifyRuntimeType(new TypeName("Event", parameterTypes));
        var name = eventDeclaration.Name.Text;
        if (eventDeclaration.TryGetIntrinsicAttribute(_semanticModel, "luau_name", out var luauNameAttribute)
            && ValidateLuauNameAttribute(luauNameAttribute, out var nameLiteral))
            name = nameLiteral.Value;

        return new TableTypeProperty(LuauVisibility.Read, name, eventType);
    }

    public override LuauNode VisitInterfaceInvocation(InterfaceInvocation interfaceInvocation)
    {
        var symbol = _semanticModel.GetSymbol(interfaceInvocation.Name, SymbolKind.Interface);

        // the resolver already reported why this is not an interface; an invocation sits in expression
        // position, so a statement placeholder here would fail to cast and surface as a compiler crash
        if (symbol is not InterfaceSymbol interfaceSymbol)
            return new NilLiteral();

        var table = GenerateInterfaceInvocationBody(interfaceInvocation.Body, interfaceSymbol);
        if (interfaceSymbol.Implements.Count == 0)
            return table;

        _semanticModel.RuntimeReferences += 1; // merge_meta;
        var metaNames = interfaceSymbol.Implementations.ConvertAll(LuauExpression (i) => new Identifier(GetImplementationMetaName(i)));
        var call = LuauFactory.SetMetatableCall(table, LuauFactory.RuntimeLibraryCall(["merge_meta"], metaNames));
        return new TypeCast(call, new TypeName(interfaceInvocation.Name.Token.Text));
    }

    private Table GenerateInterfaceInvocationBody(InterfaceInvocationBody interfaceInvocationBody, InterfaceSymbol interfaceSymbol)
    {
        var unhandledInitializer = interfaceInvocationBody.Initializers
            .Find(i => i is not (IndexInitializer
                              or PropertyInitializer
                              or ShorthandPropertyInitializer)
            );

        if (unhandledInitializer != null)
            _diagnostics.CompilerError(unhandledInitializer, "Unhandled interface invocation initializer type");

        return new Table(
            interfaceInvocationBody.Initializers.ConvertAll(TableInitializer (i) => i switch
                {
                    IndexInitializer indexInitializer => GenerateInterfaceInvocationIndexInitializer(indexInitializer),
                    PropertyInitializer propertyInitializer => GenerateInterfaceInvocationPropertyInitializer(
                        propertyInitializer,
                        interfaceSymbol
                    ),
                    ShorthandPropertyInitializer shorthandPropertyInitializer => GenerateInterfaceInvocationShorthandPropertyInitializer(
                        shorthandPropertyInitializer,
                        interfaceSymbol
                    ),
                    _ => null!
                }
            )
        );
    }

    private ComputedPropertyTableInitializer GenerateInterfaceInvocationIndexInitializer(IndexInitializer indexInitializer) =>
        new(Visit(indexInitializer.IndexExpression), Visit(indexInitializer.Expression));

    private PropertyTableInitializer GenerateInterfaceInvocationPropertyInitializer(
        PropertyInitializer propertyInitializer,
        InterfaceSymbol interfaceSymbol)
    {
        var name = GetRenamedPropertyName(interfaceSymbol, propertyInitializer.Name.Text);
        var initializedValue = Visit(propertyInitializer.Expression);
        return new PropertyTableInitializer(name, initializedValue);
    }

    private string GetRenamedPropertyName(InterfaceSymbol interfaceSymbol, string name)
    {
        var propertySymbol = interfaceSymbol.GetPropertyAtPath([name]);
        return propertySymbol == null
            || !propertySymbol.TryGetIntrinsicAttribute("luau_name", out var luauNameAttribute)
            || !ValidateLuauNameAttribute(luauNameAttribute, out var nameLiteral)
                ? name
                : nameLiteral.Value;
    }

    private PropertyTableInitializer GenerateInterfaceInvocationShorthandPropertyInitializer(
        ShorthandPropertyInitializer shorthandPropertyInitializer,
        InterfaceSymbol interfaceSymbol) =>
        new(GetRenamedPropertyName(interfaceSymbol, shorthandPropertyInitializer.Identifier.Name.Text), Visit(shorthandPropertyInitializer.Identifier));

    private static string GetImplementationMetaName(Implement implement)
    {
        var typeArguments = implement.TraitName.TypeArguments;
        var typeArgumentText = typeArguments != null ? '_' + string.Join('_', typeArguments.ArgumentsList.Select(a => a.ToString())) : "";
        return $"{implement.TraitName.Name.Text}{typeArgumentText}_for_{implement.InterfaceName.Name.Text}";
    }
}