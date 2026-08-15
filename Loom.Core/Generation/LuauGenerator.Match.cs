using Loom.Core.Parsing.AST;
using Loom.Core.TypeChecking.Types;
using Loom.Luau.AST;
using BinaryOperator = Loom.Luau.AST.BinaryOperator;
using ExpressionStatement = Loom.Luau.AST.ExpressionStatement;
using FunctionType = Loom.Core.TypeChecking.Types.FunctionType;
using Identifier = Loom.Luau.AST.Identifier;
using LiteralType = Loom.Core.TypeChecking.Types.LiteralType;
using PrimitiveType = Loom.Core.TypeChecking.Types.PrimitiveType;
using PrimitiveTypeKind = Loom.Core.TypeChecking.Types.PrimitiveTypeKind;
using Type = Loom.Core.TypeChecking.Types.Type;
using UnionType = Loom.Core.TypeChecking.Types.UnionType;
using IntersectionType = Loom.Core.TypeChecking.Types.IntersectionType;

namespace Loom.Core.Generation;

public sealed partial class LuauGenerator
{
    // Set while compiling a guard, so it sees the pattern's bound names as subject accesses instead of
    // the not-yet-declared locals (those are only introduced in the arm body, after the guard passes).
    private Dictionary<string, LuauExpression>? _guardSubstitutions;

    public override LuauNode VisitMatchExpression(MatchExpression matchExpression)
    {
        if (matchExpression.Arms.Count == 0)
            return new NilLiteral();

        // A match with only a single, unguarded wildcard arm always takes that arm no matter what the
        // scrutinee is, so the subject/local-match scaffolding - and evaluating the scrutinee at all -
        // can be skipped entirely in favor of just the arm's body.
        if (matchExpression.Arms is [{ Pattern: WildcardPattern, Guard: null } soleArm])
            return Visit(soleArm.Body);

        var subject = _state.PushToVariable("_subject", Visit(matchExpression.Expression));
        var matchName = _state.Scope.AddIdentifier("_match");
        var matchIdentifier = new Identifier(matchName);
        _state.Prereq(new LocalVariable(matchName, null, null));

        LuauExpression? thenCondition = null;
        Chunk? thenBranch = null;
        var elseIfBranches = new List<ElseIfBranch>();
        Chunk? elseBranch = null;

        foreach (var arm in matchExpression.Arms)
        {
            var conditions = new List<LuauExpression>();
            var bindings = new List<LuauStatement>();
            if (!TryCompilePattern(arm.Pattern, subject, conditions, bindings, out var isIrrefutable))
                continue;

            if (arm.Guard != null)
            {
                conditions.Add(CompileGuard(arm.Pattern, arm.Guard, subject));
                isIrrefutable = false;
            }

            var armChunk = CompileMatchArmBody(arm, matchIdentifier, bindings);
            if (isIrrefutable)
            {
                elseBranch = armChunk;
                break;
            }

            var condition = CombineWith(conditions, "and");
            if (thenCondition == null)
            {
                thenCondition = condition;
                thenBranch = armChunk;
            }
            else
            {
                elseIfBranches.Add(new ElseIfBranch(condition, armChunk));
            }
        }

        if (thenCondition == null)
        {
            if (elseBranch != null)
                _state.Prereq([..elseBranch.Statements]);
        }
        else
        {
            _state.Prereq(new IfStatement(thenCondition, thenBranch!, elseIfBranches, elseBranch));
        }

        return matchIdentifier;
    }

    private LuauExpression CompileGuard(Pattern pattern, Expression guard, LuauExpression subject)
    {
        var substitutions = CollectPatternBindings(pattern, subject).ToDictionary(binding => binding.Name, binding => binding.Value);
        if (substitutions.Count == 0)
            return Visit(guard);

        var previous = _guardSubstitutions;
        _guardSubstitutions = substitutions;
        try
        {
            return Visit(guard);
        }
        finally
        {
            _guardSubstitutions = previous;
        }
    }

