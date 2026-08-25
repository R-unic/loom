using System.Diagnostics.CodeAnalysis;
using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving.Symbols;
using Loom.Core.TypeChecking.Types;
using FunctionType = Loom.Core.TypeChecking.Types.FunctionType;
using IntersectionType = Loom.Core.TypeChecking.Types.IntersectionType;
using LiteralType = Loom.Core.TypeChecking.Types.LiteralType;
using PrimitiveType = Loom.Core.TypeChecking.Types.PrimitiveType;
using Type = Loom.Core.TypeChecking.Types.Type;
using TypePredicateType = Loom.Core.Parsing.AST.TypePredicateType;
using Loom.Core.TypeChecking.Solving;

namespace Loom.Core.TypeChecking;

public sealed partial class TypeChecker
{
    public override Type VisitImplement(Implement implement)
    {
        var traitType = Visit(implement.TraitName);
        var interfaceType = Visit(implement.InterfaceName);
        foreach (var declaration in implement.Body.Implementations)
        {
            // not a FunctionType only when the trait/interface name itself failed to resolve (the
            // resolver already reported why - most commonly ImplementOutsideModuleScope - and
            // GetTypeAtIndex has already reported its own diagnostic for indexing into the fallback
            // type that leaves); nothing left here can be checked safely against a made-up signature
            if (GetTypeAtIndex(declaration, traitType, new LiteralType(declaration.Name.Text)) is not FunctionType declarationType)
                continue;

            BindType(declaration, declarationType);
            MaybeVisit(declaration.TypeParameters);

            CheckFunctionBodyAgainstSignature(declaration, declarationType);
        }

        return TypeSimplifier.Expanded(new IntersectionType([traitType, interfaceType]));
    }

    /// <summary>
    ///     Checks a function's parameters and body against a signature it is already known to implement -
    ///     an <c>implement</c> block's method against the trait/interface member it satisfies, or a static
    ///     block's method against the static member it provides. Parameter and return-type constraints are
    ///     added against <paramref name="signature" /> rather than re-inferred, and the body is visited
    ///     under that binding. <c>Math.Min</c> bounds the loop rather than assuming the parameter list and
    ///     signature agree in length - a mismatch is reported by whichever check established the signature
    ///     match in the first place, and this only needs to not run past either list.
    /// </summary>
    private void CheckFunctionBodyAgainstSignature(FunctionDeclaration node, FunctionType signature)
    {
        var parameterCount = Math.Min(signature.ParameterTypes.Count, node.Parameters?.ParameterList.Count ?? 0);
        for (var i = 0; i < parameterCount; i++)
        {
            var parameter = node.Parameters!.ParameterList[i];
            var explicitType = MaybeVisit(parameter.ColonTypeClause);
            var initializerType = MaybeVisit(parameter.EqualsValueClause);
            var type = signature.ParameterTypes[i];
            if (parameter.EqualsValueClause != null)
                _semanticModel.TypeSolver.AddConstraint(initializerType!, type, parameter.EqualsValueClause.Value);

            if (parameter.EqualsValueClause != null && Type.IsOptional(type))
                type = type.NonNullable();

            if (explicitType != null)
                _semanticModel.TypeSolver.AddConstraint(explicitType, type, parameter.ColonTypeClause!.Type);

            BindType(parameter, type);
        }

        var actualType = GetReturnType(node);
        _semanticModel.TypeSolver.AddConstraint(actualType, signature.ReturnType, node.ReturnType?.Type.LocationSpan ?? node.LocationSpan);
        if (node.ReturnType != null)
            BindType(node.ReturnType, signature.ReturnType);

        Visit(node.Body);
    }

    public override Type VisitSelfExpression(SelfExpression selfExpression)
    {
        var implement = selfExpression.FirstAncestorOfType<Implement>();
        if (implement == null)
        {
            // outside an 'implement' block, '@' names no concrete interface - only 'unknown', and only
            // where a value of an unknown, opaque shape is legal: a type predicate subject, or the value
            // passed through a default method's own body (shared verbatim by every implementer, so it can
            // assume nothing about which interface's fields it will actually run against)
            if ((selfExpression.Parent is TypePredicateType || selfExpression.IsInsideDefaultMethodBody())
                && (selfExpression.FirstAncestorOfType<InterfaceDeclaration>() != null || selfExpression.FirstAncestorOfType<TraitDeclaration>() != null))
                return BindType(selfExpression, PrimitiveType.Unknown);

            return BindType(selfExpression, PrimitiveType.Never);
        }

        var interfaceType = _semanticModel.GetType(implement.InterfaceName);
        if (interfaceType is not InterfaceType nonGenericInterfaceType
            || _semanticModel.GetSymbol(implement.InterfaceName, SymbolKind.Interface) is not InterfaceSymbol interfaceSymbol)
            return BindType(selfExpression, interfaceType);

        var traitProperties = CollectEffectiveTraitProperties(interfaceSymbol.FullImplementations);
        var objectType = new ObjectType(nonGenericInterfaceType.ObjectType.Indexer, [..nonGenericInterfaceType.ObjectType.Properties, ..traitProperties]);
        var selfType = nonGenericInterfaceType.WithObjectType(objectType, traitProperties.ConvertAll(property => property.Name).ToHashSet());

        return BindType(selfExpression, selfType);
    }

