using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving.Symbols;
using Loom.Core.Text;
using Attribute = Loom.Core.Parsing.AST.Attribute;

namespace Loom.Core.Resolving;

public sealed partial class Resolver
{
    public override bool VisitImplement(Implement implement)
    {
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

        foreach (var methodName in traitSymbol.MethodNames.Where(methodName => implement.Body.Implementations.All(i => methodName != i.Name.Text)))
        {
            _diagnostics.Error(
                implement,
                InternalCodes.MissingImplementation,
                $"Implementation of trait '{traitSymbol.Name}' on interface '{interfaceSymbol.Name}' is missing method '{methodName}'"
            );

            return false;
        }

        PushScope();
        interfaceSymbol.Implementations.Add(implement);
        interfaceSymbol.Implements.Add(traitSymbol);
        traitSymbol.ImplementedBy.Add(interfaceSymbol);
        var success = interfaceSymbol.FullProperties
            .All(property => DeclareVariable(implement, new InjectedPropertyVariableSymbol(implement, property.Name, interfaceSymbol, property.IsMutable)));

        // A bare call to a method from another trait already implemented on this interface resolves the
        // same way a bare call to one of THIS block's own methods already does (as an ordinary function
        // symbol), so it compiles through the same self+colon-call codegen path without any changes there.
        // Only traits implemented earlier in the file are visible here, matching the same source-order
        // dependency '@.method()' already has for cross-trait access.
        if (success)
        {
            var otherMethods = interfaceSymbol.FullImplementations
                .Where(other => other != implement)
                .SelectMany(other => other.Body.Implementations);

            success = otherMethods.All(declaration => DeclareVariable(declaration, new FunctionSymbol(declaration, declaration.Name.Text)));
        }

        if (success)
            Visit(implement.Body);

        PopScope();
        return success;
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

        if (selfExpression.Parent is TypePredicateType)
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

        PushScope();
        base.VisitTraitDeclaration(traitDeclaration);
        PopScope();

        return true;
    }

    public override bool VisitInterfaceDeclaration(InterfaceDeclaration interfaceDeclaration)
    {
        var isSealed = interfaceDeclaration.SealedKeyword != null;
        if (!DeclareVariable(interfaceDeclaration)
            || DeclareInterface(interfaceDeclaration, isSealed) is not { } symbol
            || !ResolveInterfaceConstraints(interfaceDeclaration.ColonTypeListClause, symbol))
            return false;

        PushScope();
        base.VisitInterfaceDeclaration(interfaceDeclaration);
        PopScope();

        return ResolveInterfaceBody(interfaceDeclaration.Body, symbol);
    }

    public override bool VisitDeclare(Declare declare)
    {
        var lastContext = _context;
        _context = ResolverContext.Ambient;

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
                PushScope();
                result &= base.VisitInterfaceDeclaration(interfaceDeclaration);
                PopScope();
                result &= ResolveInterfaceBody(interfaceDeclaration.Body, interfaceSymbol);
            }
        }
        else
        {
            result = Visit(declare.Signature);
        }

        _context = lastContext;
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
                 select new PropertySymbol(property, pointsTo, attributeSymbols) { IsIntrinsic = interfaceSymbol.IsIntrinsic })
        {
            interfaceSymbol.Properties.Add(symbol);
            AddDeclaration(symbol);
        }

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
        var name = attribute.Expression.Tokens.Last(t => t.Kind == SyntaxKind.Identifier).Text;
        var declarationSymbol = LookupSymbol(name, SymbolKind.Function);
        return new AttributeSymbol(attribute, name) { IsIntrinsic = declarationSymbol?.IsIntrinsic ?? false };
    }

    private bool ReportNonInterfaceConstraint(TypeExpression constraint)
    {
        _diagnostics.Error(constraint, InternalCodes.NonInterfaceConstraint, "Interfaces may only be constrained by other interfaces.");
        return false;
    }
}
