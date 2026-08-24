using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving.Symbols;
using Attribute = Loom.Core.Parsing.AST.Attribute;

namespace Loom.Core.Resolving;

public sealed partial class Resolver
{
    public override bool VisitImplement(Implement implement)
    {
        if (!AtModuleScope())
        {
            _diagnostics.Error(
                implement,
                InternalCodes.ImplementOutsideModuleScope,
                "Traits can only be implemented at the top level of a module.",
                "move the 'implement' block out of the enclosing block"
            );

            return false;
        }

        var traitNameSymbol = LookupTypeSymbol(implement.TraitName.Name.Text);
        if (traitNameSymbol is not TraitSymbol traitSymbol)
        {
            _diagnostics.Error(implement.TraitName, InternalCodes.NonInterfaceImplementation, "Interfaces may only implement traits.");
            return false;
        }

        AddReference(implement.TraitName, traitSymbol);
        if (implement.TraitName.TypeArguments != null
            && implement.TraitName.TypeArguments.ArgumentsList.Any(typeArgument => !Visit(typeArgument)))
            return false;

        var interfaceNameSymbol = LookupTypeSymbol(implement.InterfaceName.Name.Text);
        if (interfaceNameSymbol is not InterfaceSymbol interfaceSymbol)
        {
            _diagnostics.Error(implement.InterfaceName, InternalCodes.NonInterfaceImplementation, "Traits may only be implemented by interfaces.");
            return false;
        }

        if (interfaceSymbol.IsIntrinsic)
        {
            _diagnostics.Error(
                implement.InterfaceName,
                InternalCodes.IntrinsicImplementation,
                $"Trait '{implement.TraitName}' may not be implemented on intrinsic interface '{implement.InterfaceName}'."
            );

            return false;
        }

        AddReference(implement.InterfaceName, interfaceSymbol);
        if (implement.InterfaceName.TypeArguments != null
            && implement.InterfaceName.TypeArguments.ArgumentsList.Any(typeArgument => !Visit(typeArgument)))
            return false;

        if (interfaceSymbol.Implements.Contains(traitSymbol))
        {
            _diagnostics.Error(
                implement.TraitName,
                InternalCodes.DuplicateImplementation,
                $"Interface '{interfaceSymbol.Name}' already has an implementation for trait '{traitSymbol.Name}'"
            );

            return false;
        }

        foreach (var implementation in implement.Body.Implementations.Where(implementation => !traitSymbol.MethodNames.Contains(implementation.Name.Text)))
        {
            _diagnostics.Error(
                implementation,
                InternalCodes.InvalidImplementation,
                $"Trait '{traitSymbol.Name}' does not contain a signature for method '{implementation.Name.Text}'"
            );

            return false;
        }

        foreach (var methodName in traitSymbol.MethodNames
                     .Where(methodName => !traitSymbol.Defaults.ContainsKey(methodName))
                     .Where(methodName => implement.Body.Implementations.All(i => methodName != i.Name.Text)))
        {
            _diagnostics.Error(
                implement,
                InternalCodes.MissingImplementation,
                $"Implementation of trait '{traitSymbol.Name}' on interface '{interfaceSymbol.Name}' is missing method '{methodName}'"
            );

            return false;
        }

        // Two traits defaulting the same method name is only a problem once neither implementer
        // overrides it - an override picks a winner unambiguously, the same as it would if the
        // collision were between two abstract (non-defaulted) signatures instead. Checked against
        // every already-resolved sibling implement rather than only the current one, since either
        // side of the pair could be the one written first in source order.
        foreach (var name in traitSymbol.Defaults.Keys.Where(name => implement.Body.Implementations.All(i => i.Name.Text != name)))
        {
            var collidingImplement = interfaceSymbol.Implementations.FirstOrDefault(sibling =>
                sibling.Body.Implementations.All(i => i.Name.Text != name)
                && _semanticModel.GetSymbol(sibling.TraitName, SymbolKind.Trait) is TraitSymbol siblingTrait
                && siblingTrait.Defaults.ContainsKey(name));

            if (collidingImplement == null) continue;

            var collidingTraitName = _semanticModel.GetSymbol(collidingImplement.TraitName, SymbolKind.Trait)!.Name;
            _diagnostics.Error(
                implement,
                InternalCodes.AmbiguousTraitDefault,
                $"Traits '{collidingTraitName}' and '{traitSymbol.Name}' both default method '{name}' on interface '{interfaceSymbol.Name}' - override '{name}' explicitly to resolve the ambiguity."
            );
        }

        using var _ = InScope();
        interfaceSymbol.Implementations.Add(implement);
        interfaceSymbol.Implements.Add(traitSymbol);
        traitSymbol.ImplementedBy.Add(interfaceSymbol);
        var success = interfaceSymbol.FullProperties
            .All(property => DeclareVariable(implement, new InjectedPropertyVariableSymbol(implement, property.Name, interfaceSymbol, property.IsMutable)));
        
        if (success)
        {
            var otherMethods = interfaceSymbol.FullImplementations
                .Where(other => other != implement)
                .SelectMany(other => other.Body.Implementations);

            success = otherMethods.All(declaration => DeclareVariable(declaration, new FunctionSymbol(declaration, declaration.Name.Text)));
        }

        if (success)
            Visit(implement.Body);

        return success;
    }

