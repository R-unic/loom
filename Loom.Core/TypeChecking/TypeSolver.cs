using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Text;
using Loom.Core.TypeChecking.Types;
using ArrayType = Loom.Core.TypeChecking.Types.ArrayType;
using FunctionType = Loom.Core.TypeChecking.Types.FunctionType;
using IndexedType = Loom.Core.TypeChecking.Types.IndexedType;
using IntersectionType = Loom.Core.TypeChecking.Types.IntersectionType;
using PrimitiveType = Loom.Core.TypeChecking.Types.PrimitiveType;
using Type = Loom.Core.TypeChecking.Types.Type;
using TypeParameter = Loom.Core.TypeChecking.Types.TypeParameter;
using TypePredicateType = Loom.Core.TypeChecking.Types.TypePredicateType;
using TupleType = Loom.Core.TypeChecking.Types.TupleType;
using UnionType = Loom.Core.TypeChecking.Types.UnionType;

namespace Loom.Core.TypeChecking;

public sealed class TypeSolver(DiagnosticBag diagnostics)
{
    private readonly List<TypeConstraint> _constraints = [];
    private readonly Dictionary<NodeId, Type> _nodeTypes = [];
    private readonly Dictionary<int, Type> _substitutions = [];

    private readonly HashSet<(Type, Type)> _unifyVisiting = new(ReferencePairComparer.Instance);
    private int _nextVariableId;

    public DiagnosticBag Diagnostics { get; } = diagnostics;

    public bool CheckCircular(ref Type type, Token name)
    {
        switch (type)
        {
            case UnionType unionType:
                return CheckCircularMembers(ref type, unionType.Types.ToList(), name, members => new UnionType(members));

            case IntersectionType intersectionType:
                return CheckCircularMembers(ref type, intersectionType.Types.ToList(), name, members => new IntersectionType(members));

            case TypeVariable:
                type = PrimitiveType.Never;
                ReportInfiniteType(name.GetLocation(), name.Text);
                return true;
        }

        return false;
    }

    private bool CheckCircularMembers(ref Type type, List<Type> members, Token name, Func<List<Type>, Type> wrap)
    {
        var circular = false;
        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            if (!CheckCircular(ref member, name)) continue;

            members[i] = member;
            circular = true;
        }

        if (circular)
            type = wrap(members);

