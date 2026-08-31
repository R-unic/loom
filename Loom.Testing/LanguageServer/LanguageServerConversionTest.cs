using Loom.Core.Diagnostics;
using Loom.Core.Text;
using Loom.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using InternalDiagnosticSeverity = Loom.Core.Diagnostics.DiagnosticSeverity;
using LoomDiagnostic = Loom.Core.Diagnostics.Diagnostic;
using Location = Loom.Core.Text.Location;
using LspDiagnosticSeverity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity;

namespace Loom.Testing.LanguageServer;

[Collection("Assembly")]
public class LanguageServerConversionTest
{
    [Fact]
    public void ToPosition_ConvertsOneBasedLineAndZeroBasedCharacterCorrectly()
    {
        var file = Utility.TestFile("let a = 1;\nlet b = 2;");
        var location = new Location(file, file.SourceText.IndexOf('2'));

        var position = Conversion.ToPosition(location);

        Assert.Equal(1, position.Line);
        Assert.Equal(8, position.Character);
    }

    [Fact]
    public void ToRange_ConvertsStartAndEnd()
    {
        var file = Utility.TestFile("let x = 1;");
        var span = new LocationSpan(new Location(file, 4), new Location(file, 5));

        var range = Conversion.ToRange(span);

        Assert.Equal(0, range.Start.Line);
        Assert.Equal(4, range.Start.Character);
        Assert.Equal(0, range.End.Line);
        Assert.Equal(5, range.End.Character);
    }

    [Theory]
    [InlineData(InternalDiagnosticSeverity.Error, LspDiagnosticSeverity.Error)]
    [InlineData(InternalDiagnosticSeverity.Warn, LspDiagnosticSeverity.Warning)]
    [InlineData(InternalDiagnosticSeverity.Info, LspDiagnosticSeverity.Information)]
    public void ToSeverity_MapsEverySeverity(InternalDiagnosticSeverity loom, LspDiagnosticSeverity lsp) => Assert.Equal(lsp, Conversion.ToSeverity(loom));

