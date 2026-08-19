using System.Diagnostics.CodeAnalysis;
using Loom.Core.Diagnostics;
using Loom.Core.FlowAnalysis;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving.Symbols;
using Loom.Core.TypeChecking.Types;
using FunctionType = Loom.Core.TypeChecking.Types.FunctionType;
using IntersectionType = Loom.Core.TypeChecking.Types.IntersectionType;
using LiteralType = Loom.Core.TypeChecking.Types.LiteralType;
using PrimitiveType = Loom.Core.TypeChecking.Types.PrimitiveType;
using Type = Loom.Core.TypeChecking.Types.Type;
using TypePredicateType = Loom.Core.Parsing.AST.TypePredicateType;

namespace Loom.Core.TypeChecking;

public sealed partial class TypeChecker
{
    public override Type VisitImplement(Implement implement)
    {
        var traitType = Visit(implement.TraitName);
        var interfaceType = Visit(implement.InterfaceName);
        foreach (var declaration in implement.Body.Implementations)
        {
            var declarationType = (FunctionType)GetTypeAtIndex(declaration, traitType, new LiteralType(declaration.Name.Text));
            BindType(declaration, declarationType);
            MaybeVisit(declaration.TypeParameters);

            for (var i = 0; i < declarationType.ParameterTypes.Count; i++)
            {
                var parameter = declaration.Parameters!.ParameterList[i];
                var explicitType = MaybeVisit(parameter.ColonTypeClause);
                var initializerType = MaybeVisit(parameter.EqualsValueClause);
                var type = declarationType.ParameterTypes[i];
                if (parameter.EqualsValueClause != null)
                    _semanticModel.TypeSolver.AddConstraint(initializerType!, type, parameter.EqualsValueClause.Value);

                if (parameter.EqualsValueClause != null && Type.IsOptional(type))
                    type = type.NonNullable();

                if (explicitType != null)
                    _semanticModel.TypeSolver.AddConstraint(explicitType, type, parameter.ColonTypeClause!.Type);

                BindType(parameter, type);
            }

            var actualType = GetReturnType(declaration);
            _semanticModel.TypeSolver.AddConstraint(actualType, declarationType.ReturnType, declaration.ReturnType?.Type.LocationSpan ?? declaration.LocationSpan);
            if (declaration.ReturnType != null)
                BindType(declaration.ReturnType, declarationType.ReturnType);

            Visit(declaration.Body);
        }

        return TypeSimplifier.Expanded(new IntersectionType([traitType, interfaceType]));
    }

    public override Type VisitSelfExpression(SelfExpression selfExpression)
    {
        var implement = selfExpression.FirstAncestorOfType<Implement>();
        if (implement == null)
        {
            if (selfExpression.Parent is TypePredicateType
                && (selfExpression.FirstAncestorOfType<InterfaceDeclaration>() != null || selfExpression.FirstAncestorOfType<TraitDeclaration>() != null))
                return BindType(selfExpression, PrimitiveType.Unknown);

            return BindType(selfExpression, PrimitiveType.Never);
        }

        var interfaceType = _semanticModel.GetType(implement.InterfaceName);
        if (interfaceType is not InterfaceType nonGenericInterfaceType
            || _semanticModel.GetSymbol(implement.InterfaceName, SymbolKind.Interface) is not InterfaceSymbol interfaceSymbol)
            return BindType(selfExpression, interfaceType);

        var traitProperties = interfaceSymbol.FullImplementations
            .SelectMany(i => i.Body.Implementations)
            .Select(declaration => new ObjectProperty(false, declaration.Name.Text, _semanticModel.GetType(declaration)))
            .ToList();

        var objectType = new ObjectType(nonGenericInterfaceType.ObjectType.Indexer, [..nonGenericInterfaceType.ObjectType.Properties, ..traitProperties]);
        var selfType = new InterfaceType(nonGenericInterfaceType.Name, nonGenericInterfaceType.Constraints, objectType)
        {
            TraitMethodNames = traitProperties.ConvertAll(property => property.Name).ToHashSet(),
            Metamethods = nonGenericInterfaceType.Metamethods,
            IteratedElementType = nonGenericInterfaceType.IteratedElementType
        };

        return BindType(selfExpression, selfType);
    }