    /// <summary>
    ///     Mirrors the binding side of <see cref="TryCompilePattern" /> - without any of the runtime
    ///     conditions - so a guard can reference names a compound pattern (array/object/tuple/typed/etc.)
    ///     would bind, before those bindings are actually declared as locals in the arm body.
    /// </summary>
    private IEnumerable<(string Name, LuauExpression Value)> CollectPatternBindings(Pattern pattern, LuauExpression subject)
    {
        switch (pattern)
        {
            case IdentifierPattern identifierPattern:
                yield return (identifierPattern.Name.Text, subject);

                break;

            case LetPattern letPattern:
                yield return (letPattern.Name.Text, subject);

                break;

            case TypedPattern typedPattern:
                yield return (typedPattern.Name.Text, subject);
                if (typedPattern.ObjectPattern != null)
                    foreach (var binding in CollectObjectPatternBindings(typedPattern.ObjectPattern, subject))
                        yield return binding;

                break;

            case TypePattern { ObjectPattern: not null } typePattern:
                foreach (var binding in CollectObjectPatternBindings(typePattern.ObjectPattern, subject))
                    yield return binding;

                break;

            case ArrayPattern arrayPattern:
                for (var i = 0; i < arrayPattern.Elements.Count; i++)
                {
                    var elementAccess = new Luau.AST.ElementAccess(subject, new NumberLiteral(i + 1));
                    foreach (var binding in CollectPatternBindings(arrayPattern.Elements[i], elementAccess))
                        yield return binding;
                }

                if (arrayPattern.Rest != null)
                {
                    var rest = BuildArrayRestSlice(subject, arrayPattern.Elements.Count);
                    foreach (var binding in CollectPatternBindings(arrayPattern.Rest.Pattern, rest))
                        yield return binding;
                }

                break;

            case ObjectPattern objectPattern:
                foreach (var binding in CollectObjectPatternBindings(objectPattern, subject))
                    yield return binding;

                break;

            case TuplePattern tuplePattern:
                for (var i = 0; i < tuplePattern.Patterns.Count; i++)
                {
                    var elementAccess = new Luau.AST.ElementAccess(subject, new NumberLiteral(i + 1));
                    foreach (var binding in CollectPatternBindings(tuplePattern.Patterns[i], elementAccess))
                        yield return binding;
                }

                break;

            case AndPattern andPattern:
                foreach (var binding in CollectPatternBindings(andPattern.Pattern, subject))
                    yield return binding;

                break;

            // Every alternative binds the same names against the same subject, so any one will do.
            case OrPattern { Patterns: [var first, ..] }:
                foreach (var binding in CollectPatternBindings(first, subject))
                    yield return binding;

                break;
        }
    }

    private IEnumerable<(string Name, LuauExpression Value)> CollectObjectPatternBindings(ObjectPattern objectPattern, LuauExpression subject)
    {
        var patternType = _semanticModel.GetType(objectPattern);
        foreach (var field in objectPattern.Fields)
        {
            var name = GetRenamedPatternFieldName(patternType, field.Name.Text);
            var propertyAccess = new Luau.AST.PropertyAccess(subject, [name]);
            foreach (var binding in CollectPatternBindings(field.Pattern, propertyAccess))
                yield return binding;
        }
    }

    private Chunk CompileMatchArmBody(MatchArm arm, Identifier matchIdentifier, List<LuauStatement> bindings)
    {
        var statements = new List<LuauStatement>();
        var (body, scope) = _state.Capture(() =>
            {
                foreach (var binding in bindings)
                    _state.Prereq(binding);

                return Visit(arm.Body);
            }
        );

        ApplyPrereqAndPostreq(
            statements,
            scope,
            new ExpressionStatement(new BinaryOperator(matchIdentifier, "=", body))
        );

        return new Chunk(statements);
    }

