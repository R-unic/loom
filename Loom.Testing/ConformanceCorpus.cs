using System.Text.Json;

namespace Loom.Testing;

/// <summary>
///     Reads the conformance corpora under <c>Conformance/</c>, which are checked in here and in
///     <c>rbx-loom/loom-pm</c> and executed by both test suites. Loom's requirements are not ordinary semver — a
///     requirement is one interval rather than a union, an unsatisfiable one is a parse error, and a pre-release is
///     accepted only by a bound naming a pre-release of the same release — so no off-the-shelf library agrees with
///     either implementation and only each other keeps them honest.
/// </summary>
/// <remarks>
///     C# is the reference implementation: when the two disagree, C# is right unless the disagreement is a bug
///     here, in which case it is fixed here and the case that caught it is added to the corpus. A case is read as
///     raw <see cref="JsonElement" />s rather than bound to a type, so a section the Go side grows a field for
///     still runs here instead of failing to deserialize.
/// </remarks>
internal static class ConformanceCorpus
{
    public const string Semver = "semver.json";
    public const string PackageNames = "package-name.json";

    private static readonly string _directory =
        $"..{Path.DirectorySeparatorChar}..{Path.DirectorySeparatorChar}..{Path.DirectorySeparatorChar}Conformance";

    /// <summary>Every case in one section of one corpus, in the order the file writes them.</summary>
    public static IReadOnlyList<JsonElement> Section(string corpus, string section)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, corpus)));
        return document.RootElement.GetProperty(section).EnumerateArray().Select(element => element.Clone()).ToArray();
    }

    public static string String(this JsonElement element, string property) => element.GetProperty(property).GetString()!;

    public static bool Bool(this JsonElement element, string property) => element.GetProperty(property).GetBoolean();

    public static string[] Strings(this JsonElement element, string property) =>
        element.GetProperty(property).EnumerateArray().Select(item => item.GetString()!).ToArray();

    /// <summary>
    ///     <paramref name="subject" /> followed by the case's <c>note</c>, as the name it is reported under — a
    ///     corpus case that fails has to say what it was asserting without anyone opening the JSON.
    /// </summary>
    public static string Describe(this JsonElement element, string subject) =>
        element.TryGetProperty("note", out var note) ? $"{subject} — {note.GetString()}" : subject;
}