    public override Type VisitTraitDeclaration(TraitDeclaration traitDeclaration)
    {
        var name = traitDeclaration.Name.Text;
        var typeParameters = traitDeclaration.TypeParameters?.ParameterList.ConvertAll(VisitTypeParameter);
        var objectType = new ObjectType(null, []);
        var interfaceType = new InterfaceType(name, [], objectType);
        Type publishedType = typeParameters == null
            ? interfaceType
            : new GenericType(traitDeclaration, typeParameters, interfaceType);

        BindType(traitDeclaration, publishedType);

        var properties = ResolveTraitProperties(traitDeclaration.Body.Members);
        objectType.AddProperties(properties);

        if (publishedType is GenericType generic)
            publishedType = VarianceInferrer.ApplyInferredVariance(generic);

        return BindType(traitDeclaration, publishedType);
    }

    public override Type VisitInterfaceDeclaration(InterfaceDeclaration interfaceDeclaration)
    {
        var resolvedType = _semanticModel.GetType(interfaceDeclaration);
        if (resolvedType is not TypeVariable)
        {
            if (_resolvingHoisted.Count == 0)
                _interfaceDeclarations.Add(interfaceDeclaration);

            return resolvedType;
        }

        MaybeVisit(interfaceDeclaration.Attributes);

        var name = interfaceDeclaration.Name.Text;
        if (_semanticModel.GetDeclarationSymbol(interfaceDeclaration, SymbolKind.Interface) is not InterfaceSymbol interfaceSymbol)
        {
            _diagnostics.Error(interfaceDeclaration, InternalCodes.CannotFindSymbol, $"Cannot find symbol for declaration of interface '{name}'.");
            return BindType(interfaceDeclaration, PrimitiveType.Never);
        }

        var typeParameters = interfaceDeclaration.TypeParameters?.ParameterList.ConvertAll(VisitTypeParameter);
        if (interfaceDeclaration.Body?.Members.OfType<MappedTypeDeclaration>().FirstOrDefault() is { } mappedDeclaration)
            return BindType(interfaceDeclaration, PublishMappedType(interfaceDeclaration, typeParameters, mappedDeclaration));

        // Expanded, since a base written as an instantiation ('interface Click: IAction<"Click">') is only
        // an InterfaceType once expanded, and anything that is not one is dropped on the next line.
        var constraints = interfaceDeclaration.ColonTypeListClause?.Types
                .Select(Visit)
                .Select(TypeSimplifier.Expanded)
                .OfType<InterfaceType>()
                .ToList()
            ?? [];

        var objectType = new ObjectType(null, []);
        var interfaceType = new InterfaceType(name, constraints, objectType)
        {
            Metamethods = CollectMetamethods(interfaceSymbol),
            IteratedElementType = CollectIteratedElementType(interfaceSymbol)
        };
        Type publishedType = typeParameters == null
            ? interfaceType
            : new GenericType(interfaceDeclaration, typeParameters, interfaceType);

        BindType(interfaceDeclaration, publishedType);

        var indexerDeclaration = interfaceDeclaration.Body?.Members.OfType<IndexerDeclaration>().FirstOrDefault();
        var indexer = ResolveInterfaceIndexer(constraints, indexerDeclaration);
        objectType.Indexer = indexer;

        var eventDeclarations = interfaceDeclaration.Body?.Members.OfType<EventDeclaration>().ToList() ?? [];
        var propertyDeclarations = interfaceDeclaration.Body?.Members.OfType<PropertyDeclaration>().ToList() ?? [];
        var events = ResolveInterfaceEvents(eventDeclarations);
        var properties = ResolveInterfaceProperties(constraints, propertyDeclarations);
        objectType.AddProperties(events);
        objectType.AddProperties(properties);

        if (interfaceDeclaration.Attributes != null)
            foreach (var attribute in interfaceDeclaration.Attributes.AttributeList)
            {
                CheckPassiveDecorator(attribute);
                CheckAttributeUsage(attribute, AttributeTargetsFlag.Interface);
            }

        if (_resolvingHoisted.Count == 0)
            _interfaceDeclarations.Add(interfaceDeclaration);

        if (publishedType is GenericType generic)
            publishedType = VarianceInferrer.ApplyInferredVariance(generic);

        return BindType(interfaceDeclaration, publishedType);
    }

    public override Type VisitInterfaceInvocation(InterfaceInvocation interfaceInvocation) =>
        CheckOrVisitInterfaceInvocation(interfaceInvocation, null);

    private Type CheckInterfaceInvocation(InterfaceInvocation interfaceInvocation, Type expected, FlowState state)
    {
        var lastState = _flowState;
        _flowState = state;
        var result = CheckOrVisitInterfaceInvocation(interfaceInvocation, expected);
        _flowState = lastState;

        return result;
    }