    [Fact]
    public void ToDiagnostic_TranslatesRangeSeverityCodeAndMessage()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let x: number = \"hi\";");
        var diagnostic = Assert.Single(diagnostics.Set, d => d.Code == InternalCodes.TypeMismatch);

        var lspDiagnostic = Conversion.ToDiagnostic(diagnostic);

        Assert.Equal(Conversion.ToRange(diagnostic.Span), lspDiagnostic.Range);
        Assert.Equal(LspDiagnosticSeverity.Error, lspDiagnostic.Severity);
        Assert.Equal(InternalCodes.TypeMismatch, lspDiagnostic.Code?.String);
        Assert.Equal(diagnostic.Message, lspDiagnostic.Message);
        Assert.Equal("loom", lspDiagnostic.Source);
    }

    [Fact]
    public void ToDiagnostic_CarriesTheHintAsRelatedInformation_RatherThanInTheMessage()
    {
        var lspDiagnostic = Conversion.ToDiagnostic(OnDisk("boom", "try something else"));

        Assert.Equal("boom", lspDiagnostic.Message);
        Assert.Equal("try something else", Assert.Single(lspDiagnostic.RelatedInformation!).Message);
    }

    [Fact]
    public void ToDiagnostic_PointsRelatedInformationAtTheFileItWasReportedIn()
    {
        var related = Assert.Single(Conversion.ToDiagnostic(OnDisk("boom", "try something else")).RelatedInformation!);
        Assert.Equal(DocumentUri.FromFileSystemPath(RootedPath), related.Location.Uri);
    }

    [Fact]
    public void ToDiagnostic_WithNoHint_CarriesNoRelatedInformation()
    {
        var diagnostics = Utility.GetTypeCheckerDiagnostics("let x: number = \"hi\";");
        var diagnostic = Assert.Single(diagnostics.Set, d => d.Code == InternalCodes.TypeMismatch);
        Assert.Null(diagnostic.Hint);

        Assert.Null(Conversion.ToDiagnostic(diagnostic).RelatedInformation);
    }

    [Fact]
    public void ToDiagnostic_TagsCodeThatDoesNothingAsUnnecessary()
    {
        var diagnostics = Utility.GetAnalysisDiagnostics("fn main(): void {\n  return;\n  let x = 1;\n}");
        var diagnostic = Assert.Single(diagnostics.Set, d => d.Code == InternalCodes.UnreachableCode);

        Assert.Equal(DiagnosticTag.Unnecessary, Assert.Single(Conversion.ToDiagnostic(diagnostic).Tags!));
    }

    [Fact]
    public void ToDiagnostic_TagsADeprecatedMemberAsDeprecated()
    {
        var diagnostics = Utility.GetAnalysisDiagnostics(
            """
            [deprecated("use 'add' instead")]
            fn sum(x: number): number { return x; }

            let total = sum(1);
            """
        );

        var diagnostic = Assert.Single(diagnostics.Set, d => d.Code == InternalCodes.DeprecatedMember);
        Assert.Equal(DiagnosticTag.Deprecated, Assert.Single(Conversion.ToDiagnostic(diagnostic).Tags!));
    }

    /// <summary>An intrinsic's "path" is an embedded resource name, so there is no document for a client to open at it.</summary>
    [Fact]
    public void ToDiagnostic_ForASpanWithNoRealFile_HasNoRelatedInformationToPointAt()
    {
        var file = Utility.TestFile("let x = 1;");
        var span = new LocationSpan(new Location(file, 0), new Location(file, 1));
        var diagnostic = new LoomDiagnostic(span, InternalDiagnosticSeverity.Error, "L001", "boom", "try something else");

        Assert.Null(Conversion.ToDiagnostic(diagnostic).RelatedInformation);
    }

    [Fact]
    public void DiagnosticsFor_WithNoResult_IsEmptySoTheClientClearsWhatItHas() =>
        Assert.Empty(Conversion.DiagnosticsFor(null, DocumentUri.FromFileSystemPath(RootedPath)));

    [Fact]
    public void DiagnosticsFor_KeepsOnlyTheDiagnosticsRaisedInTheRequestedFile() =>
        Utility.WithTempProject(
            [("main.loom", "let x: string = 1;"), ("other.loom", "let y: number = \"two\";")],
            (unit, result) =>
            {
                var mainPath = unit.SourceFiles.First(file => file.Name == "main.loom").AbsolutePath;
                var diagnostics = Conversion.DiagnosticsFor(result, DocumentUri.FromFileSystemPath(mainPath));

                Assert.Equal(InternalCodes.TypeMismatch, Assert.Single(diagnostics).Code?.String);
                Assert.Contains("'string'", Assert.Single(diagnostics).Message);
            }
        );

    /// <summary>
    ///     A compile spans the whole project, so an edit in one file routinely breaks another. Reporting only
    ///     the file that was edited computes those and throws them away.
    /// </summary>
    [Fact]
    public void DiagnosticsByFile_ReportsEveryFileTheCompileFoundSomethingIn() =>
        Utility.WithTempProject(
            [("main.loom", "let x: string = 1;"), ("other.loom", "let y: number = \"two\";"), ("clean.loom", "let z = 1;")],
            (_, result) =>
            {
                var byFile = Conversion.DiagnosticsByFile(result);

                Assert.Equal(2, byFile.Count);
                Assert.Contains(byFile.Keys, uri => uri.Path.EndsWith("main.loom"));
                Assert.Contains(byFile.Keys, uri => uri.Path.EndsWith("other.loom"));
                Assert.DoesNotContain(byFile.Keys, uri => uri.Path.EndsWith("clean.loom"));
            }
        );

    /// <summary>An intrinsic is an embedded resource, so there is no document to attach its diagnostics to.</summary>
    [Fact]
    public void DiagnosticsByFile_LeavesOutFilesAClientCouldNotOpen() =>
        Utility.WithTempProject(
            [("main.loom", "let x: string = 1;")],
            (_, result) => Assert.All(Conversion.DiagnosticsByFile(result).Keys, uri => Assert.True(Path.IsPathRooted(uri.GetFileSystemPath())))
        );

    private static readonly string RootedPath = Path.Combine(Path.GetTempPath(), "loom-conversion-test", "main.loom");

    private static LoomDiagnostic OnDisk(string message, string? hint)
    {
        var file = new SourceFile(RootedPath, "let x = 1;");
        return new LoomDiagnostic(new LocationSpan(new Location(file, 0), new Location(file, 1)), InternalDiagnosticSeverity.Error, "L001", message, hint);
    }
}
