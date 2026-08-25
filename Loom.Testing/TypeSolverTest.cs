using Loom.Core.Diagnostics;
using Loom.Core.Parsing.AST;
using Loom.Core.Text;
using Loom.Core.TypeChecking;
using Loom.Core.TypeChecking.Types;
using ArrayType = Loom.Core.TypeChecking.Types.ArrayType;
using FunctionType = Loom.Core.TypeChecking.Types.FunctionType;
using IntersectionType = Loom.Core.TypeChecking.Types.IntersectionType;
using OptionalType = Loom.Core.TypeChecking.Types.OptionalType;
using LoomType = Loom.Core.TypeChecking.Types.Type;
using PrimitiveType = Loom.Core.TypeChecking.Types.PrimitiveType;
using TypeParameter = Loom.Core.TypeChecking.Types.TypeParameter;
using UnionType = Loom.Core.TypeChecking.Types.UnionType;
using Loom.Core.TypeChecking.Solving;

namespace Loom.Testing;

[Collection("Assembly")]
public class TypeSolverTest
{
    private static DiagnosticBag CreateDiagnostics() => new();

    [Fact]
    public void Unify_InstantiatedWithGeneric_BindsParameters()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var generic = new GenericType(
            new TypeAlias(null!, null!, null!, null!),
            [new TypeParameter("T")],
            PrimitiveType.Never
        );

        var instantiated = generic.Construct([PrimitiveType.Number]);

        solver.AddConstraint(instantiated, generic, Utility.Span);
        Assert.True(solver.SolveConstraints());
        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Unify_ObjectTypes_ExtraProperties_Allowed()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var objectOne = new ObjectType(
            null,
            [new ObjectProperty(false, "x", PrimitiveType.Number), new ObjectProperty(false, "y", PrimitiveType.String)]
        );

        var objectTwo = new ObjectType(
            null,
            [new ObjectProperty(false, "x", PrimitiveType.Number)]
        );

