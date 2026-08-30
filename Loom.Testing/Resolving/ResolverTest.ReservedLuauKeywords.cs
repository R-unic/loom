using Loom.Core.Diagnostics;

namespace Loom.Testing.Resolving;

public partial class ResolverTest
{
    [Theory]
    [InlineData("let repeat = 1;", "repeat")]
    [InlineData("fn until() {}", "until")]
    [InlineData("interface local;", "local")]
    public void ThrowsFor_ReservedLuauKeywordDeclaration(string source, string keyword)
    {
        var diagnostics = Utility.GetSemanticModel(source).Diagnostics;
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.ReservedLuauKeyword,
            $"'{keyword}' is a reserved Luau keyword and cannot be used as a declaration name."
        );
    }

    [Fact]
    public void ThrowsFor_ReservedLuauKeywordParameter()
    {
        var diagnostics = Utility.GetSemanticModel("fn foo(local: number): void {}").Diagnostics;
        Utility.AssertDiagnostic(diagnostics, InternalCodes.ReservedLuauKeyword, "'local' is a reserved Luau keyword and cannot be used as a declaration name.");
    }

    [Theory]
    [InlineData("declare let local: number;", "local")]
    [InlineData("declare fn end(): void;", "end")]
    [InlineData("declare fn f(function: number): void;", "function")]
    [InlineData("declare interface repeat;", "repeat")]
    public void ThrowsFor_ReservedLuauKeywordName_InAmbientDeclaration(string source, string keyword)
    {
        var diagnostics = Utility.GetSemanticModel(source).Diagnostics;
        Utility.AssertDiagnostic(
            diagnostics,
            InternalCodes.ReservedLuauKeyword,
            $"'{keyword}' is a reserved Luau keyword and cannot be used as a declaration name."
        );
    }

    [Fact]
    public void Allows_SelfAsDeclarationName()
    {
        var model = Utility.GetSemanticModel("fn foo(self: number): void {}");
        Assert.Null(model.Diagnostics.Find(d => d.Code == InternalCodes.ReservedLuauKeyword));
    }
}
