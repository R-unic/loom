using Loom.Config;
using Loom.Core;
using Loom.Core.Diagnostics;

namespace Loom.Testing;

[Collection("Assembly")]
public sealed class RuntimeImportTest : IDisposable
{
    private const string RuntimeSource = "event abc;";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "loom_runtime_import_test_" + Guid.NewGuid().ToString("N"));

    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void Maps_To_Resolved_When_Rojo_Contains_Runtime()
    {
        var dir = ProjectDir("resolved", RojoMapping("packages", "include"), true);
        var unit = new CompilationUnit(new LoomConfig { ProjectDirectory = dir });

        Assert.Equal(RuntimeImportStatus.Resolved, unit.RuntimeImport.Status);
        Assert.Equal("@game/ReplicatedStorage/packages/loom_runtime", unit.RuntimeImport.Path);
    }

    [Fact]
    public void Maps_To_NotFoundInRojo_When_Runtime_Absent()
    {
        var dir = ProjectDir("not_found", RojoMapping("packages", "include"), false);
        var unit = new CompilationUnit(new LoomConfig { ProjectDirectory = dir });

        Assert.Equal(RuntimeImportStatus.NotFoundInRojo, unit.RuntimeImport.Status);
        Assert.Equal(RuntimeImport.DefaultPath, unit.RuntimeImport.Path);
    }

    [Fact]
    public void Maps_To_RojoMissing_Without_Project_File()
    {
        var dir = ProjectDir("missing", null, false);
        var unit = new CompilationUnit(new LoomConfig { ProjectDirectory = dir });

        Assert.Equal(RuntimeImportStatus.RojoMissing, unit.RuntimeImport.Status);
        Assert.Equal(RuntimeImport.DefaultPath, unit.RuntimeImport.Path);
    }

    [Fact]
    public void Emits_Resolved_Require_Path_In_Output()
    {
        var result = CompileProject(RojoMapping("packages", "include"), true);
        Assert.DoesNotContain(result.Diagnostics.Set, d => d.Code == InternalCodes.RuntimeLibraryNotFound);
        Assert.Contains("require(\"@game/ReplicatedStorage/packages/loom_runtime\")", ConcreteOutput(result));
    }

    [Fact]
    public void Warns_And_Falls_Back_When_Runtime_Not_Found_In_Rojo()
    {
        var result = CompileProject(RojoMapping("packages", "include"), false);

        var warning = Assert.Single(result.Diagnostics.Set, d => d.Code == InternalCodes.RuntimeLibraryNotFound);
        Assert.Equal(DiagnosticSeverity.Warn, warning.Severity);
        Assert.Contains($"require(\"{RuntimeImport.DefaultPath}\")", ConcreteOutput(result));
    }

    private CompilationResult CompileProject(string? rojo, bool withRuntime)
    {
        var dir = ProjectDir("compile_" + Guid.NewGuid().ToString("N"), rojo, withRuntime);
        Write(dir, "loom-config.toml", "project_type = \"game\"\n");
        Write(dir, Path.Combine("src", "main.loom"), RuntimeSource);

        var config = ConfigReader.LocateFromDirectory(dir);
        Assert.NotNull(config);
        config.NoEmit = true;

        return new CompilationUnit(config).Compile();
    }

    private static string ConcreteOutput(CompilationResult result) => Assert.Single(result.Files, f => !f.SourceFile.IsDeclaration).RenderedLuau;

    // Maps a runtime-containing folder ($path "include") at ReplicatedStorage.<instanceName>.
    private static string RojoMapping(string instanceName, string path) =>
        $$"""
        {
          "tree": {
            "$className": "DataModel",
            "ReplicatedStorage": {
              "{{instanceName}}": { "$path": "{{path}}" }
            }
          }
        }
        """;

    private string ProjectDir(string name, string? rojo, bool withRuntime)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        if (rojo != null)
            Write(dir, RojoResolver.ProjectFileName, rojo);

        if (withRuntime)
            Write(dir, Path.Combine("include", RojoResolver.RuntimeFileName), "return {}");

        return dir;
    }

    private static void Write(string projectDir, string relativePath, string content)
    {
        var path = Path.Combine(projectDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}