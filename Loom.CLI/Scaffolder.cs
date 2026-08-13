using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Loom.Config;
using Loom.Core.Diagnostics;
using Loom.Core.Pipeline;

namespace Loom.CLI;

internal static class Scaffolder
{
    private static readonly IReadOnlyList<PromptOption<ProjectType>> _projectTypeOptions =
    [
        new(ProjectType.Game, "Game", "Scripts compiled straight into a Rojo tree."),
        new(ProjectType.Library, "Library", "A package other Loom projects can depend on."),
        new(ProjectType.Plugin, "Plugin", "A Roblox Studio plugin.")
    ];

    public static int NewProject(string directory)
    {
        var projectDirectory = Path.GetFullPath(directory);
        if (File.Exists(Path.Combine(projectDirectory, ConfigReader.ConfigFileName)))
        {
            Log.Fatal($"a Loom project already exists in '{projectDirectory}'.");
            return 1;
        }

        var name = Path.GetFileName(projectDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        Console.WriteLine($"{Colors.Bold}{Colors.Pink}Let's create a new Loom project!{Colors.Reset}{Colors.Dim} ({name}){Colors.Reset}");
        Console.WriteLine();

        var projectType = Prompt.Select("What kind of project would you like to create?", _projectTypeOptions);
        Console.WriteLine();
        var initializeGit = Prompt.Confirm("Initialize a git repository?");
        Console.WriteLine();

        Scaffold(projectDirectory, projectType, name);
        if (initializeGit)
            InitializeGitRepository(projectDirectory);

        Log.Info($"Created {Colors.Bold}{Colors.White}{name}{Colors.Reset}. Next steps:");
        Console.WriteLine($"  {Colors.Dim}cd{Colors.Reset} {directory}");
        Console.WriteLine($"  {Colors.Dim}loom{Colors.Reset} build");
        return 0;
    }

    private static void Scaffold(string projectDirectory, ProjectType projectType, string projectName)
    {
        Directory.CreateDirectory(projectDirectory);
        var sourceDirectory = Path.Combine(projectDirectory, "src");
        Directory.CreateDirectory(sourceDirectory);

        File.WriteAllText(
            Path.Combine(projectDirectory, ConfigReader.ConfigFileName),
            GetConfigContent(projectType)
        );
        
        var filesWritten = new List<string> { "src/main.loom", ".gitignore", "loom-config.toml" };
        if (projectType == ProjectType.Game)
        {
            const string projectFileName = "default.project.json";
            File.WriteAllText(
                Path.Combine(projectDirectory, projectFileName),
                GetRojoProjectContent(projectName)
            );
            filesWritten.Add(projectFileName);
        }

        File.WriteAllText(Path.Combine(sourceDirectory, "main.loom"), StarterSource(projectType));
        File.WriteAllText(Path.Combine(projectDirectory, ".gitignore"), "dist/" + Environment.NewLine);
        Log.Info($"Wrote {string.Join(", ", filesWritten.SkipLast(1).Select(colorFileName))}, and {colorFileName(filesWritten.Last())}.");

        return;

        string colorFileName(string fileName) => $"{Colors.Cyan}{fileName}{Colors.Reset}";
    }

    private static string GetConfigContent(ProjectType projectType) =>
        $"""
        project_type = "{ProjectTypeName(projectType)}"

        [files]
        source_directory = "src"
        output_directory = "dist"
        """ + Environment.NewLine;

    /// <remarks>
    ///     The name goes through the JSON serializer rather than straight into the template: a directory may
    ///     legally be named with a quote or a backslash, and either one written verbatim produces a manifest
    ///     that nothing can read back.
    /// </remarks>
    private static string GetRojoProjectContent(string projectName) =>
        $$"""
        {
          "name": {{JsonSerializer.Serialize(projectName)}},
          "globIgnorePaths": ["**/loom-config.toml"],
          "tree": {
            "$className": "DataModel",
            "ServerScriptService": {
              "$className": "ServerScriptService",
              "Loom": {
                "$path": "dist"
              }
            },
            "ReplicatedStorage": {
              "$className": "ReplicatedStorage",
              "include": {
                "$path": "include"
              }
            }
          }
        }
        """;

    private static string ProjectTypeName(ProjectType projectType) =>
        projectType switch
        {
            ProjectType.Game => "game",
            ProjectType.Library => "library",
            ProjectType.Plugin => "plugin",
            _ => throw new ArgumentOutOfRangeException(nameof(projectType))
        };

    private static string StarterSource(ProjectType projectType) =>
        projectType switch
        {
            ProjectType.Library => "export fn hello(): string -> \"Hello from your Loom library!\";",
            ProjectType.Plugin => "print(\"Hello from your Loom plugin!\");",
            _ => "print(\"Hello from your Loom game!\");"
        };

    private static void InitializeGitRepository(string projectDirectory)
    {
        try
        {
            var startInfo = new ProcessStartInfo("git", "init")
            {
                WorkingDirectory = projectDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using var process = Process.Start(startInfo);
            process?.WaitForExit();

            if (process?.ExitCode == 0)
                Log.Info("Initialized a git repository.");
            else
                Log.Fatal("'git init' failed; is git installed and on PATH?");
        }
        catch (Win32Exception exception)
        {
            Log.Fatal($"could not run 'git init': {exception.Message}");
        }
    }
}
