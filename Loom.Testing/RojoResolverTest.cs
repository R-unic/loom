using Loom.Config;

namespace Loom.Testing;

public sealed class RojoResolverTest : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "loom_rojo_test_" + Guid.NewGuid().ToString("N"));

    public RojoResolverTest() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void Resolves_Runtime_Through_Directory_Mapping()
    {
        var dir = ProjectDir("dir_mapping");
        Write(dir, Path.Combine("include", RojoResolver.RuntimeFileName), "return {}");
        Write(
            dir,
            RojoResolver.ProjectFileName,
            """
            {
              "tree": {
                "$className": "DataModel",
                "ReplicatedStorage": {
                  "include": { "$path": "include" }
                }
              }
            }
            """
        );

        var resolver = RojoResolver.FromProjectDirectory(dir);
        Assert.NotNull(resolver);
        Assert.Equal(["ReplicatedStorage", "include", "loom_runtime"], resolver.ResolveRuntimePath());
    }

    [Fact]
    public void Resolves_Runtime_Through_Nested_Directory_Mapping()
    {
        var dir = ProjectDir("nested_mapping");
        Write(dir, Path.Combine("out", "vendor", RojoResolver.RuntimeFileName), "return {}");
        Write(
            dir,
            RojoResolver.ProjectFileName,
            """
            {
              "tree": {
                "$className": "DataModel",
                "ReplicatedStorage": {
                  "packages": { "$path": "out" }
                }
              }
            }
            """
        );

        var resolver = RojoResolver.FromProjectDirectory(dir);
        Assert.NotNull(resolver);
        Assert.Equal(["ReplicatedStorage", "packages", "vendor", "loom_runtime"], resolver.ResolveRuntimePath());
    }

    [Fact]
    public void Resolves_Runtime_When_Path_Points_At_File()
    {
        var dir = ProjectDir("file_mapping");
        Write(dir, Path.Combine("runtime", RojoResolver.RuntimeFileName), "return {}");
        Write(
            dir,
            RojoResolver.ProjectFileName,
            """
            {
              "tree": {
                "$className": "DataModel",
                "ReplicatedStorage": {
                  "LoomRuntime": { "$path": "runtime/loom_runtime.luau" }
                }
              }
            }
            """
        );

        var resolver = RojoResolver.FromProjectDirectory(dir);
        Assert.NotNull(resolver);
        Assert.Equal(["ReplicatedStorage", "LoomRuntime"], resolver.ResolveRuntimePath());
    }

    [Fact]
    public void Returns_Null_When_Runtime_Not_Mapped()
    {
        var dir = ProjectDir("no_runtime");
        Write(dir, Path.Combine("src", "main.luau"), "return {}");
        Write(
            dir,
            RojoResolver.ProjectFileName,
            """
            {
              "tree": {
                "$className": "DataModel",
                "ServerScriptService": {
                  "App": { "$path": "src" }
                }
              }
            }
            """
        );

        var resolver = RojoResolver.FromProjectDirectory(dir);
        Assert.NotNull(resolver);
        Assert.Null(resolver.ResolveRuntimePath());
    }

    [Fact]
    public void Resolves_Path_Under_A_Directory_Mapping()
    {
        var resolver = ProjectWithOutputMapping("path_dir", out var dir);
        Assert.Equal(
            ["ReplicatedStorage", "Shared", "math"],
            resolver.ResolvePath(Path.Combine(dir, "dist", "math.luau"))
        );
    }

    [Fact]
    public void Resolves_Path_Of_A_Nested_File()
    {
        var resolver = ProjectWithOutputMapping("path_nested", out var dir);
        Assert.Equal(
            ["ReplicatedStorage", "Shared", "util", "helpers"],
            resolver.ResolvePath(Path.Combine(dir, "dist", "util", "helpers.luau"))
        );
    }

    [Fact]
    public void Folds_An_Init_File_Into_Its_Folder()
    {
        var resolver = ProjectWithOutputMapping("path_init", out var dir);
        Assert.Equal(
            ["ReplicatedStorage", "Shared", "util"],
            resolver.ResolvePath(Path.Combine(dir, "dist", "util", "init.luau"))
        );
    }

    [Fact]
    public void Resolves_Path_When_The_Mapping_Points_At_The_File_Itself()
    {
        var dir = ProjectDir("path_file");
        Write(dir, Path.Combine("dist", "math.luau"), "return {}");
        Write(
            dir,
            RojoResolver.ProjectFileName,
            """
            {
              "tree": {
                "$className": "DataModel",
                "ReplicatedStorage": {
                  "Math": { "$path": "dist/math.luau" }
                }
              }
            }
            """
        );

        var resolver = RojoResolver.FromProjectDirectory(dir);
        Assert.NotNull(resolver);

        // the node holding the $path already names the instance
        Assert.Equal(["ReplicatedStorage", "Math"], resolver.ResolvePath(Path.Combine(dir, "dist", "math.luau")));
    }

    [Fact]
    public void Prefers_The_Deepest_Mapping_Covering_A_Path()
    {
        var dir = ProjectDir("path_nested_mapping");
        Write(dir, Path.Combine("dist", "vendor", "math.luau"), "return {}");
        Write(
            dir,
            RojoResolver.ProjectFileName,
            """
            {
              "tree": {
                "$className": "DataModel",
                "ReplicatedStorage": {
                  "$path": "dist",
                  "Vendor": { "$path": "dist/vendor" }
                }
              }
            }
            """
        );

        var resolver = RojoResolver.FromProjectDirectory(dir);
        Assert.NotNull(resolver);
        Assert.Equal(["ReplicatedStorage", "Vendor", "math"], resolver.ResolvePath(Path.Combine(dir, "dist", "vendor", "math.luau")));
    }

    [Fact]
    public void Returns_Null_For_A_Path_No_Mapping_Covers()
    {
        var resolver = ProjectWithOutputMapping("path_unmapped", out var dir);
        Assert.Null(resolver.ResolvePath(Path.Combine(dir, "elsewhere", "math.luau")));
    }

    [Fact]
    public void FromProjectDirectory_Returns_Null_Without_Project_File()
    {
        var dir = ProjectDir("no_project");
        Assert.Null(RojoResolver.FromProjectDirectory(dir));
    }

    [Fact]
    public void FromProjectDirectory_Returns_Null_For_Missing_Directory() => Assert.Null(RojoResolver.FromProjectDirectory(Path.Combine(_root, "does_not_exist")));

    private RojoResolver ProjectWithOutputMapping(string name, out string directory)
    {
        directory = ProjectDir(name);
        Write(directory, Path.Combine("dist", "math.luau"), "return {}");
        Write(directory, Path.Combine("dist", "util", "init.luau"), "return {}");
        Write(directory, Path.Combine("dist", "util", "helpers.luau"), "return {}");
        Write(
            directory,
            RojoResolver.ProjectFileName,
            """
            {
              "tree": {
                "$className": "DataModel",
                "ReplicatedStorage": {
                  "Shared": { "$path": "dist" }
                }
              }
            }
            """
        );

        var resolver = RojoResolver.FromProjectDirectory(directory);
        Assert.NotNull(resolver);

        return resolver;
    }

    private string ProjectDir(string name)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Write(string projectDir, string relativePath, string content)
    {
        var path = Path.Combine(projectDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}