    private Type CheckOrVisitInterfaceInvocation(InterfaceInvocation interfaceInvocation, Type? expected)
    {
        var type = Visit(interfaceInvocation.Name);
        if (type.Equals(IntrinsicTypes.Range))
            _diagnostics.Warn(interfaceInvocation, InternalCodes.SimplifiableCode, "Use a range literal.");

        var traitProperties = new List<ObjectProperty>();
        if (_semanticModel.GetSymbol(interfaceInvocation.Name, SymbolKind.Interface) is InterfaceSymbol interfaceSymbol)
            traitProperties.AddRange(
                from declaration in interfaceSymbol.Implementations.SelectMany(i => i.Body.Implementations)
                let methodType = _semanticModel.GetType(declaration)
                select new ObjectProperty(false, declaration.Name.Text, methodType)
            );

        if (type is InterfaceType nonGeneric)
        {
            var boundType = BindInterfaceInvocation(interfaceInvocation, nonGeneric, traitProperties);

            // A non-generic invocation has nothing to infer from 'expected', but 'new X { ... }' used
            // where a different, structurally incompatible type is expected still needs to be flagged -
            // deferred to TypeSolver rather than reported directly, same as every other Check case, so
            // it composes with whatever else is still being inferred around it.
            if (expected != null && !boundType.IsAssignableTo(expected))
                _semanticModel.TypeSolver.AddConstraint(boundType, expected, interfaceInvocation);

            return boundType;
        }

        if (type is not GenericType { UnderlyingType: InterfaceType underlying } generic)
        {
            _diagnostics.Error(interfaceInvocation, InternalCodes.InvalidInvocation, $"Type '{type}' is not an interface.");
            return BindType(interfaceInvocation, PrimitiveType.Never);
        }

        if (!TrySubstituteGenericInterface(interfaceInvocation, generic, underlying, expected, out var interfaceType))
            return BindType(interfaceInvocation, PrimitiveType.Never);

        return BindInterfaceInvocation(interfaceInvocation, interfaceType, traitProperties);
    }

    private InterfaceType BindInterfaceInvocation(InterfaceInvocation node, InterfaceType interfaceType, List<ObjectProperty> traitProperties)
    {
        CheckInterfaceInvocationInitializers(node, interfaceType);

        // A fresh ObjectType/InterfaceType is built here rather than mutating interfaceType.ObjectType in place,
        // since interfaceType is the shared instance cached for the interface declaration; mutating it would leak
        // trait methods into the structural property list for every other construction site of the same interface.
        var traitMethodNames = traitProperties.Select(p => p.Name).ToHashSet();
        var objectType = new ObjectType(interfaceType.ObjectType.Indexer, [..interfaceType.ObjectType.Properties, ..traitProperties]);
        var boundType = new InterfaceType(interfaceType.Name, interfaceType.Constraints, objectType)
        {
            TraitMethodNames = traitMethodNames,
            Metamethods = interfaceType.Metamethods,
            IteratedElementType = interfaceType.IteratedElementType
        };

        return BindType(node, boundType);
    }

    /// <summary>
    ///     Non-destructive struct update: every field the '{ ... }' block lists is checked against the left
    ///     operand's own type the same way a <c>new X { ... }</c> initializer is, but a field it leaves out is
    ///     never an error here - it just keeps the left operand's value at generation time, so completeness
    ///     never applies to 'with' the way it does to construction.
    /// </summary>
    public override Type VisitWithOperator(WithOperator withOperator)
    {
        var expressionType = Visit(withOperator.Expression);
        if (expressionType is not InterfaceType interfaceType)
        {
            if (Type.IsNotNever(expressionType))
                _diagnostics.Error(
                    withOperator.Expression,
                    InternalCodes.InvalidWithOperand,
                    $"'with' requires an interface value, got '{expressionType}'."
                );

            return BindType(withOperator, PrimitiveType.Never);
        }

        foreach (var initializer in withOperator.Body.Initializers)
            CheckInterfaceInvocationInitializer(interfaceType, initializer);

        return BindType(withOperator, expressionType);
    }

