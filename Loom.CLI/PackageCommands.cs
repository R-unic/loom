using System.Text;
using Loom.Config;
using Loom.Core.Diagnostics;
using Loom.Core.Pipeline;
using Loom.Packages;

namespace Loom.CLI;

/// <summary>
///     The two verbs that work on packages rather than on code: <c>add</c>, which changes what a project depends on,
///     and <c>publish</c>, which offers a project to an index as a version of a package.
/// </summary>
/// <remarks>
///     Both are thin on purpose. Deciding what to write into a manifest and what makes up a published version is
///     <c>Loom.Packages</c>' work, which is testable without a terminal; what is here is reading the command line's
///     answer to those and saying what happened.
/// </remarks>
internal static class PackageCommands
{
    /// <summary>
    ///     Adds the packages named to the project's manifest and restores it, so that the next build compiles against
    ///     them.
    /// </summary>
    public static int Add(AddOptions options)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (!Projects.TryLocate(options.Directory, out var config))
            return 1;

        var requests = new List<PackageRequest>();
        foreach (var package in options.Packages)
        {
            if (!PackageRequest.TryParse(package, options.DevelopmentOnly, out var request, out var error))
            {
                Log.Fatal($"'{package}' is not a package to add: {error}");
                return 1;
            }

            requests.Add(request);
        }

        var added = PackageAdder.Add(config, requests, out var diagnostics);
        if (added == null)
        {
            Projects.Report(diagnostics);
            return 1;
        }

        foreach (var package in added)
        {
            var development = package.IsDevelopmentOnly ? ", development-only" : string.Empty;
            Log.Info(
                $"Added {Colors.Bold}{Colors.White}{package.Name}{Colors.Reset} {Colors.Cyan}{package.Requirement}{Colors.Reset}"
                + $" {Colors.Dim}({package.Version}{development}){Colors.Reset}"
            );
        }

        return 0;
    }

    /// <summary>
    ///     Publishes the project as one version of a package: what it is made of, that it compiles, and then the
    ///     index.
    /// </summary>
    /// <remarks>
    ///     It compiles before it publishes because a published version is never replaced. Everything else a publish
    ///     gets wrong can be fixed by publishing the next version; source that does not compile is in the index for
    ///     good, and every consumer that resolves it inherits the failure. <c>--allow-dirty</c> is there for the
    ///     publisher who knows something the compiler does not — a package for a runtime whose types are not
    ///     installed here, a release cut from a machine that cannot build it — and it says what it let through, since
    ///     the version it produces is the one thing about a publish nobody can take back.
    /// </remarks>
    public static int Publish(PublishOptions options)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (!Projects.TryLocate(options.Directory, out var config))
            return 1;

        var payload = PackagePublisher.Prepare(config, out var prepareDiagnostics);
        if (payload == null)
        {
            Projects.Report(prepareDiagnostics);
            return 1;
        }

        if (options.DryRun)
        {
            Describe(payload);
            Log.Info($"{Colors.Dim}Nothing was published; this was a dry run.{Colors.Reset}");
            return 0;
        }

        var index = PackageIndexes.Open(config, out var indexDiagnostics);
        if (index == null)
        {
            Projects.Report(indexDiagnostics);
            return 1;
        }

        if (!PackagePublisher.CanPublish(payload, index, out var refusal))
        {
            Projects.Report(refusal);
            return 1;
        }

        if (options.AllowDirty)
            Log.Info($"{Colors.Yellow}Publishing without checking that the project compiles.{Colors.Reset}");
        else if (!Compiles(config))
            return 1;

        if (!PackagePublisher.Publish(payload, index, out var publishDiagnostics))
        {
            Projects.Report(publishDiagnostics);
            return 1;
        }

        Log.Info(
            $"Published {Colors.Bold}{Colors.White}{payload.Name}{Colors.Reset} {Colors.Cyan}{payload.Version}{Colors.Reset}"
            + $" to {Colors.Dim}{index.Description}{Colors.Reset}."
        );

        return 0;
    }

    /// <summary>Lists what a publish would send, in the order the payload holds it.</summary>
    private static void Describe(PackagePayload payload)
    {
        Log.Info($"{Colors.Bold}{Colors.White}{payload.Name}{Colors.Reset} {Colors.Cyan}{payload.Version}{Colors.Reset} publishes:");
        foreach (var file in payload.Files)
            Console.WriteLine($"  {Colors.Dim}{file}{Colors.Reset}");
    }

    /// <summary>
    ///     Whether the project compiles as it stands. Its own dependencies are restored first, since a package's
    ///     sources are compiled against them like any other project's, and nothing is emitted: what is being asked is
    ///     whether the source is publishable, not what it would build into for one consumer.
    /// </summary>
    private static bool Compiles(LoomConfig project)
    {
        if (!PackageManager.Restore(project, out var restoreDiagnostics))
        {
            Projects.Report(restoreDiagnostics);
            return false;
        }

        project.NoEmit = true;
        var roots = ProjectLoader.Load(project, out var loadDiagnostics);
        if (roots == null)
        {
            Projects.Report(loadDiagnostics);
            return false;
        }

        var result = new CompilationUnit(roots).Compile();
        Log.OutputResult(result);
        if (!result.Failed)
            return true;

        Log.Fatal("the project does not compile, so it was not published.");
        return false;
    }
}