    public override bool VisitStaticBlock(StaticBlock staticBlock)
    {
        if (!AtModuleScope())
        {
            _diagnostics.Error(
                staticBlock,
                InternalCodes.StaticBlockOutsideModuleScope,
                "Static blocks can only be declared at the top level of a module.",
                "move the 'static' block out of the enclosing block"
            );

            return false;
        }

        var interfaceNameSymbol = LookupTypeSymbol(staticBlock.InterfaceName.Name.Text);
        if (interfaceNameSymbol == null)
        {
            _diagnostics.Error(
                staticBlock.InterfaceName,
                InternalCodes.CannotFindSymbol,
                $"Cannot find interface symbol '{staticBlock.InterfaceName.Name.Text}'."
            );

            return false;
        }

        if (interfaceNameSymbol is not InterfaceSymbol interfaceSymbol)
        {
            _diagnostics.Error(staticBlock.InterfaceName, InternalCodes.NonInterfaceImplementation, "A 'static' block may only target an interface.");
            return false;
        }

        AddReference(staticBlock.InterfaceName, interfaceSymbol);

        if (interfaceSymbol.IsAmbient)
        {
            _diagnostics.Error(
                staticBlock.InterfaceName,
                InternalCodes.StaticBlockOnAmbientInterface,
                $"Interface '{interfaceSymbol.Name}' is ambient, so its static members need no companion block.",
                "remove the 'static' block - an ambient interface's static signatures are trusted as-is"
            );

            return false;
        }

        if (interfaceSymbol.StaticBlocks.Count > 0)
        {
            _diagnostics.Error(
                staticBlock.InterfaceName,
                InternalCodes.DuplicateStaticBlock,
                $"Interface '{interfaceSymbol.Name}' already has a 'static' block."
            );

            return false;
        }

        interfaceSymbol.StaticBlocks.Add(staticBlock);

        using var _ = InScope();
        return Visit(staticBlock.Body);
    }

    public override bool VisitSelfExpression(SelfExpression selfExpression)
    {
        var implement = selfExpression.FirstAncestorOfType<Implement>();
        if (implement != null)
        {
            if (_semanticModel.GetSymbol(implement.InterfaceName) is not InterfaceSymbol interfaceSymbol)
                return false;

            AddReference(selfExpression, interfaceSymbol);
            return true;
        }

        // '@' as a type predicate subject on any interface/trait member, or as the receiver inside a
        // default trait method's own body - both name no concrete interface, only the trait/interface
        // declaration itself
        if (selfExpression.Parent is TypePredicateType || selfExpression.IsInsideDefaultMethodBody())
        {
            if (selfExpression.FirstAncestorOfType<InterfaceDeclaration>() is { } interfaceDeclaration)
            {
                if (_semanticModel.GetDeclarationSymbol(interfaceDeclaration, SymbolKind.Interface) is not InterfaceSymbol interfaceSymbol)
                    return false;

                AddReference(selfExpression, interfaceSymbol);
                return true;
            }

            if (selfExpression.FirstAncestorOfType<TraitDeclaration>() is { } traitDeclaration)
            {
                if (_semanticModel.GetDeclarationSymbol(traitDeclaration, SymbolKind.Trait) is not TraitSymbol traitSymbol)
                    return false;

                AddReference(selfExpression, traitSymbol);
                return true;
            }
        }

        _diagnostics.Error(
            selfExpression,
            InternalCodes.SelfOutsideImplementation,
            "'@' can only be used inside an implemented trait method or as a type predicate subject on an interface or trait member."
        );

        return false;
    }

    public override bool VisitTraitDeclaration(TraitDeclaration traitDeclaration)
    {
        if (!DeclareTrait(traitDeclaration) || !ResolveTraitBody(traitDeclaration.Body, traitDeclaration.Name.Text))
            return false;

        using var _ = InScope();
        base.VisitTraitDeclaration(traitDeclaration);

        return true;
    }

    public override bool VisitInterfaceDeclaration(InterfaceDeclaration interfaceDeclaration)
    {
        var isSealed = interfaceDeclaration.SealedKeyword != null;
        if (!DeclareVariable(interfaceDeclaration)
            || DeclareInterface(interfaceDeclaration, isSealed) is not { } symbol
            || !ResolveInterfaceConstraints(interfaceDeclaration.ColonTypeListClause, symbol))
            return false;
        
        using (var _ = InScope())
            base.VisitInterfaceDeclaration(interfaceDeclaration);

        return ResolveInterfaceBody(interfaceDeclaration.Body, symbol);
    }

