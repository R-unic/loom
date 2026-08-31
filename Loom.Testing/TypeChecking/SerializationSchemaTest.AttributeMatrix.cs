using Loom.Core.Diagnostics;

namespace Loom.Testing.TypeChecking;

public partial class SerializationSchemaTest
{
    [Fact]
    public void ThrowsFor_Packed_WithoutSerializable()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("[packed] interface MyData { id: number }");
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.MissingRequiredAttribute,
            "'packed' requires interface 'MyData' to also have the 'serializable' attribute.",
            "'packed' only changes how a serializable type is encoded."
        );
    }

    [Fact]
    public void ThrowsFor_PropertyAttribute_OnNonSerializableInterface()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            interface MyData {
                [number_range(0, 100)]
                id: number;
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.MissingRequiredAttribute,
            "'number_range' requires interface 'MyData' to have the 'serializable' attribute.",
            "add 'serializable' to 'MyData', or remove the attribute from 'id'."
        );
    }

    [Fact]
    public void ThrowsFor_NumberRange_OnSizedType()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            [serializable] interface MyData {
                [number_range(0, 100)]
                health: u8;
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.ConflictingAttributes,
            "'health' is already 'u8', so 'number_range' has nothing left to set.",
            "remove 'number_range', or declare 'health: number' to use a bounded range instead."
        );
    }

    [Fact]
    public void ThrowsFor_Quantize_WithoutNumberRange()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            [serializable] interface MyData {
                [number_step(0.01)]
                opacity: number;
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.MissingRequiredAttribute,
            "'number_step' on 'opacity' requires 'number_range'.",
            "without bounds there is no bit width to derive from a step."
        );
    }

    [Fact]
    public void ThrowsFor_IgnoreSerialization_OnRequiredProperty()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            [serializable] interface MyData {
                [ignore_serialization]
                cached: string;
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidAttributeTargetType,
            "'ignore_serialization' requires 'cached' to be optional, since there is no default value to restore.",
            "declare it as 'cached: string?'."
        );
    }

    [Fact]
    public void ThrowsFor_IgnoreSerialization_WithEncodingAttribute()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            [serializable] interface MyData {
                [ignore_serialization, number_range(0, 100)]
                cached: number?;
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.ConflictingAttributes,
            "'cached' is both ignored and annotated with 'number_range'.",
            "an ignored property is not encoded, so it cannot carry encoding attributes."
        );
    }

    [Fact]
    public void ThrowsFor_LengthType_NoLongerExists()
    {
        var diagnostics = Utility.GetSemanticModel(
            """
            [serializable] interface MyData {
                [length_type(NumberType.U8)]
                name: string;
            }
            """
        ).Diagnostics;

        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find name 'length_type'.");
    }

    [Fact]
    public void ThrowsFor_CFrameType_OnNonCFrameProperty()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            [serializable] interface MyData {
                [cframe_type(CFrameType::Precise)]
                position: Vector3;
            }
            """
        );

        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.InvalidAttributeTargetType,
            "'cframe_type' requires 'position' to be a CFrame, but it is 'Vector3<f32>'."
        );
    }

    [Fact]
    public void ThrowsFor_NumberType_NoLongerExists()
    {
        var diagnostics = Utility.GetSemanticModel(
            """
            [serializable] interface MyData {
                [number_type(NumberType.I16)]
                position: Vector3;
            }
            """
        ).Diagnostics;

        Utility.AssertDiagnostic(diagnostics, InternalCodes.CannotFindName, "Cannot find name 'number_type'.");
    }
}
