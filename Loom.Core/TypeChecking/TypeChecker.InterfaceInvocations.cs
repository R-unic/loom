using System.Diagnostics.CodeAnalysis;
using Loom.Core.Diagnostics;
using Loom.Core.FlowAnalysis;
using Loom.Core.Parsing.AST;
using Loom.Core.Resolving.Symbols;
using Loom.Core.TypeChecking.Types;
using PrimitiveType = Loom.Core.TypeChecking.Types.PrimitiveType;
using Type = Loom.Core.TypeChecking.Types.Type;
using Loom.Core.TypeChecking.Intrinsic;

namespace Loom.Core.TypeChecking;

public sealed partial class TypeChecker
{
    public override Type VisitInterfaceInvocation(InterfaceInvocation interfaceInvocation) =>
        CheckOrVisitInterfaceInvocation(interfaceInvocation, null);

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
            traitProperties.AddRange(CollectEffectiveTraitProperties(interfaceSymbol.Implementations));

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
            IteratedElementType = interfaceType.IteratedElementType,
            IsIntrinsic = interfaceType.IsIntrinsic
        };

        return BindType(node, boundType);
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
        substituted = new InterfaceType(underlying.Name, underlying.Constraints, substitutedObject)
        {
            Metamethods = underlying.Metamethods,
            IteratedElementType = underlying.IteratedElementType,
            IsIntrinsic = underlying.IsIntrinsic
        };
        return true;
    }

    private void CheckInterfaceInvocationInitializers(InterfaceInvocation node, InterfaceType interfaceType)
    {
        var objectType = interfaceType.ObjectType;
        var providedProperties = new HashSet<string>();
        foreach (var property in node.Body.Initializers.SelectMany(initializer => CheckInterfaceInvocationInitializer(interfaceType, initializer)))
            providedProperties.Add(property);

        foreach (var property in objectType.Properties.Where(property => !property.IsStatic && !providedProperties.Contains(property.Name)))
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

        if (property.IsStatic)
        {
            _diagnostics.Error(
                node,
                InternalCodes.StaticMemberInObjectLiteral,
                $"'{name}' is a static member of '{interfaceType.Name}' - it cannot be set on an instance literal.",
                $"assign it in a 'static {interfaceType.Name} {{ ... }}' block instead"
            );

            Check(expression, property.ValueType);
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