    public override bool VisitDeclare(Declare declare)
    {
        using var ambient = InContext(ResolverContext.Ambient);

        bool result;
        if (declare.Signature is InterfaceDeclaration interfaceDeclaration)
        {
            var isSealed = interfaceDeclaration.SealedKeyword != null;
            var interfaceSymbol = DeclareInterface(interfaceDeclaration, isSealed);
            result = interfaceSymbol is not null
                && ResolveInterfaceConstraints(interfaceDeclaration.ColonTypeListClause, interfaceSymbol);

            if (result)
            {
                interfaceSymbol!.IsAmbient = true;
                using (var _ = InScope())
                    result &= base.VisitInterfaceDeclaration(interfaceDeclaration);

                result &= ResolveInterfaceBody(interfaceDeclaration.Body, interfaceSymbol);
            }
        }
        else
        {
            result = Visit(declare.Signature);
        }

        return result;
    }

    public override bool VisitInterfaceInvocation(InterfaceInvocation interfaceInvocation)
    {
        var name = interfaceInvocation.Name.Token.Text;
        var typeSymbol = LookupTypeSymbol(name);
        var symbol = LookupValueSymbol(name) ?? typeSymbol;
        switch (symbol)
        {
            case null:
                _diagnostics.Error(interfaceInvocation.Name, InternalCodes.CannotFindSymbol, $"Cannot find interface symbol '{name}'.");
                return false;
            case InterfaceSymbol:
                _diagnostics.Error(
                    interfaceInvocation,
                    InternalCodes.InvokeDeclaredInterface,
                    $"Cannot invoke interface '{name}' because it was declared as a type."
                );

                return false;
        }

        AddReference(interfaceInvocation.Name, symbol);
        if (typeSymbol != null)
            AddReference(interfaceInvocation.Name, typeSymbol);

        return base.VisitInterfaceInvocation(interfaceInvocation);
    }

    private bool ResolveTraitBody(TraitBody body, string name)
    {
        var methodNames = body.Members.Select(p => p.Name.Text).ToList();
        var duplicates = methodNames.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count <= 0)
            return true;

        foreach (var duplicate in duplicates)
        {
            var property = body.Members.FindLast(m => m.Name.Text == duplicate)!;
            _diagnostics.Error(property, InternalCodes.DuplicateName, $"Method '{duplicate}' already exists on trait '{name}'");
        }