    private bool TrySubstituteGenericInterface(
        InterfaceInvocation node,
        GenericType generic,
        InterfaceType underlying,
        Type? expected,
        [MaybeNullWhen(false)] out InterfaceType substituted)
    {
        substituted = null;
        var substitution = node.TypeArguments != null
            ? ResolveExplicitInterfaceTypeArguments(node, generic)
            : _inferrer.InferInterfaceTypeArguments(node, generic, underlying, expected);

        if (substitution == null)
            return false;

        foreach (var tp in generic.Parameters)
        {
            if (tp.Constraint == null || !substitution.TryGetValue(tp, out var arg)) continue;
            if (!CheckTypeParameterConstraints(node, arg, tp))
                return false;
        }

        var substitutedObject = SubstituteObjectType(node, underlying.ObjectType, substitution);
        substituted = new InterfaceType(underlying.Name, underlying.Constraints, substitutedObject) { Metamethods = underlying.Metamethods, IteratedElementType = underlying.IteratedElementType };
        return true;
    }

    private static readonly HashSet<string> _supportedMetamethods = ["__add", "__sub", "__mul", "__div", "__idiv", "__mod", "__pow"];

    private List<ObjectProperty> ResolveTraitProperties(List<DeclareFunctionSignature> signatures)
    {
        var properties = new List<ObjectProperty>();
        foreach (var signature in signatures)
        {
            properties.Add(new ObjectProperty(false, signature.Name.Text, Visit(signature)));
            if (signature.Attributes == null)
                continue;

            foreach (var attribute in signature.Attributes.AttributeList)
            {
                CheckPassiveDecorator(attribute);
                CheckAttributeUsage(attribute, AttributeTargetsFlag.Function);
            }

            if (signature.TryGetIntrinsicAttribute(_semanticModel, "luau_metamethod", out var metamethodAttribute))
            {
                ValidateMetamethodAttribute(metamethodAttribute);
                CheckMetamethodDoesNotYield(signature);
            }
        }

        return properties;
    }

    /// <summary>
    ///     A metamethod is invoked by Luau itself, across a C-call boundary, where a yielding thread raises
    ///     rather than suspends. So an operator that awaits does not block - it fails, at whichever call
    ///     first reached the yield, with an error naming neither the operator nor the type it belongs to.
    /// </summary>
    private void CheckMetamethodDoesNotYield(DeclareFunctionSignature signature)
    {
        if (signature.AsyncKeyword == null)
            return;

        _diagnostics.Error(
            signature.AsyncKeyword,
            InternalCodes.YieldInNoYieldContext,
            $"'{signature.Name.Text}' is a metamethod, so it cannot be 'async'.",
            "Luau invokes it across a C-call boundary, where yielding raises - await before the operator runs and give it the result"
        );
    }

    private void ValidateMetamethodAttribute(AttributeSymbol attribute)
    {
        if (attribute.Attribute.Arguments.ArgumentList is not [Literal { Value: string metamethodName }])
        {
            _diagnostics.Error(attribute.Attribute, InternalCodes.InvalidMetamethodAttribute, "'luau_metamethod' requires a single string literal argument.");
            return;
        }

        if (!_supportedMetamethods.Contains(metamethodName))
            _diagnostics.Error(
                attribute.Attribute,
                InternalCodes.InvalidMetamethodAttribute,
                $"'{metamethodName}' is not a supported metamethod. Supported metamethods: {string.Join(", ", _supportedMetamethods)}."
            );
    }

    /// <summary>
    ///     The element type an interface yields when iterated, taken from the <c>Iterator&lt;T&gt;</c> it
    ///     implements. Collected from the symbol onto the canonical type exactly as
    ///     <see cref="CollectMetamethods" /> is, and for the same reason: an <c>implement</c> block sits
    ///     outside the interface's own declaration, so nothing about it reaches the declaration's types.
    /// </summary>
    private Type? CollectIteratedElementType(InterfaceSymbol interfaceSymbol)
    {
        foreach (var implementation in interfaceSymbol.FullImplementations)
            if (implementation.TraitName.Name.Text == IteratorTraitName && implementation.TraitName.TypeArguments?.ArgumentsList is [{ } element])
                return Visit(element);

        return null;
    }

    private const string IteratorTraitName = "Iterator";

    private static Dictionary<string, string> CollectMetamethods(InterfaceSymbol interfaceSymbol)
    {
        var metamethods = new Dictionary<string, string>();
        foreach (var trait in interfaceSymbol.Implements)
            foreach (var (metamethodName, methodName) in trait.Metamethods)
                metamethods[metamethodName] = methodName;

        foreach (var (metamethodName, methodName) in interfaceSymbol.Metamethods)
            metamethods[metamethodName] = methodName;

        return metamethods;
    }