    /// <summary>
    ///     Compiles a pattern against <paramref name="subject" />, appending any runtime checks the
    ///     pattern requires to <paramref name="conditions" /> (AND-ed together by the caller) and any
    ///     variable bindings the pattern introduces to <paramref name="bindings" /> (emitted inside the
    ///     arm body, after the conditions have already passed). Returns false - and reports a diagnostic
    ///     - for pattern kinds that still aren't supported, so the caller can skip the arm.
    /// </summary>
    private bool TryCompilePattern(Pattern pattern, LuauExpression subject, List<LuauExpression> conditions, List<LuauStatement> bindings, out bool isIrrefutable)
    {
        switch (pattern)
        {
            case WildcardPattern:
                isIrrefutable = true;
                return true;

            case IdentifierPattern identifierPattern:
                bindings.Add(new ConstVariable(identifierPattern.Name.Text, null, subject));
                isIrrefutable = true;
                return true;

            case LetPattern letPattern:
                bindings.Add(new ConstVariable(letPattern.Name.Text, null, subject));
                isIrrefutable = true;
                return true;

            case LiteralPattern literalPattern:
                conditions.Add(new BinaryOperator(subject, "==", LiteralValueToExpression(literalPattern.Value)));
                isIrrefutable = false;
                return true;

            // Resolves to the same LiteralType a literal pattern would when CheckQualifiedNamePattern
            // accepted it, so it compiles to the identical comparison - the value just came from a name
            // instead of being spelled out. An unresolved name or a non-constant reference already failed
            // type checking and bound something else (never, most often); this only has to not crash on
            // its way to a compile the diagnostic already fails.
            case QualifiedNamePattern qualifiedNamePattern:
            {
                if (_semanticModel.GetType(qualifiedNamePattern) is not LiteralType literalType)
                {
                    isIrrefutable = false;
                    return false;
                }

                conditions.Add(new BinaryOperator(subject, "==", LiteralValueToExpression(literalType.Value)));
                isIrrefutable = false;
                return true;
            }

            case NullPattern:
                conditions.Add(new BinaryOperator(subject, "==", new NilLiteral()));
                isIrrefutable = false;
                return true;

            case RangePattern rangePattern:
                AddTypeofCondition(conditions, PrimitiveType.Number, subject);
                if (rangePattern.Minimum is LiteralPattern minimum)
                    conditions.Add(new BinaryOperator(subject, ">=", LiteralValueToExpression(minimum.Value)));

                if (rangePattern.Maximum is LiteralPattern maximum)
                    conditions.Add(new BinaryOperator(subject, "<=", LiteralValueToExpression(maximum.Value)));

                isIrrefutable = false;
                return true;

            case TypedPattern typedPattern:
            {
                AddTypeCondition(conditions, _semanticModel.GetType(typedPattern.Type), subject, typedPattern.ObjectPattern == null);
                bindings.Add(new ConstVariable(typedPattern.Name.Text, null, subject));
                if (typedPattern.ObjectPattern != null && !CompileObjectPatternFields(typedPattern.ObjectPattern, subject, conditions, bindings))
                {
                    isIrrefutable = false;
                    return false;
                }

                isIrrefutable = false;
                return true;
            }

            case TypePattern typePattern:
            {
                AddTypeCondition(conditions, _semanticModel.GetType(typePattern.Type), subject, typePattern.ObjectPattern == null);
                if (typePattern.ObjectPattern != null && !CompileObjectPatternFields(typePattern.ObjectPattern, subject, conditions, bindings))
                {
                    isIrrefutable = false;
                    return false;
                }

                isIrrefutable = false;
                return true;
            }

            case ArrayPattern arrayPattern:
            {
                conditions.Add(new BinaryOperator(TypeofCall(subject), "==", new StringLiteral("table")));
                for (var i = 0; i < arrayPattern.Elements.Count; i++)
                {
                    var elementAccess = new Luau.AST.ElementAccess(subject, new NumberLiteral(i + 1));
                    if (!TryCompilePattern(arrayPattern.Elements[i], elementAccess, conditions, bindings, out _))
                    {
                        isIrrefutable = false;
                        return false;
                    }
                }

                if (arrayPattern.Rest != null)
                {
                    var rest = BuildArrayRestSlice(subject, arrayPattern.Elements.Count);
                    if (!TryCompilePattern(arrayPattern.Rest.Pattern, rest, conditions, bindings, out _))
                    {
                        isIrrefutable = false;
                        return false;
                    }
                }

                isIrrefutable = false;
                return true;
            }

            case ObjectPattern objectPattern:
                conditions.Add(new BinaryOperator(TypeofCall(subject), "==", new StringLiteral("table")));
                if (!CompileObjectPatternFields(objectPattern, subject, conditions, bindings))
                {
                    isIrrefutable = false;
                    return false;
                }

                isIrrefutable = false;
                return true;

            case TuplePattern tuplePattern:
            {
                conditions.Add(new BinaryOperator(TypeofCall(subject), "==", new StringLiteral("table")));
                for (var i = 0; i < tuplePattern.Patterns.Count; i++)
                {
                    var elementAccess = new Luau.AST.ElementAccess(subject, new NumberLiteral(i + 1));
                    if (!TryCompilePattern(tuplePattern.Patterns[i], elementAccess, conditions, bindings, out _))
                    {
                        isIrrefutable = false;
                        return false;
                    }
                }

                isIrrefutable = false;
                return true;
            }

            case OrPattern orPattern:
            {
                // The resolver requires every alternative to bind the same names (Resolver.Patterns.cs
                // VisitOrPattern), so the first alternative's bindings stand in for whichever one matches.
                var alternativeConditions = new List<LuauExpression>();
                List<LuauStatement>? representativeBindings = null;
                foreach (var alternative in orPattern.Patterns)
                {
                    var altConditions = new List<LuauExpression>();
                    var altBindings = new List<LuauStatement>();
                    if (!TryCompilePattern(alternative, subject, altConditions, altBindings, out var altIrrefutable))
                    {
                        isIrrefutable = false;
                        return false;
                    }

                    representativeBindings ??= altBindings;

                    if (altIrrefutable)
                    {
                        bindings.AddRange(representativeBindings);
                        isIrrefutable = true;
                        return true;
                    }

                    alternativeConditions.Add(CombineWith(altConditions, "and"));
                }

                bindings.AddRange(representativeBindings!);
                conditions.Add(CombineWith(alternativeConditions, "or"));
                isIrrefutable = false;
                return true;
            }

            case AndPattern andPattern:
            {
                if (!TryCompilePattern(andPattern.Pattern, subject, conditions, bindings, out _))
                {
                    isIrrefutable = false;
                    return false;
                }

                conditions.Add(CompileGuard(andPattern.Pattern, andPattern.Guard, subject));
                isIrrefutable = false;
                return true;
            }

            // Bindings from the inner pattern are discarded - nothing is captured when a negated pattern
            // doesn't hold, so there is nothing meaningful for the arm/condition after it to reference.
            case NotPattern notPattern:
            {
                var innerConditions = new List<LuauExpression>();
                var innerBindings = new List<LuauStatement>();
                if (!TryCompilePattern(notPattern.Pattern, subject, innerConditions, innerBindings, out _))
                {
                    isIrrefutable = false;
                    return false;
                }

                conditions.Add(NegateCondition(CombineWith(innerConditions, "and")));
                isIrrefutable = false;
                return true;
            }

            default:
                _diagnostics.NotImplemented(pattern, $"Pattern kind '{pattern.GetType().Name}' is not yet supported in code generation.");
                isIrrefutable = false;
                return false;
        }
    }

