using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.TypeChecking.Types;
using LiteralType = Loom.Core.TypeChecking.Types.LiteralType;
using PrimitiveType = Loom.Core.TypeChecking.Types.PrimitiveType;
using Type = Loom.Core.TypeChecking.Types.Type;

namespace Loom.Core.TypeChecking;

public sealed partial class TypeChecker
{
    public override Type VisitEnumDeclaration(EnumDeclaration enumDeclaration)
    {
        var baseType = MaybeVisit(enumDeclaration.ColonTypeClause) ?? PrimitiveType.Number;
        if (enumDeclaration.ColonTypeClause != null
            && !baseType.IsAssignableTo(PrimitiveType.String)
            && !baseType.IsAssignableTo(PrimitiveType.Number))
        {
            _diagnostics.Error(
                enumDeclaration.ColonTypeClause,
                InternalCodes.InvalidEnumBaseType,
                "Invalid enum base type.",
                "valid types are 'string' and 'number'"
            );

            return BindType(enumDeclaration, PrimitiveType.Never);
        }

        var properties = baseType.IsAssignableTo(PrimitiveType.String)
            ? BuildStringEnumProperties(enumDeclaration, baseType)
            : BuildNumericEnumProperties(enumDeclaration, baseType);

        return properties != null
            ? BindType(enumDeclaration, new ObjectType(null, properties))
            : BindType(enumDeclaration, PrimitiveType.Never);
    }

    private List<ObjectProperty> BuildNumericEnumProperties(EnumDeclaration enumDeclaration, Type baseType)
    {
        var properties = new List<ObjectProperty>();
        var nextValue = 0d;
        foreach (var member in enumDeclaration.Members)
        {
            var memberValue = nextValue;
            if (member.EqualsValueClause != null && CheckEnumMemberIsConstant(member, member.EqualsValueClause.Value))
            {
                var explicitType = Visit(member.EqualsValueClause);
                memberValue = ExtractNumericLiteralValue(explicitType, nextValue);
                _semanticModel.TypeSolver.AddConstraint(explicitType, baseType, member.EqualsValueClause.Value);
            }

            properties.Add(new ObjectProperty(false, member.Name.Text, new LiteralType(memberValue)));
            nextValue = Math.Floor(memberValue + 1);
        }

        return properties;
    }

    private List<ObjectProperty>? BuildStringEnumProperties(EnumDeclaration enumDeclaration, Type baseType)
    {
        var properties = new List<ObjectProperty>();
        foreach (var member in enumDeclaration.Members)
        {
            if (member.EqualsValueClause == null)
            {
                _diagnostics.Error(
                    member,
                    InternalCodes.StringEnumMemberMustHaveInitializer,
                    $"Member '{member.Name.Text}' of string enum '{enumDeclaration.Name.Text}' must have an initializer."
                );

                return null;
            }

            if (!CheckEnumMemberIsConstant(member, member.EqualsValueClause.Value)) continue;

            var type = Visit(member.EqualsValueClause);
            _semanticModel.TypeSolver.AddConstraint(type, baseType, member.EqualsValueClause.Value);
            properties.Add(new ObjectProperty(false, member.Name.Text, type));
        }

        return properties;
    }

    private static double ExtractNumericLiteralValue(Type type, double fallback) =>
        type switch
        {
            LiteralType { Value: long l } => l,
            LiteralType { Value: int i } => i,
            LiteralType { Value: double d } => d,
            _ => fallback
        };

    private bool CheckEnumMemberIsConstant(EnumMember member, Expression expression)
    {
        if (_semanticModel.IsCompileTimeConstant(expression))
            return true;

        _diagnostics.Error(
            member.EqualsValueClause!.Value,
            InternalCodes.DynamicEnumMemberInitializer,
            "Enum member initializers must be constant values."
        );

        return false;
    }
}