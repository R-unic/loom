using Loom.Core.Diagnostics;

namespace Loom.Testing.TypeChecking;

public partial class SerializationSchemaTest
{
    [Fact]
    public void ThrowsFor_NestedNonSerializableInterface()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface Inner { value: number }
            [serializable] interface MyData { inner: Inner }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.NotSerializable,
            "'inner' has type 'Inner', which is not serializable.",
            "add the 'serializable' attribute to interface 'Inner'."
        );
    }

    [Fact]
    public void ThrowsFor_RecursiveSerializableType()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("[serializable] interface Node { next: Node }");
        Assert.NotNull(diagnostics.Find(d => d.Code == InternalCodes.RecursiveSerializableType));
    }

    [Fact]
    public void ThrowsFor_AmbiguousUnion()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            [serializable] interface Circle { radius: number }
            [serializable] interface Square { side: number }
            [serializable] interface MyData { shape: Circle | Square }
            """
        );

        Assert.NotNull(diagnostics.Find(d => d.Code == InternalCodes.AmbiguousSerializableUnion));
    }

    [Theory]
    [InlineData("Vector2int16", "Vector2")]
    [InlineData("Vector3int16", "Vector3")]
    public void ThrowsFor_Int16Datatype_PointingAtItsGenericReplacement(string datatype, string replacement)
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            $$"""
            [serializable] interface MyData {
                position: {{datatype}};
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.NotSerializable,
            $"'position' has type '{datatype}', which cannot be serialized.",
            $"use '{replacement}<i16>' instead - its components are already i16, and the width is configurable."
        );
    }

    [Fact]
    public void ThrowsFor_GenericInterface_WithUnresolvedTypeParameter()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("[serializable] interface Box<T> { value: T }");

        Utility.AssertDiagnostic(diagnostics, InternalCodes.NotSerializable, "'value' has type 'T', which cannot be serialized.");
    }

    [Fact]
    public void ThrowsFor_Vector3_WithNonSizedTypeArgument()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            [serializable] interface MyData {
                position: Vector3<string>;
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidTypeArguments,
            "'position' needs a sized type for its component width, but got 'string'."
        );
    }

    [Fact]
    public void ThrowsFor_Array_WithSignedLengthType()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            [serializable] interface MyData {
                ids: Array<number, i8>;
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidTypeArguments,
            "'ids' needs an unsigned length type, but got 'I8'.",
            "lengths are never negative; use U8, U16, or U32."
        );
    }

    [Fact]
    public void ThrowsFor_NumberStep_NotPositive()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            [serializable] interface MyData {
                [number_range(0, 100), number_step(-1)]
                health: number;
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidSerializationRange,
            "'health' has a number_step of -1; the step must be positive."
        );
    }

    [Fact]
    public void ThrowsFor_NumberRange_MaximumNotGreaterThanMinimum()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            [serializable] interface MyData {
                [number_range(100, 0)]
                health: number;
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidSerializationRange,
            "'health' has an empty number range [100, 0]."
        );
    }

    [Fact]
    public void ThrowsFor_TupleElement_ThatIsNotSerializable()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface Circle { radius: number }
            [serializable] interface MyData { pair: (u8, Circle) }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.NotSerializable,
            "'pair[2]' has type 'Circle', which is not serializable.",
            "add the 'serializable' attribute to interface 'Circle'."
        );
    }

    [Fact]
    public void ThrowsFor_DiscriminatedUnion_WithVariantThatFailsToFlatten()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface IShape<Kind: string> { kind: Kind }
            interface Bad { x: number }

            [serializable] interface Circle: IShape<"Circle"> { thing: Bad }
            [serializable] interface Square: IShape<"Square">;

            [serializable] interface MyData { shape: Circle | Square }
            """
        );

        Assert.Contains(
            diagnostics.Set,
            d => d.Code == InternalCodes.NotSerializable && d.Message == "'shape.thing' has type 'Bad', which is not serializable."
        );
    }

    [Fact]
    public void SilentlyIgnoresRangeArgument_WhenItIsNotACompileTimeNumericConstant()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            [serializable] interface MyData {
                [number_range(0, "bad")]
                health: number;
            }
            """
        );

        Assert.DoesNotContain(diagnostics.Set, d => d.Code == InternalCodes.InvalidSerializationRange);
    }

    [Fact]
    public void ThrowsFor_MapValue_ThatIsNotSerializable()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface Circle { radius: number }
            [serializable] interface MyData { entries: Record<string, Circle> }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.NotSerializable,
            "'entries[v]' has type 'Circle', which is not serializable.",
            "add the 'serializable' attribute to interface 'Circle'."
        );
    }

    [Fact]
    public void ThrowsFor_DiscriminatedUnion_WithNonSerializableVariant()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface IAction<Kind: string> { kind: Kind }
            interface Ping: IAction<"Ping">;
            [serializable] interface Pong: IAction<"Pong">;

            [serializable] interface MyData { action: Ping | Pong }
            """
        );

        Assert.NotNull(diagnostics.Find(d => d.Code == InternalCodes.AmbiguousSerializableUnion));
    }

    [Fact]
    public void ThrowsFor_RuntimeKindUnion_WithMemberThatFailsToEncode()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface Circle { radius: number }
            [serializable] interface MyData { value: (u8, Circle) | number }
            """
        );

        Assert.NotNull(diagnostics.Find(d => d.Code == InternalCodes.NotSerializable));
    }

    [Fact]
    public void ThrowsFor_Union_WhoseMembersAreUnrecognizedAtRuntime()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            [serializable] interface MyData { value: Vector3<f32> | number }
            """
        );

        Assert.NotNull(diagnostics.Find(d => d.Code == InternalCodes.AmbiguousSerializableUnion));
    }

    [Fact]
    public void ThrowsFor_MapKey_ThatIsNotSerializable()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface Circle { radius: number }
            [serializable] interface MyData { entries: Record<Circle, number> }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.NotSerializable,
            "'entries[k]' has type 'Circle', which is not serializable.",
            "add the 'serializable' attribute to interface 'Circle'."
        );
    }

    /// <summary>
    ///     'Vector3' is only special-cased by name in <c>TryBuildInstantiatedField</c> - a user interface
    ///     that shadows the name but declares unrelated type parameters still takes that branch, and its
    ///     type argument lookup by parameter name then genuinely fails.
    /// </summary>
    [Fact]
    public void ThrowsFor_UserInterface_ShadowingARobloxGenericName_WithMismatchedTypeParameters()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface Vector3<X, Y> { value: X }
            [serializable] interface MyData { position: Vector3<u8, u8> }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.NotSerializable,
            "'position' references 'Vector3', which has no type parameter named 'T' - this is a compiler bug."
        );
    }

    /// <summary>
    ///     Same shadowing trick as <see cref="ThrowsFor_UserInterface_ShadowingARobloxGenericName_WithMismatchedTypeParameters" />,
    ///     for the 'Array' branch's own 'T'/'L' lookup instead of Vector3/Vector2/CFrame's 'T'.
    /// </summary>
    [Fact]
    public void ThrowsFor_UserInterface_ShadowingArray_WithMismatchedTypeParameters()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface Array<A, B> { value: A }
            [serializable] interface MyData { ids: Array<u8, u8> }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.NotSerializable,
            "'ids' references 'Array', which has no type parameter named 'T' - this is a compiler bug."
        );
    }

    /// <summary>
    ///     'number_range' is declared to take exactly 2 arguments, so a call with fewer already reports the
    ///     usual arity error - but <c>ReadNumberArgument</c> still runs its own bounds check on the way to
    ///     the missing 'maximum', reading the range as absent (rather than throwing) instead of double
    ///     reporting.
    /// </summary>
    [Fact]
    public void SilentlyTreatsRangeAsAbsent_WhenNumberRangeIsCalledWithTooFewArguments()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            [serializable] interface MyData {
                [number_range(0)]
                health: number;
            }
            """
        );

        Assert.DoesNotContain(diagnostics.Set, d => d.Code == InternalCodes.InvalidSerializationRange);
        Assert.Contains(diagnostics.Set, d => d.Message.Contains("Function expects 2 arguments, but 1 were provided."));
    }
}