    public override Type VisitTraitDeclaration(TraitDeclaration traitDeclaration)
    {
        var name = traitDeclaration.Name.Text;
        var typeParameters = traitDeclaration.TypeParameters?.ParameterList.ConvertAll(VisitTypeParameter);
        var objectType = new ObjectType(null, []);
        var interfaceType = new InterfaceType(name, [], objectType);

        return PublishGenericOrInterface(
            traitDeclaration,
            typeParameters,
            interfaceType,
            () => objectType.AddProperties(ResolveTraitProperties(traitDeclaration.Body.Members))
        );
    }

    /// <summary>
    ///     Wraps <paramref name="interfaceType" /> in a <see cref="GenericType" /> when
    ///     <paramref name="typeParameters" /> is non-null, binds that to <paramref name="declaration" />,
    ///     runs <paramref name="populateMembers" /> - which fills in <paramref name="interfaceType" />'s
    ///     members, indexer, etc., reading back the bound type where a member's own signature
    ///     self-references the declaration - and finally applies <see cref="VarianceInferrer" /> if generic
    ///     before binding and returning the finished type. Binding before <paramref name="populateMembers" />
    ///     runs (rather than once, after) is what lets a member self-reference the interface/trait it
    ///     belongs to while its own body is still being resolved.
    /// </summary>
    private Type PublishGenericOrInterface(
        GenericNamedDeclaration declaration,
        List<Types.TypeParameter>? typeParameters,
        InterfaceType interfaceType,
        Action populateMembers)
    {
        Type publishedType = typeParameters == null
            ? interfaceType
            : new GenericType(declaration, typeParameters, interfaceType);

        BindType(declaration, publishedType);

        populateMembers();

        if (publishedType is GenericType generic)
            publishedType = VarianceInferrer.ApplyInferredVariance(generic);

        return BindType(declaration, publishedType);
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
            IteratedElementType = CollectIteratedElementType(interfaceSymbol),
            IsIntrinsic = interfaceSymbol.IsIntrinsic
        };
        return PublishGenericOrInterface(interfaceDeclaration, typeParameters, interfaceType, () =>
        {
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
        });
    }

    /// <summary>
    ///     Every trait method reachable through <paramref name="implementations" />: an explicit override
    ///     wins, and a trait method neither this interface nor its constraints ever overrode falls back to
    ///     its trait's own default. A trait method with no override and no default cannot reach here -
    ///     <see cref="Resolving.Resolver.VisitImplement" /> already required an override for it. Without this
    ///     fallback a fully-defaulted method would type-check nowhere: not <c>foo.method()</c> on a
    ///     constructed value, and not <c>@.method()</c> from a sibling <c>implement</c> block.
    /// </summary>
    private List<ObjectProperty> CollectEffectiveTraitProperties(IReadOnlyCollection<Implement> implementations)
    {
        var effective = new Dictionary<string, ObjectProperty>();
        foreach (var declaration in implementations.SelectMany(i => i.Body.Implementations))
            effective[declaration.Name.Text] = new ObjectProperty(false, declaration.Name.Text, _semanticModel.GetType(declaration));

        foreach (var implement in implementations)
        {
            if (_semanticModel.GetSymbol(implement.TraitName, SymbolKind.Trait) is not TraitSymbol traitSymbol
                || traitSymbol.Defaults.Count == 0)
                continue;

            // indexed off the trait's own published type rather than read straight off
            // 'defaultDeclaration' by node: an intrinsic (or any cross-file) trait's default body was
            // bound by a different SemanticModel, whose per-node type cache this one never shares -
            // only the trait's type itself crosses that boundary, the same way an ordinary cross-file
            // interface property already does.
            var traitType = _semanticModel.GetType(traitSymbol.Declaration) switch
            {
                InterfaceType direct => direct,
                GenericType { UnderlyingType: InterfaceType underlying } => underlying,
                _ => null
            };

            if (traitType == null) continue;

            foreach (var name in traitSymbol.Defaults.Keys)
                if (!effective.ContainsKey(name) && traitType.GetProperty(name) is { } property)
                    effective[name] = property;
        }

        return effective.Values.ToList();
    }

    /// <summary>
    ///     Non-destructive struct update: every field the '{ ... }' block lists is checked against the left
    ///     operand's own type the same way a <c>new X { ... }</c> initializer is, but a field it leaves out is
    ///     never an error here - it just keeps the left operand's value at generation time, so completeness
    ///     never applies to 'with' the way it does to construction.
    /// </summary>
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

            properties.Add(new ObjectProperty(isMutable, name, valueType, IsStatic: property.IsStatic));
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
                merged[index] = new ObjectProperty(merged[index].IsMutable, property.Name, new IntersectionType([..existingSignatures]), merged[index].IsStatic);
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
}
