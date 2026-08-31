using System.Text.Json;
using Loom.Config;

namespace Loom.Packages;

/// <summary>
///     The body of a registry's index endpoint — one package, and every version of it the registry publishes —
///     read into the <see cref="PublishedPackage" />s resolution works in.
/// </summary>
/// <remarks>
///     Read through <see cref="JsonDocument" /> rather than a deserializer, as <c>RojoProject</c> is: the CLI
///     publishes ahead of time, where a reflecting serializer has no types to reflect over.
///     <para>
///         A document with one unreadable version in it is rejected whole. Dropping the entry instead would leave
///         a shorter list that still looks like an answer, and resolution reading the newest version off the end
///         of it would quietly pick an older one — the failure this whole out-parameter exists to keep apart from
///         "no such package".
///     </para>
/// </remarks>
internal static class IndexDocument
{
    /// <summary>
    ///     Every version of <paramref name="package" /> the body states, newest last, or <see langword="null" />
    ///     with the reason it could not be read.
    /// </summary>
    /// <param name="source">What the versions record as where they came from, which is the index as the manifest spells it.</param>
    public static IReadOnlyList<PublishedPackage>? Read(
        Stream body,
        PackageName package,
        string index,
        string? source,
        out IReadOnlyList<ConfigDiagnostic> diagnostics
    )
    {
        diagnostics = [];
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException exception)
        {
            diagnostics = [Unreadable(index, package, exception.Message)];
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                diagnostics = [Unreadable(index, package, "the body is not an object")];
                return null;
            }

            // an index answering about a package other than the one asked for cannot be told apart from one
            // answering about this one, once the name is dropped and only the versions are kept
            if (root.TryGetProperty("name", out var name)
                && (name.ValueKind != JsonValueKind.String || !PackageName.TryParse(name.GetString(), out var stated) || !stated.Equals(package)))
            {
                diagnostics = [Unreadable(index, package, $"it names '{name}'")];
                return null;
            }

            if (!root.TryGetProperty("versions", out var versions) || versions.ValueKind != JsonValueKind.Array)
            {
                diagnostics = [Unreadable(index, package, "it states no 'versions' array")];
                return null;
            }

            var published = new List<PublishedPackage>();
            foreach (var element in versions.EnumerateArray())
            {
                var publication = ReadVersion(element, package, source, out var reason);
                if (publication == null)
                {
                    diagnostics = [Unreadable(index, package, reason)];
                    return null;
                }

                published.Add(publication);
            }

            // sorted on receipt rather than taken on trust: Publications promises newest last and LockResolver
            // reads the newest match off the end, so an index answering in another order resolves to an older
            // version with nothing anywhere saying so
            published.Sort((left, right) => left.Version.CompareTo(right.Version));
            return published;
        }
    }

    private static PublishedPackage? ReadVersion(JsonElement element, PackageName package, string? source, out string reason)
    {
        reason = string.Empty;
        if (element.ValueKind != JsonValueKind.Object)
        {
            reason = "a version is not an object";
            return null;
        }

        if (!element.TryGetProperty("version", out var stated) || stated.ValueKind != JsonValueKind.String)
        {
            reason = "a version states no 'version'";
            return null;
        }

        if (!Version.TryParse(stated.GetString(), out var version, out var error))
        {
            reason = $"'{stated.GetString()}' is not a version ({error.TrimEnd('.')})";
            return null;
        }

        var dependencies = ReadDependencies(element, version, out reason);
        if (dependencies == null)
            return null;

        return new PublishedPackage(
            package,
            version,
            dependencies,
            String(element, "checksum"),
            source,
            element.TryGetProperty("yanked", out var yanked) && yanked.ValueKind == JsonValueKind.True
        );
    }

    private static List<Dependency>? ReadDependencies(JsonElement version, Version stated, out string reason)
    {
        reason = string.Empty;
        var dependencies = new List<Dependency>();
        if (!version.TryGetProperty("dependencies", out var element))
            return dependencies;

        if (element.ValueKind != JsonValueKind.Array)
        {
            reason = $"{stated} states a 'dependencies' that is not an array";
            return null;
        }

        foreach (var entry in element.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                reason = $"{stated} states a dependency that is not an object";
                return null;
            }

            if (!PackageName.TryParse(String(entry, "name"), out var name, out var nameError))
            {
                reason = $"{stated} depends on '{String(entry, "name")}' ({nameError.TrimEnd('.')})";
                return null;
            }

            if (!VersionRequirement.TryParse(String(entry, "requirement"), out var requirement, out var requirementError))
            {
                reason = $"{stated} requires '{String(entry, "requirement")}' of '{name}' ({requirementError.TrimEnd('.')})";
                return null;
            }

            dependencies.Add(new Dependency(name, requirement, entry.TryGetProperty("dev", out var dev) && dev.ValueKind == JsonValueKind.True));
        }

        return dependencies;
    }

    private static string? String(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static ConfigDiagnostic Unreadable(string index, PackageName package, string reason) =>
        new($"'{index}' answered something this cannot read about '{package}': {reason}.");
}