        return false;
    }

    private bool ResolveInterfaceBody(InterfaceBody? body, InterfaceSymbol interfaceSymbol)
    {
        if (body == null)
            return true;
        
        var mapped = body.Members.OfType<MappedTypeDeclaration>().ToList();
        if (mapped.Count > 0 && body.Members.Count > 1)
        {
            foreach (var extra in body.Members.Where(member => member != mapped[0]))
                _diagnostics.Error(
                    extra,
                    InternalCodes.InvalidMappedType,
                    $"Mapped type '{interfaceSymbol.Name}' cannot declare members of its own.",
                    "every member it has comes from the keys it maps over"
                );

            return false;
        }

        var indexers = body.Members.OfType<IndexerDeclaration>().ToList();
        if (indexers.Count > 1)
        {
            foreach (var extraIndexer in indexers.Skip(1))
                _diagnostics.Error(extraIndexer, InternalCodes.DuplicateIndexer, $"Type '{interfaceSymbol.Name}' may only have one indexer.");

            return false;
        }

        var properties = body.Members.OfType<PropertyDeclaration>().ToList();
        foreach (var symbol in
                 from property in properties
                 let attributeSymbols = property.Attributes?.AttributeList.Select(DeclareAttribute).ToList() ?? []
                 let propertyType = property.ColonTypeClause.Type is OptionalType optionalType ? optionalType.NonNullableType : property.ColonTypeClause.Type
                 let pointsTo = _semanticModel.GetSymbol(propertyType, SymbolKind.Interface) as InterfaceSymbol
                 select new PropertySymbol(property, pointsTo, attributeSymbols) { IsIntrinsic = interfaceSymbol.IsIntrinsic, IsStatic = property.IsStatic })
        {
            interfaceSymbol.Properties.Add(symbol);
            AddDeclaration(symbol);
        }

        // A non-ambient interface already gets a same-named value symbol from hoisting
        // (HoistDeclarations), regardless of whether it declares statics - that symbol is what
        // 'new Foo { ... }' resolves 'Foo' against. An ambient one does not, since hoisting only
        // declares the type for a 'declare interface'; a static member is otherwise unreachable as a
        // value ('Vector2::create' has nothing to look 'Vector2' up as), so this is where it is added -
        // harmlessly hitting the 'AlreadyHoisted' case for the non-ambient path, and reporting the
        // ordinary duplicate-symbol diagnostic when an explicit 'let'/import already claims the name.
        if (properties.Any(property => property.IsStatic) && !DeclareVariable((InterfaceDeclaration)interfaceSymbol.Declaration))
            return false;

        var events = body.Members.OfType<EventDeclaration>().ToList();
        foreach (var symbol in
                 from eventDeclaration in events
                 let attributeSymbols = eventDeclaration.Attributes?.AttributeList.Select(DeclareAttribute).ToList() ?? []
                 select new EventSymbol(eventDeclaration, attributeSymbols) { IsIntrinsic = interfaceSymbol.IsIntrinsic })
        {
            interfaceSymbol.Properties.Add(symbol);
            AddDeclaration(symbol);
        }

        var duplicateGroups = properties.GroupBy(p => p.Name.Text).Where(g => g.Count() > 1).ToList();
        var invalidDuplicateGroups = duplicateGroups.Where(g => !g.All(p => p.ColonTypeClause.Type is FunctionType)).ToList();
        if (invalidDuplicateGroups.Count <= 0)
            return true;

        foreach (var group in invalidDuplicateGroups)
            _diagnostics.Error(group.Last(), InternalCodes.DuplicateName, $"Property '{group.Key}' already exists on type '{interfaceSymbol.Name}'");

        return false;
    }

    private bool ResolveInterfaceConstraints(ColonTypeListClause? colonTypeListClause, InterfaceSymbol symbol)
    {
        if (colonTypeListClause == null)
            return true;

        foreach (var constraint in colonTypeListClause.Types)
        {
            if (constraint is not TypeName typeName)
                return ReportNonInterfaceConstraint(constraint);

            var constraintSymbol = LookupTypeSymbol(typeName.Name.Text);
            if (constraintSymbol is not InterfaceSymbol interfaceSymbol)
                return ReportNonInterfaceConstraint(constraint);

            if (!interfaceSymbol.IsSealed) continue;
            _diagnostics.Error(
                constraint,
                InternalCodes.InheritFromSealed,
                $"Cannot constrain interface '{symbol.Name}' with sealed interface '{interfaceSymbol.Name}'."
            );

            return false;
        }

        return true;
    }

    private bool DeclareTrait(TraitDeclaration traitDeclaration)
    {
        var scope = CurrentScope();
        var name = traitDeclaration.Name.Text;
        if (scope.Lookup(SymbolNamespace.Type).TryGetValue(name, out var symbols))
        {
            if (IsAlreadyHoisted(traitDeclaration, symbols))
                return true;

            var kindName = symbols is [.., TraitSymbol] ? "Trait" : "Type";
            _diagnostics.Error(traitDeclaration.Name, InternalCodes.DuplicateName, $"{kindName} '{name}' is already declared in this scope.");
            return false;
        }

        DeclareSymbol(new TraitSymbol(traitDeclaration, name));
        return true;
    }

    private InterfaceSymbol? DeclareInterface(InterfaceDeclaration interfaceDeclaration, bool isSealed)
    {
        var scope = CurrentScope();
        var name = interfaceDeclaration.Name.Text;
        if (scope.Lookup(SymbolNamespace.Type).TryGetValue(name, out var symbols))
        {
            if (IsAlreadyHoisted<InterfaceSymbol>(interfaceDeclaration, symbols, out var interfaceSymbol))
                return interfaceSymbol;

            var kindName = symbols is [.., InterfaceSymbol] ? "Interface" : "Type";
            _diagnostics.Error(interfaceDeclaration.Name, InternalCodes.DuplicateName, $"{kindName} '{name}' is already declared in this scope.");
            return null;
        }

        var constraints = interfaceDeclaration.ColonTypeListClause?.Types
            .Select(t => t is TypeName c ? LookupSymbol(c.Name.Text, SymbolKind.Interface) : null)
            .OfType<InterfaceSymbol>()
            .ToList();

        var finalSymbol = new InterfaceSymbol(interfaceDeclaration, name, isSealed, constraints);
        DeclareSymbol(finalSymbol);
        return finalSymbol;
    }

    private AttributeSymbol DeclareAttribute(Attribute attribute)
    {
        var name = attribute.Name!;
        var declarationSymbol = LookupSymbol(name, SymbolKind.Function);
        return new AttributeSymbol(attribute, name) { IsIntrinsic = declarationSymbol?.IsIntrinsic ?? false };
    }

    private bool ReportNonInterfaceConstraint(TypeExpression constraint)
    {
        _diagnostics.Error(constraint, InternalCodes.NonInterfaceConstraint, "Interfaces may only be constrained by other interfaces.");
        return false;
    }
}