    private bool CompileObjectPatternFields(ObjectPattern objectPattern, LuauExpression subject, List<LuauExpression> conditions, List<LuauStatement> bindings)
    {
        var patternType = _semanticModel.GetType(objectPattern);
        return !(
            from field in objectPattern.Fields
            let name = GetRenamedPatternFieldName(patternType, field.Name.Text)
            let propertyAccess = new Luau.AST.PropertyAccess(subject, [name])
            where !TryCompilePattern(field.Pattern, propertyAccess, conditions, bindings, out _)
            select field
        ).Any();
    }

    private string GetRenamedPatternFieldName(Type patternType, string name) =>
        _semanticModel.GetPropertySymbol(patternType, [name]) is { } propertySymbol
        && propertySymbol.TryGetIntrinsicAttribute("luau_name", out var luauNameAttribute)
        && ValidateLuauNameAttribute(luauNameAttribute, out var nameLiteral)
            ? nameLiteral.Value
            : name;

    private static Call BuildArrayRestSlice(LuauExpression subject, int elementCount)
    {
        var length = new Luau.AST.UnaryOperator("#", subject);
        return new Call(
            new Luau.AST.PropertyAccess(new Identifier("table"), ["move"]),
            [subject, new NumberLiteral(elementCount + 1), length, new NumberLiteral(1), new Table([])]
        );
    }

    private static void AddTypeofCondition(List<LuauExpression> conditions, Type type, LuauExpression subject)
    {
        var typeofString = GetLuauTypeofString(type);
        if (typeofString != null)
            conditions.Add(new BinaryOperator(TypeofCall(subject), "==", new StringLiteral(typeofString)));
    }

