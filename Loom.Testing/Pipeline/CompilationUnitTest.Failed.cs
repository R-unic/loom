using Loom.Core.Diagnostics;
using Loom.Core.Text;
using Loom.Testing;

namespace Loom.Testing.Pipeline;

public partial class CompilationUnitTest
{

    [Fact]
    public void Failed_IsFalse_ForACleanCompile() =>
        Utility.WithTempProject([("main.loom", "let x = 1;")], (_, result) => Assert.False(result.Failed));

    /// <summary>
    ///     Every case below compiles without <see cref="DiagnosticOptions.FailFast" />, which is how anything
    ///     compiling on another project's behalf has to run: the first bad dependency must not take the process
    ///     down with it. The result has to say so on its own.
    /// </summary>
    [Fact]
    public void Failed_IsTrue_WhenAFileHasAnError() =>
        Utility.WithTempProject(
            [("main.loom", "let x: number = \"not a number\";")],
            (_, result) => Assert.True(result.Failed)
        );

    [Fact]
    public void Failed_IsTrue_WhenADeclarationFileHasAnError() =>
        Utility.WithTempProject(
            [("main.loom", "let x = 1;"), ("globals.d.loom", "let oops = 1;")],
            (_, result) => Assert.True(result.Failed)
        );

    [Fact]
    public void Failed_IsTrue_WhenAFileCouldNotBeCompiled() =>
        Utility.WithTempProject(
            [("main.loom", "let")],
            (_, result) => Assert.True(result.Failed)
        );

    [Fact]
    public void Failed_IsFalse_ForWarningsAlone() =>
        Utility.WithTempProject(
            [("main.loom", "let x = 1;")],
            (_, result) =>
            {
                result.Diagnostics.Warn(LocationSpan.Empty(SourceFile.Empty), "TEST", "a warning is not a failure");
                Assert.False(result.Failed);
            }
        );

}