        return circular;
    }

    public static Type Transform(Type type, Converter<Type, Type> fn, Type? defaultValue = null, bool simplify = true)
    {
        var changed = false;

        Type Map(Type original)
        {
            var mapped = fn(original);
            changed |= !ReferenceEquals(mapped, original);
            return mapped;
        }

        Type MapDefault()
        {
            if (defaultValue == null || ReferenceEquals(defaultValue, type))
                return type;

            changed = true;
            return defaultValue;
        }

        var transformed = type switch
        {
            IndexedType indexedType => new IndexedType(Map(indexedType.Target), Map(indexedType.Index)),
            ArrayType arrayType => new ArrayType(Map(arrayType.ElementType), arrayType.IsMutable),
            InterfaceType interfaceType => new InterfaceType(
                interfaceType.Name,
                interfaceType.Constraints.ConvertAll(Map).OfType<InterfaceType>().ToList(),
                (ObjectType)Map(interfaceType.ObjectType),
                interfaceType.TraitMethodNames
            ) { Metamethods = interfaceType.Metamethods },
            ObjectType objectType => new ObjectType(
                objectType.Indexer != null
                    ? new ObjectIndexer(objectType.Indexer.IsMutable, Map(objectType.Indexer.KeyType), Map(objectType.Indexer.ValueType))
                    : null,
                objectType.Properties.ConvertAll(p => new ObjectProperty(p.IsMutable, p.Name, Map(p.ValueType)))
            ),
            TupleType tupleType => new TupleType(tupleType.ElementTypes.ConvertAll(Map)),
            IntersectionType intersectionType => new IntersectionType(intersectionType.Types.ConvertAll(Map)),
            UnionType unionType => new UnionType(unionType.Types.ConvertAll(Map)),
            FunctionType functionType => new FunctionType(
                functionType.TypeParameters,
                functionType.ParameterTypes.ConvertAll(Map),
                Map(functionType.ReturnType),
                functionType.HasRestParameter,
                functionType.IsAsync
            ),
            TypePredicateType predicate => new TypePredicateType(predicate.ParameterIndex, Map(predicate.TargetType)),
            GenericType genericType => new GenericType(
                genericType.Declaration,
                genericType.Parameters,
                Map(genericType.UnderlyingType)
            ),
            InstantiatedType instantiatedType => instantiatedType.GenericType.Construct(instantiatedType.Arguments.ConvertAll(Map)),
            _ => MapDefault()
        };

        if (!changed)
            transformed = type;

        return simplify ? TypeSimplifier.Simplify(transformed) : transformed;
    }

    public void SetType(Node node, Type type) => _nodeTypes[node.Id] = type;

    public Type GetType(Node node)
    {
        if (_nodeTypes.TryGetValue(node.Id, out var type))
            return type;

        var variable = CreateTypeVariable();
        _nodeTypes.Add(node.Id, variable);
        return variable;
    }

    public TypeConstraint AddConstraint(Type actual, Type expected, Node node) => AddConstraint(actual, expected, node.LocationSpan);

    public TypeConstraint AddConstraint(Type actual, Type expected, LocationSpan span)
    {
        var constraint = new TypeConstraint(actual, expected, span);
        _constraints.Add(constraint);
        return constraint;
    }

    public bool SolveConstraints()
    {
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var constraint in _constraints)
            {
                var resolvedA = Substitute(constraint.Actual);
                var resolvedB = Substitute(constraint.Expected);
                var trace = TraceThroughExpansion(constraint.Actual, resolvedA, constraint.Expected, resolvedB, constraint.Trace);
                if (!TryUnify(resolvedA, resolvedB, constraint.Span, out var updated, trace))
                    return false;

                if (updated)
                    changed = true;
            }
        }

        ApplySubstitutions();
        return true;
    }

    // Substitute() expands a fully-resolved InstantiatedType (e.g. Box<number>) into its underlying
    // type before unification ever sees it, so UnifyInstantiatedPair's own trace frame never fires for
    // that common case. Preserve the generic-level context here instead, before it's lost to expansion.
    private static TypeMismatchTrace? TraceThroughExpansion(Type originalA, Type resolvedA, Type originalB, Type resolvedB, TypeMismatchTrace? trace) =>
        originalA is InstantiatedType && originalB is InstantiatedType && resolvedA is not InstantiatedType && resolvedB is not InstantiatedType
            ? new TypeMismatchTrace(originalA, originalB, trace)
            : trace;

    private bool TryUnify(Type a, Type b, LocationSpan span, out bool updated, TypeMismatchTrace? trace = null)
    {
        updated = false;
        var pair = (a, b);
        if (!_unifyVisiting.Add(pair))
            return true;

        try
        {
            return (a, b) switch
            {
                (TypeVariable va, TypeVariable vb) => UnifyBothVariables(va, vb, out updated),
                (TypeVariable v, _) => BindVariable(v, b, span, out updated),
                (_, TypeVariable v) => BindVariable(v, a, span, out updated),
                (InstantiatedType i1, InstantiatedType i2) => UnifyInstantiatedPair(i1, i2, span, out updated, trace),
                (FunctionType f1, FunctionType f2) => UnifyFunctionTypes(f1, f2, span, out updated, trace),
                (ArrayType t1, ArrayType t2) => UnifyArrayTypes(t1, t2, span, out updated, trace),
                (ObjectType o1, ObjectType o2) => UnifyObjectTypes(o1, o2, span, out updated, trace),
                // UnifyObjectTypes is directional (a is the source, b is what it must satisfy), so which
                // side is the interface and which is the plain object has to carry through in the same
                // order it arrived in - collapsing both orderings onto one helper with a fixed argument
                // order silently swapped actual and expected for one of the two orderings.
                (ObjectType o, InterfaceType i) => TryUnify(o, i.AssignabilityType, span, out updated, new TypeMismatchTrace(o, i, trace)),
                (InterfaceType i, ObjectType o) => TryUnify(i.AssignabilityType, o, span, out updated, new TypeMismatchTrace(i, o, trace)),
                (InterfaceType i1, InterfaceType i2) => UnifyInterfaceTypes(i1, i2, span, out updated, trace),
                (TypeParameter p1, TypeParameter p2) => UnifyTypeParameters(p1, p2, span, out updated, trace),

                _ when a.IsAssignableTo(b) => true,
                _ => ReportTypeMismatch(a, b, span, trace: trace)
            };
        }
        finally
        {
            _unifyVisiting.Remove(pair);
        }
    }

    private bool UnifyTypeParameters(TypeParameter p1, TypeParameter p2, LocationSpan span, out bool updated, TypeMismatchTrace? trace)
    {
        updated = false;
        if (p1.Constraint != null && p2.Constraint != null && !p1.Constraint.IsAssignableTo(p2.Constraint))
            return ReportTypeMismatch(p1, p2, span, trace: trace);

        if (p1.Constraint == null || p2.Constraint == null)
            return true;

        return TryUnify(p1.Constraint, p2.Constraint, span, out updated, new TypeMismatchTrace(p1, p2, trace));
    }

    // Array element assignability is covariant against an immutable target and invariant against a
    // mutable one (see ArrayType.IsAssignableTo) - mirrored here so the pass/fail outcome never changes,
    // only the diagnostic gains a "why" trace into the element types on failure. Intrinsic array methods
    // (push/pop/join/etc.) are deliberately NOT unified structurally here (unlike UnifyObjectTypes) since
    // they redundantly re-derive the same element-type mismatch through several unrelated-looking paths.
    private bool UnifyArrayTypes(ArrayType a, ArrayType b, LocationSpan span, out bool updated, TypeMismatchTrace? trace)
    {
        updated = false;
        if (a.IsAssignableTo(b))
            return true;

        if (b.IsMutable)
        {
            var reason = !a.IsMutable
                ? "Cannot assign an immutable array to a mutable array type."
                : "Mutable arrays require identical element types, but source and target element types differ.";

            return ReportTypeMismatch(a, b, span, reason, trace);
        }

        return TryUnify(a.ElementType, b.ElementType, span, out updated, new TypeMismatchTrace(a, b, trace));
    }

    private bool UnifyBothVariables(TypeVariable va, TypeVariable vb, out bool updated)
    {
        if (va.Id == vb.Id)
        {
            updated = false;
            return true;
        }

        _substitutions[va.Id] = vb;
        updated = true;
        return true;
    }

    private bool BindVariable(TypeVariable variable, Type type, LocationSpan span, out bool updated)
    {
        updated = false;
        if (OccursIn(variable, type))
            return ReportInfiniteType(span, type.ToString());

        _substitutions[variable.Id] = type;
        updated = true;
        return true;
    }

    private bool UnifyInstantiatedPair(InstantiatedType a, InstantiatedType b, LocationSpan span, out bool updated, TypeMismatchTrace? trace)
    {
        updated = false;
        if (!a.GenericType.Equals(b.GenericType) || a.Arguments.Count != b.Arguments.Count)
            return ReportTypeMismatch(a, b, span, trace: trace);

        var success = true;
        var childTrace = new TypeMismatchTrace(a, b, trace);
        for (var i = 0; i < a.Arguments.Count; i++)
            CombineUnify(a.Arguments[i], b.Arguments[i], span, ref success, ref updated, childTrace);

        return success;
    }

    private bool UnifyObjectTypes(ObjectType a, ObjectType b, LocationSpan span, out bool updated, TypeMismatchTrace? trace)
    {
        updated = false;
        var success = true;
        var childTrace = new TypeMismatchTrace(a, b, trace);

        if (a.Indexer != null && b.Indexer != null)
        {
            CombineUnify(a.Indexer.KeyType, b.Indexer.KeyType, span, ref success, ref updated, childTrace);
            CombineUnify(a.Indexer.ValueType, b.Indexer.ValueType, span, ref success, ref updated, childTrace);

            // Only the unsound direction, matching ObjectType.IsAssignableTo: giving up 'mut' is safe,
            // gaining it is not. Demanding the two agree exactly made this the stricter of two rules for
            // the same question, so whether a pair was accepted depended on whether it reached checking
            // or constraint solving.
            if (!a.Indexer.IsMutable && b.Indexer.IsMutable)
                if (!ReportTypeMismatch(a, b, span, $"Type '{a}' has an immutable indexer, but type '{b}' requires a mutable one.", trace))
                    success = false;
        }
        else if (a.Indexer == null && b.Indexer != null)
        {
            var noIndexerType = a.Indexer == null ? a : b;
            var indexerType = a.Indexer != null ? a : b;
            if (!ReportTypeMismatch(a, b, span, $"Type '{noIndexerType}' is missing indexer from type '{indexerType}'", trace))
                success = false;
        }

        // Directional, like ObjectType.IsAssignableTo: every property b requires must exist on a, but a
        // may have more than b asks for. A name present only on a is excess and not itself a mismatch -
        // skipping BOTH directions here (as a plain Union of the two name sets did) treated two objects
        // with zero overlapping property names as compatible with each other, since neither name matched
        // on both sides and the loop then never touched 'success' at all.
        var aProps = a.Properties.ToDictionary(p => p.Name);
        foreach (var propB in b.Properties)
        {
            if (!aProps.TryGetValue(propB.Name, out var propA))
            {
                if (!ReportTypeMismatch(a, b, span, $"Type '{a}' is missing property '{propB.Name}' required by type '{b}'.", trace))
                    success = false;

                continue;
            }

            CombineUnify(propA.ValueType, propB.ValueType, span, ref success, ref updated, childTrace);

            if (propA.IsMutable || !propB.IsMutable) continue;
            if (!ReportTypeMismatch(a, b, span, $"Property '{propB.Name}' is immutable on type '{a}', but type '{b}' requires a mutable one.", trace))
                success = false;
        }

        return success;
    }

    private void CombineUnify(Type a, Type b, LocationSpan span, ref bool success, ref bool updated, TypeMismatchTrace? trace = null)
    {
        if (!TryUnify(a, b, span, out var stepUpdated, trace))
            success = false;
        else if (stepUpdated)
            updated = true;
    }

    private bool UnifyInterfaceTypes(InterfaceType a, InterfaceType b, LocationSpan span, out bool updated, TypeMismatchTrace? trace)
    {
        updated = false;
        return TryUnify(a.AssignabilityType, b.AssignabilityType, span, out updated, new TypeMismatchTrace(a, b, trace));
    }

    private bool UnifyFunctionTypes(FunctionType a, FunctionType b, LocationSpan span, out bool updated, TypeMismatchTrace? trace)
    {
        updated = false;

        // unification has to answer the same question IsAssignableTo does, or a call site would accept
        // through inference what a declared type rejects
        if (a.TypeParameters.Count != b.TypeParameters.Count
            || a.RequiredParameterTypes.Count < b.RequiredParameterTypes.Count
            || a.ParameterTypes.Count > b.ParameterTypes.Count
            || a.IsAsync != b.IsAsync)
            return ReportTypeMismatch(a, b, span, trace: trace);

        var success = true;
        var childTrace = new TypeMismatchTrace(a, b, trace);
        for (var i = 0; i < a.TypeParameters.Count; i++)
            CombineUnify(a.TypeParameters[i], b.TypeParameters[i], span, ref success, ref updated, childTrace);

        var freshVars = a.TypeParameters.Select(_ => CreateTypeVariable()).ToList();
        var aMapping = a.TypeParameters.Zip(freshVars).ToDictionary(p => p.First, p => p.Second);
        var bMapping = b.TypeParameters.Zip(freshVars).ToDictionary(p => p.First, p => p.Second);
        var aParamTypes = a.ParameterTypes.ConvertAll(t => SubstituteTypeParameters(aMapping, t));
        var bParamTypes = b.ParameterTypes.ConvertAll(t => SubstituteTypeParameters(bMapping, t));
        var aReturnType = SubstituteTypeParameters(aMapping, a.ReturnType);
        var bReturnType = SubstituteTypeParameters(bMapping, b.ReturnType);
        for (var i = 0; i < aParamTypes.Count; i++)
            CombineUnify(aParamTypes[i], bParamTypes[i], span, ref success, ref updated, childTrace);

        CombineUnify(aReturnType, bReturnType, span, ref success, ref updated, childTrace);

        return success;
    }

    private static bool OccursIn(TypeVariable variable, Type type) => OccursIn(variable, type, new HashSet<Type>(ReferenceEqualityComparer.Instance));

    private static bool OccursIn(TypeVariable variable, Type type, HashSet<Type> visited) =>
        visited.Add(type)
        && type switch
        {
            TypeVariable tv => tv.Id == variable.Id,
            IndexedType indexedType => OccursIn(variable, indexedType.Target, visited) || OccursIn(variable, indexedType.Index, visited),
            InterfaceType i => i.Constraints.Any(t => OccursIn(variable, t, visited)) || OccursIn(variable, i.ObjectType, visited),
            ObjectType obj => obj.Indexer != null && (OccursIn(variable, obj.Indexer.KeyType, visited) || OccursIn(variable, obj.Indexer.ValueType, visited))
                || obj.Properties.Any(p => OccursIn(variable, p.ValueType, visited)),
            GenericType generic => OccursIn(variable, generic.UnderlyingType, visited),
            InstantiatedType inst => inst.Arguments.Any(a => OccursIn(variable, a, visited)),
            TupleType tuple => tuple.ElementTypes.Any(t => OccursIn(variable, t, visited)),
            IntersectionType inter => inter.Types.Any(t => OccursIn(variable, t, visited)),
            UnionType union => union.Types.Any(t => OccursIn(variable, t, visited)),
            FunctionType fn => fn.TypeParameters.Any(p => OccursIn(variable, p, visited))
                || fn.ParameterTypes.Any(t => OccursIn(variable, t, visited))
                || OccursIn(variable, fn.ReturnType, visited),
            TypeParameter tp => tp.Constraint != null && OccursIn(variable, tp.Constraint, visited)
                || tp.DefaultType != null && OccursIn(variable, tp.DefaultType, visited),
            _ => false
        };

    private void ApplySubstitutions()
    {
        if (_substitutions.Count == 0) return;

        foreach (var nodeId in _nodeTypes.Keys.ToList())
            _nodeTypes[nodeId] = Substitute(_nodeTypes[nodeId]);
    }

    private static Type SubstituteTypeParameters(Dictionary<TypeParameter, TypeVariable> mapping, Type type) =>
        SubstituteTypeParameters(mapping, type, new Dictionary<Type, Type>(ReferenceEqualityComparer.Instance));

    private static Type SubstituteTypeParameters(Dictionary<TypeParameter, TypeVariable> mapping, Type type, Dictionary<Type, Type> visited)
    {
        if (type is TypeParameter typeParameter)
            return mapping.TryGetValue(typeParameter, out var tv) ? tv : type;

        if (visited.TryGetValue(type, out var existing))
            return existing;

        visited[type] = type;
        // simplify: false, like every other substituter (InstantiatedType.SubstituteTypeParameters,
        // TypeInferrer.Substitute). Simplifying here rewrites every nested InstantiatedType into its
        // expansion, which Transform then sees as a change and rebuilds the whole enclosing type around -
        // so renaming nothing still handed back a structurally identical but freshly allocated graph. On a
        // self-referential generic that is fatal: TryUnify's visiting set keys on reference identity, so it
        // met an unseen pair on every level and never terminated.
        var transformed = Transform(type, t => SubstituteTypeParameters(mapping, t, visited), simplify: false);
        visited[type] = transformed;
        return transformed;
    }

    private Type Substitute(Type type) => Substitute(type, new Dictionary<Type, Type>(ReferenceEqualityComparer.Instance));

    private Type Substitute(Type type, Dictionary<Type, Type> visitedSubstitutions)
    {
        if (visitedSubstitutions.TryGetValue(type, out var existing))
            return existing;

        var original = type;
        var visited = new HashSet<int>();
        while (type is TypeVariable tv && _substitutions.TryGetValue(tv.Id, out var replacement))
        {
            if (!visited.Add(tv.Id)) break;
            type = replacement;
        }

        visitedSubstitutions[original] = type;
        type = Transform(type, t => Substitute(t, visitedSubstitutions), null, false);
        if (type is InstantiatedType instantiated && instantiated.Arguments.All(a => a is not TypeVariable))
            type = instantiated.Expand();

        type = TypeSimplifier.Simplify(type);
        visitedSubstitutions[original] = type;
        return type;
    }

    private bool ReportTypeMismatch(Type a, Type b, LocationSpan span, string? info = null, TypeMismatchTrace? trace = null)
    {
        var frames = new List<string>();
        for (var frame = trace; frame != null; frame = frame.Parent)
        {
            // Two different instantiations of the same generic (e.g. Box<number> vs Box<string>) can
            // expand to types that render identically on both sides (both just "Box") - such a frame
            // reads as a tautology and carries no information, so skip it in favor of a deeper frame.
            var outerText = frame.Outer.ToString();
            var expectedText = frame.OuterExpected.ToString();
            if (outerText == expectedText)
                continue;

            frames.Add($"Type '{outerText}' is not assignable to type '{expectedText}'.");
        }

        frames.Reverse();

        var lines = new List<string>();
        foreach (var line in frames)
            if (lines.Count == 0 || lines[^1] != line)
                lines.Add(line);

        var leaf = $"Type '{a}' is not assignable to type '{b}'.{(info != null ? " " + info : "")}";
        if (lines.Count == 0 || lines[^1] != leaf)
            lines.Add(leaf);

        var message = string.Join('\n', lines.Select((line, depth) => new string(' ', depth * 4) + line));
        Diagnostics.Error(span, InternalCodes.TypeMismatch, message);
        return false;
    }

    internal bool ReportInfiniteType(LocationSpan span, string name)
    {
        Diagnostics.Error(span, InternalCodes.InfiniteType, $"Type '{name}' circularly references itself.");
        return false;
    }

    private TypeVariable CreateTypeVariable() => new(Interlocked.Increment(ref _nextVariableId));

    public sealed record TypeConstraint
    {
        public TypeConstraint(Type actual, Type expected, LocationSpan span)
        {
            ArgumentNullException.ThrowIfNull(actual);
            ArgumentNullException.ThrowIfNull(expected);
            Actual = actual;
            Expected = expected;
            Span = span;
        }

        public Type Actual { get; }
        public Type Expected { get; }
        public LocationSpan Span { get; }

        // Set after construction once the enclosing container (e.g. an array literal) finishes
        // checking all its children, since the container's own actual type isn't known until then.
        public TypeMismatchTrace? Trace { get; set; }
    }

    // A chain of ancestor type-pairs, outermost reachable via repeated .Parent, that a mismatch was
    // discovered underneath - rendered as an indented "why" trail above the leaf mismatch message.
    public sealed record TypeMismatchTrace(Type Outer, Type OuterExpected, TypeMismatchTrace? Parent = null);
}