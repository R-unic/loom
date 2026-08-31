using Loom.Config;

namespace Loom.Packages;

/// <summary>
///     Turns a project's requirements into a lock file: one version per package, chosen from what an index
///     publishes, covering everything the build reaches transitively.
/// </summary>
/// <remarks>
///     Every requirement on a package is intersected into the single interval <see cref="VersionRequirement" />
///     already is, so combining what several dependents ask for needs no search — the highest published version
///     inside that interval is the answer, and an empty intersection is a conflict to name rather than a state to
///     back out of. What this deliberately does not do is search: if choosing the newest version a package allows
///     leaves some other package unsatisfiable, that is reported, not worked around by trying older ones. A
///     resolver that backtracks can be written behind the same call; nothing here or in the lock format assumes
///     this one.
/// </remarks>
public static class LockResolver
{
    /// <summary>
    ///     How many times the choices may be revised before resolution gives up. Each round re-derives every
    ///     requirement from the versions currently chosen, so a graph that settles does so in a few; the bound is
    ///     here so one that does not is reported rather than spun on.
    /// </summary>
    private const int MaximumRounds = 64;

    /// <summary>
    ///     Resolves <paramref name="entry" />'s dependencies — development ones included, since the project is the
    ///     one being developed — against <paramref name="index" />, or answers <see langword="null" /> with the
    ///     <paramref name="diagnostics" /> saying which requirement could not be met.
    /// </summary>
    /// <param name="preferred">
    ///     A lock to keep to where it still fits: a version already chosen and still acceptable is chosen again,
    ///     so re-resolving after one requirement changes does not quietly move every other package to its newest
    ///     release.
    /// </param>
    public static LockFile? Resolve(
        LoomConfig entry,
        IPackageIndex index,
        LockFile? preferred,
        out IReadOnlyList<ConfigDiagnostic> diagnostics
    )
    {
        var reported = new List<ConfigDiagnostic>();
        diagnostics = reported;

        var chosen = new Dictionary<PackageName, PublishedPackage>();
        for (var round = 0; round < MaximumRounds; round++)
        {
            var demands = CollectDemands(entry, chosen);
            var settled = true;
            foreach (var (package, requests) in demands)
            {
                var requirement = VersionRequirement.Intersect(requests.Select(request => request.Requirement));
                if (requirement == null)
                {
                    reported.Add(Conflict(package, requests));
                    return null;
                }

                if (chosen.TryGetValue(package, out var current) && requirement.Satisfies(current.Version))
                    continue;

                var publications = index.Publications(package, out var indexDiagnostics);
                if (indexDiagnostics.Count > 0)
                {
                    reported.AddRange(indexDiagnostics);
                    return null;
                }

                var publication = Choose(publications, requirement, preferred?.Find(package)?.Version);
                if (publication == null)
                {
                    reported.Add(Unsatisfiable(package, requirement, requests, index, publications));
                    return null;
                }

                chosen[package] = publication;
                settled = false;
            }

            // a package chosen for a version that has since been replaced may no longer be demanded by anything
            foreach (var package in chosen.Keys.Where(package => !demands.ContainsKey(package)).ToArray())
                chosen.Remove(package);

            if (settled)
                return new LockFile(chosen.Values.Select(publication => publication.ToLockedPackage()));
        }

        reported.Add(
            new ConfigDiagnostic($"could not settle on one version per package after {MaximumRounds} rounds; the requirements may not have a single answer.")
        );

        return null;
    }

    /// <summary>
    ///     Every requirement in play, re-derived from the project and the versions chosen so far rather than
    ///     accumulated: a requirement written by a version no longer chosen is no longer a requirement, and
    ///     remembering it would narrow the answer with something nothing asks for.
    /// </summary>
    private static Dictionary<PackageName, List<Request>> CollectDemands(LoomConfig entry, Dictionary<PackageName, PublishedPackage> chosen)
    {
        var demands = new Dictionary<PackageName, List<Request>>();
        foreach (var dependency in entry.Dependencies.Values)
            Demand(demands, dependency.Name, new Request("the project", dependency.VersionRequirement));

        foreach (var publication in chosen.Values)
        {
            foreach (var dependency in publication.Dependencies)
                Demand(demands, dependency.Name, new Request($"'{publication}'", dependency.VersionRequirement));
        }

        return demands;
    }

    private static void Demand(Dictionary<PackageName, List<Request>> demands, PackageName package, Request request)
    {
        if (!demands.TryGetValue(package, out var requests))
            demands[package] = requests = [];

        requests.Add(request);
    }

    /// <summary>
    ///     The version to take: the one already locked when it still fits, and otherwise the newest published
    ///     version inside the requirement. Newest, because a requirement is a statement about what a dependent can
    ///     live with, and the answer it would pick for itself is the latest of those.
    /// </summary>
    /// <remarks>
    ///     <paramref name="publications" /> is taken newest last, as <see cref="IPackageIndex.Publications" />
    ///     promises, and the newest match is read off the end rather than searched for — an index answering in any
    ///     other order silently resolves to an older version, so one that cannot sort itself must be sorted on
    ///     receipt.
    ///     <para>
    ///         A yanked version is passed over here and nowhere else: the version already locked is kept whether it
    ///         has since been yanked or not, and <see cref="PackageInstaller" /> installs what the lock pins without
    ///         asking. A yank withdraws a version from being taken up, and a build already on it is precisely what
    ///         it is not trying to break.
    ///     </para>
    /// </remarks>
    private static PublishedPackage? Choose(IReadOnlyList<PublishedPackage> publications, VersionRequirement requirement, Version? locked)
    {
        if (locked != null && requirement.Satisfies(locked))
        {
            var kept = publications.FirstOrDefault(publication => publication.Version.Equals(locked));
            if (kept != null)
                return kept;
        }

        return publications.LastOrDefault(publication => !publication.Yanked && requirement.Satisfies(publication.Version));
    }

    private static ConfigDiagnostic Conflict(PackageName package, List<Request> requests) =>
        new($"no version of '{package}' satisfies every requirement on it: {Describe(requests)}.");

    private static ConfigDiagnostic Unsatisfiable(
        PackageName package,
        VersionRequirement requirement,
        List<Request> requests,
        IPackageIndex index,
        IReadOnlyList<PublishedPackage> publications
    )
    {
        var published = publications.Count == 0
            ? $"'{package}' is not published in '{index.Description}'"
            : $"'{index.Description}' publishes {PublishedPackage.Describe(publications)}";

        return new ConfigDiagnostic($"no published version of '{package}' satisfies '{requirement.ToComparatorString()}' ({Describe(requests)}); {published}.");
    }

    private static string Describe(List<Request> requests) =>
        string.Join(", ", requests.Select(request => $"{request.Owner} requires '{request.Requirement}'"));

    /// <summary>One dependent's requirement on one package, kept with who wrote it so a conflict can name both sides.</summary>
    private readonly record struct Request(string Owner, VersionRequirement Requirement);
}
