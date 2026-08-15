using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Attribute = Loom.Core.Parsing.AST.Attribute;

namespace Loom.Core.TypeChecking;

using Type = Types.Type;

public sealed partial class TypeChecker
{
    [Flags]
    private enum AttributeTargetsFlag
    {
        Function = 1 << 0,
        Interface = 1 << 1,
        Property = 1 << 2,
        Event = 1 << 3
    }

    private bool IsIntrinsicAttribute(Attribute attribute) => _semanticModel.GetSymbol(attribute.Expression)?.IsIntrinsic == true;

    private bool IsMetadataOnlyDecorator(Attribute attribute) =>
        _semanticModel.GetSymbol(attribute.Expression)?.Declaration is IWithAttributes { Attributes: { } declaredAttributes }
        && declaredAttributes.AttributeList.Exists(a => a.Name == "metadata_only");

    private void CheckDecoratorAttribute(Attribute attribute, string valueName, Type valueType)
    {
        if (_resolvingHoisted.Count > 0) return;
        if (IsIntrinsicAttribute(attribute)) return;
        if (_semanticModel.GetType(attribute) is not Types.FunctionType decoratorType) return;

        if (decoratorType.ParameterTypes.Count < 2)
        {
            _diagnostics.Error(
                attribute,
                InternalCodes.InvalidDecorator,
                "Decorators must accept the decorated value and its name as arguments."
            );

            return;
        }

        var thunkType = new Types.FunctionType([], [], valueType);
        var nameType = new Types.LiteralType(valueName);
        var argumentTypes = new List<Type> { thunkType, nameType };
        var substitution = decoratorType.TypeParameters.Count == 0
            ? null
            : TypeInferrer.InferFunctionTypeArguments(decoratorType, argumentTypes);

        var expectedParameterTypes = substitution == null
            ? decoratorType.ParameterTypes
            : SubstituteTypeParameters(attribute, decoratorType.ParameterTypes, substitution);

        if (!thunkType.IsAssignableTo(expectedParameterTypes[0]) || !nameType.IsAssignableTo(expectedParameterTypes[1]))
        {
            _diagnostics.Error(
                attribute,
                InternalCodes.InvalidDecorator,
                $"Decorator is not compatible with '{valueName}' of type '{valueType}'."
            );

            return;
        }

        var resultType = substitution == null ? decoratorType.ReturnType : SubstituteTypeParameters(attribute, decoratorType.ReturnType, substitution);
        if (!resultType.IsAssignableTo(valueType))
            _diagnostics.Error(
                attribute,
                InternalCodes.InvalidDecorator,
                $"Decorator must return a value assignable to '{valueType}', but returns '{resultType}'."
            );
    }

    private void CheckPassiveDecorator(Attribute attribute)
    {
        if (_resolvingHoisted.Count > 0)
            return;

        if (IsIntrinsicAttribute(attribute))
            return;

        foreach (var argument in attribute.Arguments.ArgumentList.Where(argument => !_semanticModel.IsCompileTimeConstant(argument)))
            _diagnostics.Error(argument, InternalCodes.DecoratorArgumentNotConstant, "Decorator arguments must be compile-time constants.");

        var boundType = _semanticModel.GetType(attribute);
        var resultType = !attribute.IsInvoked && boundType is Types.FunctionType functionType ? functionType.ReturnType : boundType;
        if (!resultType.IsAssignableTo(Types.PrimitiveType.Void))
            _diagnostics.Error(attribute, InternalCodes.InvalidDecorator, $"Decorator must return 'void', but returns '{resultType}'.");
    }

    private void CheckAttributeUsage(Attribute attribute, AttributeTargetsFlag currentTarget)
    {
        if (_resolvingHoisted.Count > 0)
            return;

        var referencedSymbol = _semanticModel.GetSymbol(attribute.Expression);
        switch (referencedSymbol)
        {
            case null:
                return;
            case { Name: "attribute_usage", IsIntrinsic: true } when currentTarget != AttributeTargetsFlag.Function:
                _diagnostics.Error(attribute, InternalCodes.AttributeUsageNotOnFunction, "'attribute_usage' may only be applied to a function declaration.");
                return;
        }

        int? allowedFlagsValue;
        if (referencedSymbol.IsIntrinsic)
        {
            allowedFlagsValue = referencedSymbol.AttributeUsageFlags;
        }
        else
        {
            if (referencedSymbol.Declaration is not IWithAttributes { Attributes: { } declaredAttributes })
                return;

            var usageAttribute = declaredAttributes.AttributeList.Find(
                a => a.Name == "attribute_usage"
            );

            allowedFlagsValue = usageAttribute?.Arguments.ArgumentList is [var flagsExpression] && _semanticModel.GetConstantValue(flagsExpression) is double flagsValue
                ? (int)flagsValue
                : null;
        }

        if (allowedFlagsValue is not { } flags)
            return;

        var allowedFlags = (AttributeTargetsFlag)flags;
        if ((allowedFlags & currentTarget) != 0)
            return;

        _diagnostics.Error(
            attribute,
            InternalCodes.AttributeTargetNotAllowed,
            $"Attribute '{referencedSymbol.Name}' is not valid on {currentTarget}."
        );
    }
}
