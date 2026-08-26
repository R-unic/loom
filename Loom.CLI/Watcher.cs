using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Loom.Config;
using Loom.Core.Diagnostics;
using Loom.Core.Pipeline;
using Loom.Packages;

namespace Loom.CLI;

internal sealed class Watcher(DiagnosticOptions diagnosticOptions)
{
    private const string ConfigFileName = "loom-config.toml";

    private readonly HashSet<string> _pending = [];
    [AllowNull] private CompilationUnit _unit;

    public int Start(LoomConfig config)
    {
        Log.Info("Starting watch mode...");
        while (true)
        {
            var nextConfig = Watch(config);
            if (nextConfig == null)
                return 0;

            config = nextConfig;
        }
    }

    private LoomConfig? Watch(LoomConfig config)
    {
        var events = new BlockingCollection<string?>();
        _unit = CreateUnit(config);
        Log.OutputResult(_unit.Compile());

        _pending.Clear();

        using var sourceWatcher = CreateSourceWatcher(config, events);
        using var projectWatcher = CreateProjectWatcher(config, events);
        void cancelHandler(object? _, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            events.CompleteAdding();
        }

        Console.CancelKeyPress += cancelHandler;
        try
        {
            Log.Info($"Watching for changes. Press {Colors.Pink}Ctrl+C{Colors.Reset} to stop.");

            var restartNeeded = false;
            while (!events.IsCompleted)
            {
                if (!events.TryTake(out var path, TimeSpan.FromMilliseconds(200)))
                {
                    if (restartNeeded)
                        return ReloadConfig(config.ProjectDirectory) ?? config;

                    if (_pending.Count == 0) continue;

                    var changed = _pending.ToHashSet();
                    _pending.Clear();

                    Log.OutputResult(_unit.Recompile(changed.ToDictionary(k => k, File.ReadAllText)));
                    continue;
                }

                if (path == null)
                    restartNeeded = true;
                else
                    _pending.Add(path);
            }

            return null;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private FileSystemWatcher CreateSourceWatcher(LoomConfig config, BlockingCollection<string?> events)
    {
        var watcher = new FileSystemWatcher(config.Files.SourceDirectory)
        {
            Filter = $"*{FileManager.LoomExtension}",
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };

        watcher.Changed += (_, e) => OnSourceChanged(events, e);
        watcher.Created += (_, e) => OnSourceCreatedOrChanged(events, e);
        watcher.Deleted += (_, _) => OnSourceStructureChanged(events);
        watcher.Renamed += (_, _) => OnSourceStructureChanged(events);

        return watcher;
    }

    private static FileSystemWatcher CreateProjectWatcher(LoomConfig config, BlockingCollection<string?> events)
    {
        var watcher = new FileSystemWatcher(config.ProjectDirectory) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName, EnableRaisingEvents = true };
        watcher.Changed += (_, e) => OnProjectFileChanged(events, e);
        watcher.Created += (_, e) => OnProjectFileChanged(events, e);

        // a tool writing one of these files writes a temporary one and renames it over the old, so the rename is
        // the only event a watch sees - a package manager installing dependencies is exactly that shape
        watcher.Renamed += (_, e) => OnProjectFileChanged(events, e);
        
        return watcher;
    }

    // Some editors save by deleting and recreating the file (an atomic replace)
    // rather than writing in place, so a "created" event for an already-tracked
    // path is just its new content, not a structural change.
    private void OnSourceCreatedOrChanged(BlockingCollection<string?> events, FileSystemEventArgs e) =>
        events.Add(
            _unit.SourceFiles.FirstOrDefault(file => file.AbsolutePath == e.FullPath) != null
                ? e.FullPath
                : null
        );

    private static void OnSourceChanged(BlockingCollection<string?> events, FileSystemEventArgs e) => events.Add(e.FullPath);
    private static void OnSourceStructureChanged(BlockingCollection<string?> events) => events.Add(null);

    // the lock file is watched with the manifest: a package manager installing or updating a dependency changes
    // which projects the unit spans, and a unit already built cannot grow a root.
    private static void OnProjectFileChanged(BlockingCollection<string?> events, FileSystemEventArgs e)
    {
        if (e.Name is ConfigFileName or RojoResolver.ProjectFileName or LockFile.FileName)
            events.Add(null);
    }

    private static LoomConfig? ReloadConfig(string projectDirectory) => ConfigReader.LocateFromDirectory(projectDirectory, out _);

    /// <summary>
    ///     The unit for this pass of the watch. A project whose dependencies cannot be loaded still gets a watch
    ///     over its own files - the problem is reported and the next save of the manifest or the lock file starts
    ///     another pass - because a watch that exits on a stale lock leaves nothing running to notice it was fixed.
    /// </summary>
    private CompilationUnit CreateUnit(LoomConfig config)
    {
        // the same restore a one-shot build does: a watch started before the packages were installed, or across an
        // edit to [dependencies], is the case this exists for
        if (!PackageManager.Restore(config, out var restoreDiagnostics))
        {
            foreach (var diagnostic in restoreDiagnostics)
                Log.Fatal(diagnostic.ToString());
        }

        var roots = ProjectLoader.Load(config, out var diagnostics);
        foreach (var diagnostic in diagnostics)
            Log.Fatal(diagnostic.ToString());

        return roots == null ? new CompilationUnit(config, diagnosticOptions) : new CompilationUnit(roots, diagnosticOptions);
    }
}