    private List<ObjectProperty> ResolveInterfaceEvents(List<EventDeclaration> eventDeclarations) =>
        eventDeclarations.ConvertAll(e =>
            {
                MaybeVisit(e.Attributes);
                return new ObjectProperty(false, e.Name.Text, Visit(e));
            }
        );

    private List<ObjectProperty> ResolveInterfaceProperties(List<InterfaceType> constraints, List<PropertyDeclaration> propertyDeclarations)
    {
        var properties = new List<ObjectProperty>();
        foreach (var property in propertyDeclarations)
        {
            MaybeVisit(property.Attributes);

            var name = property.Name.Text;
            if (constraints.Find(i => i.GetProperty(name) != null) is { } subclass
                && !property.TryGetIntrinsicAttribute(_semanticModel, "override", out _))
            {
                _diagnostics.Error(property, InternalCodes.ConstraintPropertyOverride, $"Property '{name}' is already declared within constraint '{subclass}'.");
                return properties;
            }

            var isMutable = property.MutKeyword != null;
            var valueType = Visit(property.ColonTypeClause);

            if (property.Attributes != null)
                foreach (var attribute in property.Attributes.AttributeList)
                {
                    CheckPassiveDecorator(attribute);
                    CheckAttributeUsage(attribute, AttributeTargetsFlag.Property);
                }

            if (property.TryGetIntrinsicAttribute(_semanticModel, "luau_metamethod", out var metamethodAttribute))
            {
                ValidateMetamethodAttribute(metamethodAttribute);

                // Same rule as a trait's metamethod: Luau invokes it across a C-call boundary, and a
                // declare interface is the one other place a metamethod may be written.
                if (valueType is FunctionType { IsAsync: true })
                    _diagnostics.Error(
                        metamethodAttribute.Attribute,
                        InternalCodes.YieldInNoYieldContext,
                        $"'{name}' is a metamethod, so it cannot be 'async'.",
                        "Luau invokes it across a C-call boundary, where yielding raises - await before the operator runs and give it the result"
                    );

                if (valueType is FunctionType && !property.IsDescendantOf<Declare>())
                    _diagnostics.Error(
                        metamethodAttribute.Attribute,
                        InternalCodes.InvalidMetamethodAttribute,
                        "'luau_metamethod' on a function property is only allowed within a 'declare interface'."
                    );
            }

            properties.Add(new ObjectProperty(isMutable, name, valueType));
        }

        return MergeOverloadedProperties(properties);
    }

    // A property name declared more than once, where every declaration is function-typed, is an overload set (e.g. CFrame.new's constructor shapes) merged into one IntersectionType.
    private static List<ObjectProperty> MergeOverloadedProperties(List<ObjectProperty> properties)
    {
        var merged = new List<ObjectProperty>();
        var indexByName = new Dictionary<string, int>();
        foreach (var property in properties)
        {
            if (indexByName.TryGetValue(property.Name, out var index)
                && property.ValueType is FunctionType newSignature
                && TryGetSignatures(merged[index].ValueType, out var existingSignatures))
            {
                existingSignatures.Add(newSignature);
                merged[index] = new ObjectProperty(merged[index].IsMutable, property.Name, new IntersectionType([..existingSignatures]));
                continue;
            }

            indexByName[property.Name] = merged.Count;
            merged.Add(property);
        }

        return merged;
    }

    private static bool TryGetSignatures(Type type, [MaybeNullWhen(false)] out List<FunctionType> signatures)
    {
        switch (type)
        {
            case FunctionType functionType:
                signatures = [functionType];
                return true;
            case IntersectionType intersection when intersection.Types.TrueForAll(t => t is FunctionType):
                signatures = intersection.Types.ConvertAll(t => (FunctionType)t);
                return true;
            default:
                signatures = null;
                return false;
        }
    }

