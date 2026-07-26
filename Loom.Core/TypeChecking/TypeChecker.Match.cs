using Loom.Core.Diagnostics;
using Loom.Core.FlowAnalysis;
using Loom.Core.Parsing.AST;
using Loom.Core.TypeChecking.Types;
using ArrayType = Loom.Core.TypeChecking.Types.ArrayType;
using LiteralType = Loom.Core.TypeChecking.Types.LiteralType;
using PrimitiveType = Loom.Core.TypeChecking.Types.PrimitiveType;
using Type = Loom.Core.TypeChecking.Types.Type;
using UnionType = Loom.Core.TypeChecking.Types.UnionType;

namespace Loom.Core.TypeChecking;

public sealed partial class TypeChecker
{
    public override Type VisitMatchExpression(MatchExpression matchExpression)
    {
        var scrutineeType = Visit(matchExpression.Expression);
        if (matchExpression.Arms.Count == 0)
            return BindType(matchExpression, PrimitiveType.Never);

        var armTypes = new List<Type>(matchExpression.Arms.Count);
        foreach (var arm in matchExpression.Arms)
            armTypes.Add(CheckMatchArm(arm, scrutineeType, null));

        CheckExhaustiveness(matchExpression);

        return BindType(matchExpression, TypeSimplifier.Simplify(new UnionType(armTypes)));
    }

    /// <summary>
    ///     Tier 0 exhaustiveness: a match must contain at least one irrefutable arm (a bare identifier,
    ///     <c>let</c>, or wildcard pattern with no guard), otherwise the compiled match can fall through
    ///     and leave its result nil at runtime.
    /// </summary>
    private void CheckExhaustiveness(MatchExpression matchExpression)
    {
        if (matchExpression.Arms.Exists(IsIrrefutableArm))
            return;

        _diagnostics.Error(
            matchExpression,
            InternalCodes.NonExhaustiveMatch,
            "Match expression is not exhaustive.",
            "add a wildcard arm ('_ -> ...') or a binding arm to cover the remaining cases."
        );
    }

    private static bool IsIrrefutableArm(MatchArm arm) => arm.Guard == null && IsIrrefutablePattern(arm.Pattern);

    private static bool IsIrrefutablePattern(Pattern pattern) =>
        pattern switch
        {
            WildcardPattern or IdentifierPattern or LetPattern => true,
            OrPattern orPattern => orPattern.Patterns.Exists(IsIrrefutablePattern),
            _ => false
        };

    private Type CheckMatchArm(MatchArm matchArm, Type scrutineeType, Type? expected)
    {
        CheckPattern(matchArm.Pattern, scrutineeType);

        if (matchArm.Guard != null)
        {
            var guardType = Visit(matchArm.Guard, null);
            _semanticModel.TypeSolver.AddConstraint(guardType, PrimitiveType.Bool, matchArm.Guard);
        }

        if (expected == null)
            return Visit(matchArm.Body, null);

        var baseState = _flowAnalyzer.GetState(matchArm.Body);
        var armState = new FlowState(
            baseState.DefinitelyInitialized,
            baseState.MaybeInitialized,
            baseState.IsUnreachable,
            _flowState.NarrowedTypes
        );

        return Check(matchArm.Body, expected, armState);
    }

    private void CheckPattern(Pattern pattern, Type inputType)
    {
        switch (pattern)
        {
            case WildcardPattern wildcardPattern:
                BindType(wildcardPattern, inputType);
                break;
            case IdentifierPattern identifierPattern:
                BindType(identifierPattern, inputType);
                break;
            case LetPattern letPattern:
                BindType(letPattern, inputType);
                break;
            case LiteralPattern literalPattern:
                CheckLiteralPattern(literalPattern, inputType);
                break;
            case RangePattern rangePattern:
                CheckRangePattern(rangePattern, inputType);
                break;
            case TypedPattern typedPattern:
                CheckTypedPattern(typedPattern, inputType);
                break;
            case TypePattern typePattern:
                CheckTypePattern(typePattern, inputType);
                break;
            case ObjectPattern objectPattern:
                CheckObjectPattern(objectPattern, inputType);
                break;
            case ArrayPattern arrayPattern:
                CheckArrayPattern(arrayPattern, inputType);
                break;
            case OrPattern orPattern:
                CheckOrPattern(orPattern, inputType);
                break;
            case NullPattern nullPattern:
                BindType(nullPattern, PrimitiveType.Never);
                break;
        }
    }

