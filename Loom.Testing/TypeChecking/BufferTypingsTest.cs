using Loom.Testing;

namespace Loom.Testing.TypeChecking;

/// <summary>
///     Covers the hand-written <c>buffer</c> library bindings in <c>Intrinsic/buffer.loom</c>, a
///     prerequisite for serialization codegen.
/// </summary>
[Collection("Assembly")]
public class BufferTypingsTest
{
    [Fact]
    public void BufferLibrary_TypeChecks()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            let b = buffer.create(8);
            buffer.writeu8(b, 0, 3);
            buffer.writei16(b, 1, -42);
            buffer.writef32(b, 4, 1.5);
            print(buffer.len(b));
            print(buffer.readu8(b, 0));
            print(buffer.readi16(b, 1));
            print(buffer.readf32(b, 4));
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void BufferBits_TypeCheck()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            let b = buffer.create(4);
            buffer.writebits(b, 0, 3, 5);
            print(buffer.readbits(b, 0, 3));
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void BufferStringAndCopy_TypeCheck()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            let source = buffer.fromstring("hello");
            let target = buffer.create(16);
            buffer.copy(target, 0, source);
            buffer.writestring(target, 8, "hi");
            buffer.fill(target, 12, 0);
            print(buffer.readstring(target, 0, 5));
            print(buffer.tostring(target));
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void BufferWrite_RejectsWrongArgumentType()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            let b = buffer.create(4);
            buffer.writeu8(b, 0, "not a number");
            """
        );

        Assert.Contains(diagnostics.Set, d => d.Severity == Core.Diagnostics.DiagnosticSeverity.Error);
    }

    [Fact]
    public void BufferCalls_EmitDirectly()
    {
        var luau = Utility.GetLuauAST("let b = buffer.create(7); buffer.writeu8(b, 0, 3);", true).Render();

        Assert.Contains("buffer.create(7)", luau);
        Assert.Contains("buffer.writeu8(b, 0, 3)", luau);
    }
}
