namespace Loom.Testing.Resolving;

public partial class ResolverTest
{
    [Fact]
    public void Resolves_CFrameDecomposition()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            let cf = CFrame::create(1, 2, 3);
            let (x, y, z, r00, r01, r02, r10, r11, r12, r20, r21, r22) = cf.get_components();
            let (rx, ry, rz) = cf.to_orientation();
            let (axis, angle) = cf.to_axis_angle();
            print(x, r22, rx, axis, angle);
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Resolves_CFrameEulerAngleVariants()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            let cf = CFrame::create(1, 2, 3);
            let (ax, ay, az) = cf.to_euler_angles_xyz();
            let (bx, by, bz) = cf.to_euler_angles_yxz();
            print(ax, ay, az, bx, by, bz);
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Resolves_CFrameSpaceHelpers()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics(
            """
            let cf = CFrame::create(1, 2, 3);
            print(cf.inverse());
            print(cf.lerp(CFrame::identity, 0.5));
            print(cf.to_world_space(CFrame::identity));
            print(cf.point_to_object_space(Vector3::zero));
            print(cf.vector_to_world_space(Vector3::one));
            """
        );

        Utility.AssertNoErrors(diagnostics);
    }

    [Fact]
    public void Resolves_CFrameDecomposition_ToMethodCalls()
    {
        var luau = Utility.GetLuauAST("let cf = CFrame::create(1, 2, 3); let (rx, ry, rz) = cf.to_orientation();", true).Render();

        Assert.Contains("CFrame.new(1, 2, 3)", luau);
        Assert.Contains("cf:ToOrientation()", luau);
    }
}