    private static List<LuauExpression> BuildTypeCondition(Type type, LuauExpression subject, bool checkRequiredFields)
    {
        var conditions = new List<LuauExpression>();
        AddTypeCondition(conditions, type, subject, checkRequiredFields);
        return conditions;
    }

    // `n when Foo` needs more than `typeof(n) == "table"`: a Roblox Instance subclass is checked with
    // `:IsA(...)`, and a plain interface has no runtime representation, so matching one structurally
    // requires checking each of its required fields exists.
    private static void AddTypeCondition(List<LuauExpression> conditions, Type type, LuauExpression subject, bool checkRequiredFields)
    {
        if (type is InstantiatedType instantiated)
            type = instantiated.Expand();

        if (type is UnionType union)
        {
            conditions.Add(CombineWith(union.Types.ConvertAll(member => CombineWith(BuildTypeCondition(member, subject, checkRequiredFields), "and")), "or"));
            return;
        }

        if (type is IntersectionType intersection)
        {
            conditions.AddRange(intersection.Types.SelectMany(member => BuildTypeCondition(member, subject, checkRequiredFields)));
            return;
        }

        if (type is not InterfaceType interfaceType)
        {
            AddTypeofCondition(conditions, type, subject);
            return;
        }

        if (interfaceType.MatchOrMatchConstraint(i => i.Name is "Instance" or "Object"))
        {
            conditions.Add(new BinaryOperator(TypeofCall(subject), "==", new StringLiteral("Instance")));
            conditions.Add(new Call(new Luau.AST.PropertyAccess(subject, ["IsA"]), [new StringLiteral(interfaceType.Name)], true));
            return;
        }

        conditions.Add(new BinaryOperator(TypeofCall(subject), "==", new StringLiteral("table")));
        if (!checkRequiredFields)
            return;

        foreach (var property in interfaceType.Properties.Where(property => Type.IsNotOptional(property.ValueType)))
            conditions.Add(new BinaryOperator(new Luau.AST.PropertyAccess(subject, [property.Name]), "~=", new NilLiteral()));
    }

    private static Call TypeofCall(LuauExpression subject) => new(new Identifier("typeof"), [subject]);

    private static string? GetLuauTypeofString(Type type) =>
        type switch
        {
            PrimitiveType { Kind: PrimitiveTypeKind.Number } => "number",
            PrimitiveType { Kind: PrimitiveTypeKind.String } => "string",
            PrimitiveType { Kind: PrimitiveTypeKind.Bool } => "boolean",
            LiteralType { Value: long or int or double } => "number",
            LiteralType { Value: string } => "string",
            LiteralType { Value: bool } => "boolean",
            FunctionType => "function",
            NativelyIndexableType => "table",
            _ => null
        };

    private static LuauExpression CombineWith(List<LuauExpression> conditions, string @operator) =>
        conditions.Count switch
        {
            0 => new BooleanLiteral(true),
            _ => conditions.Skip(1).Aggregate(conditions[0], (accumulated, next) => new BinaryOperator(accumulated, @operator, next))
        };

    // A single equality comparison flips in place (`typeof(x) == "string"` -> `typeof(x) ~= "string"`);
    // anything else - a compound and/or condition, a bare call like `x:IsA(...)` - is wrapped in `not (...)`
    // rather than distributed via De Morgan's, since the AST here doesn't track operator precedence.
    private static LuauExpression NegateCondition(LuauExpression condition) =>
        condition switch
        {
            BinaryOperator { Operator: "==" } binary => new BinaryOperator(binary.Left, "~=", binary.Right),
            BinaryOperator { Operator: "~=" } binary => new BinaryOperator(binary.Left, "==", binary.Right),
            BinaryOperator => new Luau.AST.UnaryOperator("not ", new Luau.AST.Parenthesized(condition)),
            _ => new Luau.AST.UnaryOperator("not ", condition)
        };

    private static LuauExpression LiteralValueToExpression(object? value) =>
        value switch
        {
            long l => new NumberLiteral(l),
            int i => new NumberLiteral(i),
            double d => new NumberLiteral(d),
            string s => new StringLiteral(s),
            bool b => new BooleanLiteral(b),
            _ => new NilLiteral()
        };
}