    private void CheckLiteralPattern(LiteralPattern pattern, Type inputType)
    {
        var literalType = new LiteralType(pattern.Value);
        if (!IsPatternCompatible(literalType, inputType))
            _diagnostics.Error(
                pattern,
                InternalCodes.TypeMismatch,
                $"Pattern of type '{literalType}' cannot match value of type '{inputType}'."
            );

        BindType(pattern, literalType);
    }

    private void CheckRangePattern(RangePattern pattern, Type inputType)
    {
        BindType(pattern.Minimum, PrimitiveType.Number);
        BindType(pattern.Maximum, PrimitiveType.Number);
        if (!IsPatternCompatible(PrimitiveType.Number, inputType))
            _diagnostics.Error(
                pattern,
                InternalCodes.TypeMismatch,
                $"Range pattern can only match values of type 'number', not '{inputType}'."
            );

        BindType(pattern, PrimitiveType.Number);
    }

    private void CheckTypedPattern(TypedPattern pattern, Type inputType)
    {
        var patternType = Visit(pattern.Type);
        var matchedType = NarrowToType(inputType, patternType);
        BindType(pattern, matchedType);
        if (pattern.ObjectPattern != null)
            CheckObjectPattern(pattern.ObjectPattern, matchedType);
    }

    private void CheckTypePattern(TypePattern pattern, Type inputType)
    {
        var patternType = Visit(pattern.Type);
        var matchedType = NarrowToType(inputType, patternType);
        BindType(pattern, matchedType);
        if (pattern.ObjectPattern != null)
            CheckObjectPattern(pattern.ObjectPattern, matchedType);
    }

    private void CheckObjectPattern(ObjectPattern pattern, Type inputType)
    {
        foreach (var field in pattern.Fields)
            CheckObjectPatternField(field, inputType);

        BindType(pattern, inputType);
    }

    private void CheckObjectPatternField(ObjectPatternField field, Type inputType)
    {
        var propertyType = TypeSimplifier.GetMemberPropertyType(inputType, field.Name.Text);
        if (propertyType == null)
        {
            if (Type.IsNotUnknown(inputType) && Type.IsNotNever(inputType))
                _diagnostics.Error(
                    field,
                    InternalCodes.InvalidAccess,
                    $"Property '{field.Name.Text}' does not exist on type '{inputType}'."
                );

            propertyType = PrimitiveType.Unknown;
        }

        CheckPattern(field.Pattern, propertyType);
    }

    private void CheckArrayPattern(ArrayPattern pattern, Type inputType)
    {
        var elementType = GetArrayElementType(inputType);
        if (elementType == null)
        {
            if (Type.IsNotUnknown(inputType) && Type.IsNotNever(inputType))
                _diagnostics.Error(
                    pattern,
                    InternalCodes.TypeMismatch,
                    $"Array pattern cannot match value of type '{inputType}'."
                );

            elementType = PrimitiveType.Unknown;
        }

        foreach (var element in pattern.Elements)
            CheckPattern(element, elementType);

        if (pattern.Rest != null)
            CheckRestPattern(pattern.Rest, elementType);

        BindType(pattern, inputType);
    }

    private void CheckRestPattern(RestPattern pattern, Type elementType)
    {
        var arrayType = new ArrayType(elementType, false);
        CheckPattern(pattern.Pattern, arrayType);
        BindType(pattern, arrayType);
    }

    private void CheckOrPattern(OrPattern pattern, Type inputType)
    {
        foreach (var alternative in pattern.Patterns)
            CheckPattern(alternative, inputType);

        BindType(pattern, inputType);
    }

    private static Type NarrowToType(Type inputType, Type patternType)
    {
        if (inputType is UnionType union)
        {
            var members = union.Types.FindAll(member => member.IsAssignableTo(patternType));
            if (members.Count > 0)
                return TypeSimplifier.Simplify(new UnionType(members));
        }

        return patternType;
    }

    private static Type? GetArrayElementType(Type type)
    {
        if (type is InstantiatedType instantiated)
            type = instantiated.Expand();

        return type is ArrayType array ? array.ElementType : null;
    }

    /// <summary>
    ///     A pattern only has to be able to match <em>some</em> value the scrutinee can hold, so both
    ///     sides are widened first: matching a literal scrutinee like `match 1` against `0` is a normal
    ///     (if never-taken) arm rather than a type error, while `match "hi"` against `0` still fails
    ///     because no widening makes a number and a string overlap.
    /// </summary>
    private static bool IsPatternCompatible(Type patternType, Type inputType)
    {
        if (Type.IsUnknown(inputType) || Type.IsNever(inputType))
            return true;

        var widenedPattern = patternType.Widen();
        var widenedInput = inputType.Widen();
        return widenedPattern.IsAssignableTo(widenedInput) || widenedInput.IsAssignableTo(widenedPattern);
    }
}