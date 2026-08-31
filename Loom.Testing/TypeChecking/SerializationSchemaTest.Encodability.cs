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
}