    /// <summary>
    ///     A mapped interface publishes the <see cref="MappedType" /> itself rather than an
    ///     <see cref="InterfaceType" /> wrapped around one: its members are not written down anywhere, so
    ///     there is nothing to put in an object body until the keys arrive.
    /// </summary>
    private Type PublishMappedType(InterfaceDeclaration interfaceDeclaration, List<Types.TypeParameter>? typeParameters, MappedTypeDeclaration mappedDeclaration)
    {
        if (interfaceDeclaration.ColonTypeListClause != null)
            _diagnostics.Error(
                interfaceDeclaration.ColonTypeListClause,
                InternalCodes.InvalidMappedType,
                $"Mapped type '{interfaceDeclaration.Name.Text}' cannot have base types.",
                "every member it has comes from the keys it maps over"
            );

        // Published before its keys and member type are resolved, so a body naming the type it belongs to -
        // 'interface Rec<T> { [K from "a"]: Rec<T> }' - finds this entry rather than an unbound name. Same
        // order the ordinary interface path above uses, and for the same reason.
        //
        // The binder stands in for both until they arrive, because it is the one type that cannot resolve:
        // anything a mapped type can answer would be answered and cached during the window by whatever
        // expands the self-reference, and 'never' in particular answers "an object with no members".
        var binder = VisitMappedTypeDeclaration(mappedDeclaration);
        var mapped = new MappedType(binder, binder, binder, mappedDeclaration.MutKeyword != null);
        Type published = typeParameters == null ? mapped : new GenericType(interfaceDeclaration, typeParameters, mapped);
        BindType(interfaceDeclaration, published);

        mapped.Source = Visit(mappedDeclaration.SourceType);
        mapped.ValueType = Visit(mappedDeclaration.ColonTypeClause);

        return typeParameters == null ? mapped.Resolve() ?? mapped : published;
    }

    private ObjectIndexer? ResolveInterfaceIndexer(List<InterfaceType> constraints, IndexerDeclaration? indexerDeclaration)
    {
        if (indexerDeclaration == null)
            return null;

        var isMutable = indexerDeclaration.MutKeyword != null;
        var indexType = Visit(indexerDeclaration.IndexType);
        var valueType = Visit(indexerDeclaration.ColonTypeClause);
        if (constraints.Find(i => i.Indexer != null) is not { } subclass)
            return new ObjectIndexer(isMutable, indexType, valueType);

        _diagnostics.Error(indexerDeclaration, InternalCodes.ConstraintIndexerOverride, $"An indexer is already declared within constraint '{subclass}'.");
        return null;
    }

    private void CheckInterfaceInvocationInitializers(InterfaceInvocation node, InterfaceType interfaceType)
    {
        var objectType = interfaceType.ObjectType;
        var providedProperties = new HashSet<string>();
        foreach (var property in node.Body.Initializers.SelectMany(initializer => CheckInterfaceInvocationInitializer(interfaceType, initializer)))
            providedProperties.Add(property);

        foreach (var property in objectType.Properties.Where(property => !providedProperties.Contains(property.Name)))
            _diagnostics.Error(
                node.Body,
                InternalCodes.IncompleteInterfaceInvocation,
                $"Missing property initializer for '{property.Name}' in interface '{interfaceType.Name}'."
            );
    }

    private HashSet<string> CheckInterfaceInvocationInitializer(InterfaceType interfaceType, InterfaceInvocationInitializer initializer)
    {
        var providedProperties = new HashSet<string>();
        switch (initializer)
        {
            case PropertyInitializer propertyInitializer:
            {
                var propertyName = CheckPropertyInitializer(propertyInitializer, propertyInitializer.Name.Text, propertyInitializer.Expression, interfaceType);
                if (propertyName != null)
                    providedProperties.Add(propertyName);

                break;
            }
            case ShorthandPropertyInitializer shorthandPropertyInitializer:
                var shorthandPropertyName = CheckPropertyInitializer(
                    shorthandPropertyInitializer,
                    shorthandPropertyInitializer.Identifier.Name.Text,
                    shorthandPropertyInitializer.Identifier,
                    interfaceType
                );

                if (shorthandPropertyName != null)
                    providedProperties.Add(shorthandPropertyName);

                break;
            case IndexInitializer indexInitializer:
            {
                CheckIndexInitializer(indexInitializer, interfaceType);
                break;
            }
        }

        return providedProperties;
    }

    private string? CheckPropertyInitializer(Node node, string name, Expression expression, InterfaceType interfaceType)
    {
        var property = interfaceType.GetProperty(name);
        if (property == null)
        {
            _diagnostics.Error(
                node,
                InternalCodes.InvalidAccess,
                $"Property '{name}' does not exist on interface '{interfaceType.Name}'."
            );

            return null;
        }

        Check(expression, property.ValueType);
        return name;
    }

    private void CheckIndexInitializer(IndexInitializer initializer, InterfaceType interfaceType)
    {
        var indexer = interfaceType.Indexer;
        if (indexer == null)
        {
            _diagnostics.Error(
                initializer,
                InternalCodes.InvalidAccess,
                $"Interface '{interfaceType.Name}' does not have an indexer."
            );

            return;
        }

        Check(initializer.IndexExpression, indexer.KeyType);
        Check(initializer.Expression, indexer.ValueType);
    }
}