        solver.AddConstraint(objectOne, objectTwo, Utility.Span);
        Assert.True(solver.SolveConstraints());
        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Unify_ObjectTypes_MissingIndexer_ReportsError()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var objectWithIndexer = new ObjectType(new ObjectIndexer(true, PrimitiveType.String, PrimitiveType.Number), []);
        var objectWithoutIndexer = new ObjectType(null, []);
        solver.AddConstraint(objectWithoutIndexer, objectWithIndexer, Utility.Span);
        Assert.False(solver.SolveConstraints());
        Assert.NotEmpty(diagnostics.Errors().Set);
    }

    [Fact]
    public void Unify_InterfaceTypes_DifferentObjectBodies_ReportsError()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var baseInterface = new InterfaceType("Base", [], ObjectType.Empty);
        var objectA = new ObjectType(null, [new ObjectProperty(false, "x", PrimitiveType.Number)]);
        var objectB = new ObjectType(null, [new ObjectProperty(false, "x", PrimitiveType.String)]);
        var interfaceA = new InterfaceType("I", [baseInterface], objectA);
        var interfaceB = new InterfaceType("I", [baseInterface], objectB);

        solver.AddConstraint(interfaceA, interfaceB, Utility.Span);
        Assert.False(solver.SolveConstraints());
        Assert.NotEmpty(diagnostics.Errors().Set);
    }

    [Fact]
    public void Unify_FunctionTypes_MismatchedTypeParameterConstraints()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var constrainedNumber = new TypeParameter("T", PrimitiveType.Number);
        var constrainedString = new TypeParameter("U", PrimitiveType.String);
        var functionOne = new FunctionType([constrainedNumber], [constrainedNumber], PrimitiveType.Bool);
        var functionTwo = new FunctionType([constrainedString], [constrainedString], PrimitiveType.Bool);

        solver.AddConstraint(functionOne, functionTwo, Utility.Span);
        Assert.False(solver.SolveConstraints());
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type 'fn<T: number>(T: number): bool' is not assignable to type 'fn<U: string>(U: string): bool'.\n    Type 'T: number' is not assignable to type 'U: string'."
        );
    }

    [Fact]
    public void Unify_ObjectTypes_PropertyMismatch_TracesOuterObjectType()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var objectA = new ObjectType(null, [new ObjectProperty(false, "x", PrimitiveType.Number)]);
        var objectB = new ObjectType(null, [new ObjectProperty(false, "x", PrimitiveType.String)]);

        solver.AddConstraint(objectA, objectB, Utility.Span);
        Assert.False(solver.SolveConstraints());
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type '{ x: number }' is not assignable to type '{ x: string }'.\n    Type 'number' is not assignable to type 'string'."
        );
    }

    [Fact]
    public void Unify_InstantiatedTypes_ArgumentMismatch_TracesOuterGenericType()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var declaration = new TypeAlias(null!, Utility.IdentifierToken("Box"), null, null!);
        var typeParameter = new TypeParameter("T");
        var generic = new GenericType(declaration, [typeParameter], typeParameter);
        var instantiatedNumber = generic.Construct([PrimitiveType.Number]);
        var instantiatedString = generic.Construct([PrimitiveType.String]);

        solver.AddConstraint(instantiatedNumber, instantiatedString, Utility.Span);
        Assert.False(solver.SolveConstraints());
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type 'Box<number>' is not assignable to type 'Box<string>'.\n    Type 'number' is not assignable to type 'string'."
        );
    }

    [Fact]
    public void Unify_InterfaceTypes_PropertyMismatch_TracesInterfaceNamesAndStructuralShape()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var objectA = new ObjectType(null, [new ObjectProperty(false, "x", PrimitiveType.Number)]);
        var objectB = new ObjectType(null, [new ObjectProperty(false, "x", PrimitiveType.String)]);
        var interfaceA = new InterfaceType("A", [], objectA);
        var interfaceB = new InterfaceType("B", [], objectB);

        solver.AddConstraint(interfaceA, interfaceB, Utility.Span);
        Assert.False(solver.SolveConstraints());
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type 'A' is not assignable to type 'B'.\n    Type '{ x: number }' is not assignable to type '{ x: string }'.\n        Type 'number' is not assignable to type 'string'."
        );
    }

    [Fact]
    public void Unify_ArrayTypes_ElementMismatch_TracesOuterArrayType()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var numberArray = new ArrayType(PrimitiveType.Number, false);
        var stringArray = new ArrayType(PrimitiveType.String, false);

        solver.AddConstraint(numberArray, stringArray, Utility.Span);
        Assert.False(solver.SolveConstraints());
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type 'number[]' is not assignable to type 'string[]'.\n    Type 'number' is not assignable to type 'string'."
        );
    }

    [Fact]
    public void Unify_ArrayTypes_ImmutableSourceToMutableTarget_ReportsDescriptiveReason()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var immutableSource = new ArrayType(PrimitiveType.Number, false);
        var mutableTarget = new ArrayType(PrimitiveType.Number, true);

        solver.AddConstraint(immutableSource, mutableTarget, Utility.Span);
        Assert.False(solver.SolveConstraints());
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type 'number[]' is not assignable to type 'number[mut]'. Cannot assign an immutable array to a mutable array type."
        );
    }

    [Fact]
    public void Unify_ArrayTypes_MutableInvariantElementMismatch_ReportsDescriptiveReason()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var sourceMutable = new ArrayType(PrimitiveType.Number, true);
        var targetMutable = new ArrayType(PrimitiveType.String, true);

        solver.AddConstraint(sourceMutable, targetMutable, Utility.Span);
        Assert.False(solver.SolveConstraints());
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type 'number[mut]' is not assignable to type 'string[mut]'. Mutable arrays require identical element types, but source and target element types differ."
        );
    }

    [Fact]
    public void Unify_ArrayOfObjectTypes_PropertyMismatch_TracesThreeLevels()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var objectA = new ObjectType(null, [new ObjectProperty(false, "x", PrimitiveType.Number)]);
        var objectB = new ObjectType(null, [new ObjectProperty(false, "x", PrimitiveType.String)]);
        var arrayA = new ArrayType(objectA, false);
        var arrayB = new ArrayType(objectB, false);

        solver.AddConstraint(arrayA, arrayB, Utility.Span);
        Assert.False(solver.SolveConstraints());
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.TypeMismatch,
            "Type '{ x: number }[]' is not assignable to type '{ x: string }[]'.\n    Type '{ x: number }' is not assignable to type '{ x: string }'.\n        Type 'number' is not assignable to type 'string'."
        );
    }

    [Fact]
    public void Unify_UnionTypes_Identical_Succeeds()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var unionOne = new UnionType([PrimitiveType.Number, PrimitiveType.String]);
        var unionTwo = new UnionType([PrimitiveType.Number, PrimitiveType.String]);

        solver.AddConstraint(unionOne, unionTwo, Utility.Span);
        Assert.True(solver.SolveConstraints());
        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Unify_UnionTypes_AssignableElement_Succeeds()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        solver.AddConstraint(PrimitiveType.Number, new UnionType([PrimitiveType.Number, PrimitiveType.String]), Utility.Span);
        Assert.True(solver.SolveConstraints());
        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Unify_InfiniteType_OccursCheck()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var node = Identifier("x");
        var variable = solver.GetType(node);
        var infinite = new ArrayType(variable, false);
        solver.AddConstraint(variable, infinite, Utility.Span);

        var success = solver.SolveConstraints();
        Assert.False(success, "Should reject infinite type X = X[]");
        Assert.Contains(diagnostics.Set, d => d.Code == InternalCodes.InfiniteType);
    }

    [Fact]
    public void Unify_InfiniteType_ThroughFunction()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var node = Identifier("f");
        var variable = solver.GetType(node);
        var infiniteFn = new FunctionType([], [variable], variable);
        solver.AddConstraint(variable, infiniteFn, Utility.Span);

        var success = solver.SolveConstraints();
        Assert.False(success, "Should reject infinite type X = fn(X): X");
        Assert.Contains(diagnostics.Set, d => d.Code == InternalCodes.InfiniteType);
    }

    [Fact]
    public void Unify_FunctionTypes_Identical()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var fn1 = new FunctionType([], [PrimitiveType.Number], PrimitiveType.String);
        var fn2 = new FunctionType([], [PrimitiveType.Number], PrimitiveType.String);
        solver.AddConstraint(fn1, fn2, Utility.Span);

        Assert.True(solver.SolveConstraints());
    }

    /// <summary>
    ///     A function may ignore arguments it is handed, so taking fewer than the target supplies unifies.
    /// </summary>
    /// <remarks>
    ///     This is the direction <see cref="FunctionType.IsAssignableTo" /> has always allowed, and
    ///     unification used to reject - so a handler naming the first of an event's two arguments was
    ///     accepted by a declared type and refused by inference.
    /// </remarks>
    [Fact]
    public void Unify_FunctionTypes_FewerParamsThanTarget_Unifies()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var fn1 = new FunctionType([], [PrimitiveType.Number], PrimitiveType.String);
        var fn2 = new FunctionType([], [PrimitiveType.Number, PrimitiveType.Bool], PrimitiveType.String);
        solver.AddConstraint(fn1, fn2, Utility.Span);

        Assert.True(solver.SolveConstraints());
        Assert.Empty(diagnostics.Errors().Set);
        Assert.True(fn1.IsAssignableTo(fn2), "unification and assignability have to answer the same question");
    }

    /// <summary>
    ///     Unification and assignability answer the same question, over the shapes that historically made
    ///     them disagree: parameter counts, rest parameters, and asyncness.
    /// </summary>
    /// <remarks>
    ///     They are two implementations of one rule living in different files, and a divergence is invisible
    ///     from either side - a call site accepts through inference what a declared type rejects, or the
    ///     reverse. Comparing them directly is the only thing that catches it.
    /// </remarks>
    [Theory]
    [MemberData(nameof(FunctionPairs))]
    public void Unify_AndAssignability_AgreeOnFunctionTypes(FunctionType source, FunctionType target)
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        solver.AddConstraint(source, target, Utility.Span);

        Assert.Equal(source.IsAssignableTo(target), solver.SolveConstraints());
    }

    public static TheoryData<FunctionType, FunctionType> FunctionPairs()
    {
        var data = new TheoryData<FunctionType, FunctionType>();
        var shapes = new List<FunctionType>
        {
            fn([], PrimitiveType.Void),
            fn([PrimitiveType.Number], PrimitiveType.Void),
            fn([PrimitiveType.Number, PrimitiveType.String], PrimitiveType.Void),
            fn([PrimitiveType.Number], PrimitiveType.Number),
            fn([new ArrayType(PrimitiveType.Number, false)], PrimitiveType.Void, hasRest: true),
            fn([PrimitiveType.String, new ArrayType(PrimitiveType.Number, false)], PrimitiveType.Void, hasRest: true),
            fn([PrimitiveType.Number], PrimitiveType.Void, isAsync: true)
        };

        foreach (var source in shapes)
            foreach (var target in shapes)
                data.Add(source, target);

        return data;

        FunctionType fn(List<LoomType> parameters, LoomType returnType, bool hasRest = false, bool isAsync = false) =>
            new([], parameters, returnType, hasRest, isAsync);
    }

    [Fact]
    public void Unify_FunctionTypes_MoreParamsThanTarget_ReportsMismatchInsteadOfCrashing()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var fn1 = new FunctionType([], [PrimitiveType.Number, new OptionalType(PrimitiveType.String)], PrimitiveType.Bool);
        var fn2 = new FunctionType([], [PrimitiveType.Number], PrimitiveType.Bool);
        solver.AddConstraint(fn1, fn2, Utility.Span);

        Assert.False(solver.SolveConstraints());
        Assert.NotEmpty(diagnostics.Errors().Set);
    }

    [Fact]
    public void Unify_FunctionTypes_DifferentReturn()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var fn1 = new FunctionType([], [PrimitiveType.Number], PrimitiveType.String);
        var fn2 = new FunctionType([], [PrimitiveType.Number], PrimitiveType.Bool);
        solver.AddConstraint(fn1, fn2, Utility.Span);

        Assert.False(solver.SolveConstraints());
        Assert.NotEmpty(diagnostics.Errors().Set);
    }

    [Fact]
    public void Unify_FunctionTypes_WithTypeParameters()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var tp1 = new TypeParameter("T");
        var tp2 = new TypeParameter("U");
        var fn1 = new FunctionType([tp1], [tp1], tp1);
        var fn2 = new FunctionType([tp2], [tp2], tp2);

        solver.AddConstraint(fn1, fn2, Utility.Span);
        Assert.True(solver.SolveConstraints());
    }

    [Fact]
    public void Unify_ObjectTypes_SameStructure()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var obj1 = new ObjectType(null, [new ObjectProperty(false, "x", PrimitiveType.Number), new ObjectProperty(true, "y", PrimitiveType.String)]);
        var obj2 = new ObjectType(null, [new ObjectProperty(false, "x", PrimitiveType.Number), new ObjectProperty(true, "y", PrimitiveType.String)]);

        solver.AddConstraint(obj1, obj2, Utility.Span);
        Assert.True(solver.SolveConstraints());
    }

    [Fact]
    public void Unify_ObjectTypes_PropertyTypeMismatch()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var obj1 = new ObjectType(null, [new ObjectProperty(false, "x", PrimitiveType.Number)]);
        var obj2 = new ObjectType(null, [new ObjectProperty(false, "x", PrimitiveType.String)]);

        solver.AddConstraint(obj1, obj2, Utility.Span);
        Assert.False(solver.SolveConstraints());
        Assert.NotEmpty(diagnostics.Errors().Set);
    }

    /// <summary>Same direction as <see cref="Core.TypeChecking.Types.Type.IsAssignableTo" />: an immutable property cannot satisfy a mutable one.</summary>
    [Fact]
    public void Unify_ObjectTypes_MutabilityMismatch()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var immutable = new ObjectType(null, [new ObjectProperty(false, "x", PrimitiveType.Number)]);
        var mutable = new ObjectType(null, [new ObjectProperty(true, "x", PrimitiveType.Number)]);

        solver.AddConstraint(immutable, mutable, Utility.Span);
        Assert.False(solver.SolveConstraints());
        Assert.NotEmpty(diagnostics.Errors().Set);
    }

    [Fact]
    public void Unify_ObjectTypes_WithIndexer_Same()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var obj1 = new ObjectType(new ObjectIndexer(true, PrimitiveType.String, PrimitiveType.Number), []);
        var obj2 = new ObjectType(new ObjectIndexer(true, PrimitiveType.String, PrimitiveType.Number), []);

        solver.AddConstraint(obj1, obj2, Utility.Span);
        Assert.True(solver.SolveConstraints());
    }

    [Fact]
    public void Unify_ObjectTypes_WithIndexer_KeyMismatch()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var obj1 = new ObjectType(new ObjectIndexer(true, PrimitiveType.String, PrimitiveType.Number), []);
        var obj2 = new ObjectType(new ObjectIndexer(true, PrimitiveType.Number, PrimitiveType.Number), []);

        solver.AddConstraint(obj1, obj2, Utility.Span);
        Assert.False(solver.SolveConstraints());
        Assert.NotEmpty(diagnostics.Errors().Set);
    }

    /// <summary>
    ///     Unification answers the same question as <see cref="Core.TypeChecking.Types.Type.IsAssignableTo" /> and has to give the
    ///     same answer: only asking for a mutable indexer you were not given is a mismatch. Handing a mutable
    ///     indexer to something that only reads through it is not.
    /// </summary>
    [Fact]
    public void Unify_ObjectTypes_ImmutableIndexerAgainstMutable_IsAMismatch()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var immutable = new ObjectType(new ObjectIndexer(false, PrimitiveType.String, PrimitiveType.Number), []);
        var mutable = new ObjectType(new ObjectIndexer(true, PrimitiveType.String, PrimitiveType.Number), []);

        solver.AddConstraint(immutable, mutable, Utility.Span);
        Assert.False(solver.SolveConstraints());
        Assert.NotEmpty(diagnostics.Errors().Set);
    }

    /// <inheritdoc cref="Unify_ObjectTypes_ImmutableIndexerAgainstMutable_IsAMismatch" />
    [Fact]
    public void Unify_ObjectTypes_MutableIndexerAgainstImmutable_Unifies()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var mutable = new ObjectType(new ObjectIndexer(true, PrimitiveType.String, PrimitiveType.Number), []);
        var immutable = new ObjectType(new ObjectIndexer(false, PrimitiveType.String, PrimitiveType.Number), []);

        Assert.True(mutable.IsAssignableTo(immutable));

        solver.AddConstraint(mutable, immutable, Utility.Span);
        Assert.True(solver.SolveConstraints());
        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Unify_ObjectWithInterface_Compatible()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var interfaceObj = new ObjectType(null, [new ObjectProperty(false, "name", PrimitiveType.String)]);
        var interfaceType = new InterfaceType("Named", [], interfaceObj);
        var obj = new ObjectType(null, [new ObjectProperty(false, "name", PrimitiveType.String)]);

        solver.AddConstraint(obj, interfaceType, Utility.Span);
        Assert.True(solver.SolveConstraints());
    }

    [Fact]
    public void Unify_InterfaceTypes_Same()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var a = new InterfaceType("A", [], ObjectType.Empty);
        var b = new InterfaceType("A", [], ObjectType.Empty);

        solver.AddConstraint(a, b, Utility.Span);
        Assert.True(solver.SolveConstraints());
    }

    [Fact]
    public void Unify_InterfaceTypes_DifferentConstraints()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var baseA = new InterfaceType("Base", [], ObjectType.Empty);
        var a = new InterfaceType("A", [baseA], ObjectType.Empty);
        var b = new InterfaceType("B", [], ObjectType.Empty);

        solver.AddConstraint(a, b, Utility.Span);
        Assert.True(solver.SolveConstraints());
        Assert.Empty(diagnostics.Errors().Set);
    }

    [Fact]
    public void Unify_InstantiatedTypes_Same()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var generic = new GenericType(
            new TypeAlias(null!, null!, null!, null!),
            [new TypeParameter("T")],
            PrimitiveType.Never
        );

        var inst1 = generic.Construct([PrimitiveType.Number]);
        var inst2 = generic.Construct([PrimitiveType.Number]);

        solver.AddConstraint(inst1, inst2, Utility.Span);
        Assert.True(solver.SolveConstraints());
    }

    [Fact]
    public void Unify_VariableWithPrimitive_BindsVariable()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var node = Identifier("x");

        var variable = solver.GetType(node);
        solver.AddConstraint(variable, PrimitiveType.Number, Utility.Span);

        var success = solver.SolveConstraints();
        Assert.True(success);
        Assert.True(solver.GetType(node).Equals(PrimitiveType.Number), $"Expected 'number', got '{solver.GetType(node)}'");
    }

    [Fact]
    public void Unify_PrimitiveWithVariable_BindsVariable()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var node = Identifier("x");

        var variable = solver.GetType(node);
        solver.AddConstraint(PrimitiveType.String, variable, Utility.Span);

        var success = solver.SolveConstraints();
        Assert.True(success);
        Assert.True(solver.GetType(node).Equals(PrimitiveType.String), $"Expected 'string', got '{solver.GetType(node)}'");
    }

    [Fact]
    public void Unify_CompatiblePrimitives_Succeeds()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        solver.AddConstraint(PrimitiveType.Number, PrimitiveType.Number, Utility.Span);

        var success = solver.SolveConstraints();
        Assert.True(success);
    }

    [Fact]
    public void Unify_IncompatiblePrimitives_ReportsError()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        solver.AddConstraint(PrimitiveType.Number, PrimitiveType.String, Utility.Span);

        var success = solver.SolveConstraints();
        Assert.False(success);
        Assert.NotEmpty(diagnostics.Errors().Set);
    }

    [Fact]
    public void Unify_VariableWithUnion_TransitiveBindings()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var nodeX = Identifier("x");
        var nodeY = Identifier("y");
        var nodeZ = Identifier("z");
        var x = solver.GetType(nodeX);
        var y = solver.GetType(nodeY);
        var z = solver.GetType(nodeZ);
        solver.AddConstraint(x, y, Utility.Span);
        solver.AddConstraint(y, PrimitiveType.Number, Utility.Span);
        solver.AddConstraint(z, x, Utility.Span);

        var success = solver.SolveConstraints();
        Assert.True(success);
        Assert.True(solver.GetType(nodeX).Equals(PrimitiveType.Number), $"Expected 'number', got '{solver.GetType(nodeX)}'");
        Assert.True(solver.GetType(nodeZ).Equals(PrimitiveType.Number), $"Expected 'number', got '{solver.GetType(nodeZ)}'");
    }

    [Fact]
    public void Unify_AssignableUnion_Succeeds()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var boolOrString = new UnionType([PrimitiveType.Bool, PrimitiveType.String]);
        solver.AddConstraint(PrimitiveType.Bool, boolOrString, Utility.Span);

        var success = solver.SolveConstraints();
        Assert.True(success);
    }

    [Fact]
    public void Unify_NotAssignableToUnion_ReportsError()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var boolOrString = new UnionType([PrimitiveType.Bool, PrimitiveType.String]);
        solver.AddConstraint(PrimitiveType.Number, boolOrString, Utility.Span);

        var success = solver.SolveConstraints();
        Assert.False(success);
        Assert.NotEmpty(diagnostics.Errors().Set);
    }

    [Fact]
    public void Unify_AssignableIntersection_Succeeds()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        solver.AddConstraint(PrimitiveType.Bool, new IntersectionType([PrimitiveType.Bool, PrimitiveType.Bool]), Utility.Span);

        var success = solver.SolveConstraints();
        Assert.True(success);
    }

    [Fact]
    public void SetType_OverwritesVariable()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var node = Identifier("x");

        var variable = solver.GetType(node);
        Assert.IsType<TypeVariable>(variable);

        solver.SetType(node, PrimitiveType.Bool);
        Assert.True(
            solver.GetType(node).Equals(PrimitiveType.Bool),
            "SetType should replace the variable with the given type"
        );
    }

    [Fact]
    public void Unify_ChainOfVariables_AllResolveToSame()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var nodes = Enumerable.Range(0, 5).Select(_ => Identifier("x")).ToList();
        var variables = nodes.Select(solver.GetType).ToList();
        for (var i = 0; i < variables.Count - 1; i++)
            solver.AddConstraint(variables[i], variables[i + 1], Utility.Span);

        solver.AddConstraint(variables.Last(), PrimitiveType.Bool, Utility.Span);
        var success = solver.SolveConstraints();
        Assert.True(success);

        foreach (var node in nodes)
            Assert.True(solver.GetType(node).Equals(PrimitiveType.Bool), $"Expected all variables to resolve to 'bool', got '{solver.GetType(node)}'");
    }

    [Fact]
    public void Unify_MultipleConstraints_AllSatisfied()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var nodeX = Identifier("x");
        var nodeY = Identifier("y");

        var x = solver.GetType(nodeX);
        var y = solver.GetType(nodeY);
        solver.AddConstraint(x, PrimitiveType.Number, Utility.Span);
        solver.AddConstraint(y, PrimitiveType.Number, Utility.Span);
        solver.AddConstraint(x, y, Utility.Span);

        var success = solver.SolveConstraints();
        Assert.True(success);
    }

    // Characterization tests for issue #235 PR 3 items 7-8 (worklist-based incremental solving,
    // free-variable subtree skipping in Transform): these lock down SolveConstraints' current
    // behavior on the shapes its own doc comments and GenericType.Construct's doc comments call out
    // as needing careful cycle handling, so a regression in either optimization is caught here rather
    // than surfacing later as a silently wrong inference. Run and confirmed green BEFORE and AFTER
    // implementing 7-8, not just after.

    /// <summary>
    ///     A type variable constrained against a self-referential generic instantiation (mirrors
    ///     <see cref="GenericTypesTest.Expand_SelfReferentialGeneric_ClosesTheCycleOnItself" /> but reached
    ///     through <see cref="TypeSolver.SolveConstraints" /> instead of <see cref="InstantiatedType.Expand" />
    ///     directly) has to resolve to the concrete instantiation and still be safely expandable afterwards -
    ///     the back-edge <see cref="GenericType.Construct" /> relies on has to survive a trip through
    ///     <c>Substitute</c>/<c>Transform</c>, which is exactly what a worklist rewrite of the fixpoint loop
    ///     risks disturbing if it stops re-visiting a constraint that still references the bound variable.
    /// </summary>
    [Fact]
    public void Unify_SelfReferentialGenericThroughVariable_ResolvesAndExpandsWithoutHanging()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);

        var paramT = new TypeParameter("T");
        var decl = new MockGenericNamedDeclaration("Bag");
        var body = new ObjectType(new ObjectIndexer(false, paramT, PrimitiveType.Bool), []);
        var generic = new GenericType(decl, [paramT], body);
        body.AddProperties([new ObjectProperty(false, "merge", new FunctionType([], [generic.Construct([paramT])], generic.Construct([paramT])))]);

        var node = Identifier("bag");
        var variable = solver.GetType(Identifier("t"));
        solver.SetType(node, generic.Construct([variable]));
        solver.AddConstraint(variable, PrimitiveType.Number, Utility.Span);

        var success = solver.SolveConstraints();
        Assert.True(success);
        Utility.AssertNoErrors(diagnostics);

        var solved = Assert.IsType<InstantiatedType>(solver.GetType(node));
        Assert.True(solved.Arguments.Single().Equals(PrimitiveType.Number));

        var expanded = Assert.IsType<ObjectType>(solved.Expand());
        var merge = Assert.IsType<FunctionType>(expanded.Properties.Single(p => p.Name == "merge").ValueType);
        Assert.Same(solved, merge.ReturnType);
        Assert.Same(solved, merge.ParameterTypes.Single());
    }

    /// <summary>
    ///     Two generics whose bodies reference each other through a function member (the same shape
    ///     <see cref="GenericTypesTest.Expand_SelfReferentialGeneric_ClosesTheCycleOnItself" /> uses for a
    ///     single self-reference, generalised to two) resolve through the solver without hanging.
    ///     Deliberately NOT built as a raw union of the two instantiations - <c>TypeSimplifier.Normalize</c>'s
    ///     expansion cache only remembers a type once its own <c>Normalize</c> call finishes, so two
    ///     different instantiations that mutually expand into each other through <em>union</em> members can
    ///     revisit one another before either finishes caching. Behind a function parameter/return type (which
    ///     <c>Normalize</c> does not walk into at all) sidesteps that - see the standalone follow-up filed for
    ///     the union-shaped case.
    /// </summary>
    [Fact]
    public void Unify_MutuallyReferentialGenericsThroughFunctionMembers_ResolvesWithoutHanging()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);

        var declA = new MockGenericNamedDeclaration("A");
        var declB = new MockGenericNamedDeclaration("B");
        var bodyA = new ObjectType(null, []);
        var bodyB = new ObjectType(null, []);
        var genericA = new GenericType(declA, [], bodyA);
        var genericB = new GenericType(declB, [], bodyB);
        bodyA.AddProperties([new ObjectProperty(false, "asB", new FunctionType([], [], genericB.Construct([])))]);
        bodyB.AddProperties([new ObjectProperty(false, "asA", new FunctionType([], [], genericA.Construct([])))]);

        var node = Identifier("a");
        var variable = solver.GetType(node);
        solver.AddConstraint(variable, genericA.Construct([]), Utility.Span);

        var success = solver.SolveConstraints();
        Assert.True(success);
        Utility.AssertNoErrors(diagnostics);

        // TypeSolver.Substitute unifies on the expanded form, so the variable binds to A's structural
        // body (an ObjectType), not the InstantiatedType it was written against - see Substitute's own
        // doc comment. The interesting cyclic-safety property is one level down: 'asB's return type is
        // still the un-expanded InstantiatedType back-edge (TypeSubstitution.Apply doesn't eagerly
        // dereference a nested instantiation), and expanding THAT by hand closes the loop back to A
        // without needing to walk through TypeSimplifier.Normalize's union-shaped hazard.
        var solvedBody = Assert.IsType<ObjectType>(solver.GetType(node));
        var asB = Assert.IsType<FunctionType>(solvedBody.Properties.Single(p => p.Name == "asB").ValueType);
        var bInstantiated = Assert.IsType<InstantiatedType>(asB.ReturnType);
        Assert.Same(genericB.Construct([]), bInstantiated);

        var expandedB = Assert.IsType<ObjectType>(bInstantiated.Expand());
        var asA = Assert.IsType<FunctionType>(expandedB.Properties.Single(p => p.Name == "asA").ValueType);
        var aInstantiated = Assert.IsType<InstantiatedType>(asA.ReturnType);
        Assert.Same(genericA.Construct([]), aInstantiated);
    }

    /// <summary>
    ///     Four variables constrained in a diamond - z depends on w through two independent chains (via x
    ///     and via y) - with the constraint that actually binds w to a concrete type listed <em>last</em>, so
    ///     a single left-to-right pass over the constraint list cannot resolve everything: the fixpoint loop
    ///     has to run at least twice for x, y and z to see w's binding. A worklist rewrite that only
    ///     re-examines constraints touching a variable that changed in the *previous* pass has to still catch
    ///     both of z's paths, not just whichever one happened to update first.
    /// </summary>
    [Fact]
    public void Unify_DiamondDependency_PropagatesThroughBothPaths()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var nodeW = Identifier("w");
        var nodeX = Identifier("x");
        var nodeY = Identifier("y");
        var nodeZ = Identifier("z");

        var w = solver.GetType(nodeW);
        var x = solver.GetType(nodeX);
        var y = solver.GetType(nodeY);
        var z = solver.GetType(nodeZ);

        solver.AddConstraint(z, x, Utility.Span);
        solver.AddConstraint(z, y, Utility.Span);
        solver.AddConstraint(x, w, Utility.Span);
        solver.AddConstraint(y, w, Utility.Span);
        solver.AddConstraint(w, PrimitiveType.Number, Utility.Span);

        var success = solver.SolveConstraints();
        Assert.True(success);
        Utility.AssertNoErrors(diagnostics);

        Assert.True(solver.GetType(nodeW).Equals(PrimitiveType.Number), $"Expected 'number', got '{solver.GetType(nodeW)}'");
        Assert.True(solver.GetType(nodeX).Equals(PrimitiveType.Number), $"Expected 'number', got '{solver.GetType(nodeX)}'");
        Assert.True(solver.GetType(nodeY).Equals(PrimitiveType.Number), $"Expected 'number', got '{solver.GetType(nodeY)}'");
        Assert.True(solver.GetType(nodeZ).Equals(PrimitiveType.Number), $"Expected 'number', got '{solver.GetType(nodeZ)}'");
    }

    /// <summary>
    ///     Two constraints referencing the same pair of overlapping variables through different member
    ///     positions (an object's two properties, each independently unified) - a worklist scheme that marks
    ///     "changed" too coarsely (e.g. per-constraint instead of per-variable) or too narrowly (missing that
    ///     a variable appears nested inside an object property rather than bare) would under- or over-process
    ///     this relative to the full re-scan.
    /// </summary>
    [Fact]
    public void Unify_OverlappingVariablesInsideObjectProperties_AllResolve()
    {
        var diagnostics = CreateDiagnostics();
        var solver = new TypeSolver(diagnostics);
        var nodeA = Identifier("a");
        var nodeB = Identifier("b");
        var varA = solver.GetType(nodeA);
        var varB = solver.GetType(nodeB);

        var actual = new ObjectType(null, [new ObjectProperty(false, "x", varA), new ObjectProperty(false, "y", varB)]);
        var expected = new ObjectType(null, [new ObjectProperty(false, "x", PrimitiveType.Number), new ObjectProperty(false, "y", varA)]);

        solver.AddConstraint(actual, expected, Utility.Span);

        var success = solver.SolveConstraints();
        Assert.True(success);
        Utility.AssertNoErrors(diagnostics);

        Assert.True(solver.GetType(nodeA).Equals(PrimitiveType.Number), $"Expected 'number', got '{solver.GetType(nodeA)}'");
        Assert.True(solver.GetType(nodeB).Equals(PrimitiveType.Number), $"Expected 'number', got '{solver.GetType(nodeB)}'");
    }

    private static Identifier Identifier(string name) => new(Utility.IdentifierToken(name));

    private class MockGenericNamedDeclaration(string name)
        : GenericNamedDeclaration(
            [],
            new Token(SyntaxKind.TypeKeyword, LocationSpan.Empty(), "type"),
            new Token(SyntaxKind.Identifier, LocationSpan.Empty(), name),
            new TypeParameters(new Token(SyntaxKind.LArrow, LocationSpan.Empty(), "<"), new Token(SyntaxKind.LArrow, LocationSpan.Empty(), ">"), [])
        )
    {
        public override T Accept<T>(Visitor<T> visitor) => default!;
    }
}
