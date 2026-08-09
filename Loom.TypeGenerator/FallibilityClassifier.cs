using System.Text.RegularExpressions;
using Loom.TypeGenerator.ApiTypes;

namespace Loom.TypeGenerator;

/// <summary>
///     Decides which Roblox API methods return <c>Result&lt;T, RobloxError&gt;</c> instead of raising.
/// </summary>
/// <remarks>
///     <para>
///         The seed is the dump's own <c>Yields</c>/<c>CanYield</c> tags, which cover the network and
///         web-service surface - the failure a type system cannot exclude. Roblox publishes nothing
///         that says "throws", so the seed is a proxy, corrected by
///         <c>Data/fallible.toml</c>: additions for documented throwers carrying neither tag, and
///         subtractions for tagged methods that cannot actually fail from Loom.
///     </para>
///     <para>
///         The direction of a mistake is asymmetric. A method wrongly marked fallible costs an xpcall
///         frame and an unnecessary <c>?</c>; a method wrongly marked infallible restores the bug this
///         exists to fix. So an unknown method is fallible, and subtractions carry the burden of proof.
///     </para>
/// </remarks>
internal sealed partial class FallibilityClassifier
{
    private const string DataFileName = "fallible.toml";

    private readonly HashSet<string> _additions;
    private readonly HashSet<string> _subtractions;

    public FallibilityClassifier(string? dataFilePath = null)
    {
        var path = dataFilePath ?? LocateDataFile();
        if (path == null || !File.Exists(path))
        {
            _additions = [];
            _subtractions = [];
            Log.Info($"no {DataFileName} found; classifying from the dump's tags alone");
            return;
        }

        var text = File.ReadAllText(path);
        _additions = ReadSection(text, "additions");
        _subtractions = ReadSection(text, "subtractions");
        Log.Info($"read {_additions.Count} additions and {_subtractions.Count} subtractions from {DataFileName}");
    }

    public bool IsFallible(Class rbxClass, Function function)
    {
        var key = $"{rbxClass.Name}:{function.Name}";
        if (_subtractions.Contains(key))
            return false;

        return _additions.Contains(key) || ClassUtility.HasTag(function, "Yields") || ClassUtility.HasTag(function, "CanYield");
    }

    /// <summary>
    ///     Reads one <c>methods = [ … ]</c> array. Deliberately not a TOML parse: the file is written
    ///     for this one reader, and taking a dependency to read two string arrays is not worth it.
    /// </summary>
    private static HashSet<string> ReadSection(string text, string section)
    {
        var start = text.IndexOf($"[{section}]", StringComparison.Ordinal);
        if (start < 0)
            return [];

        var arrayStart = text.IndexOf('[', text.IndexOf("methods", start, StringComparison.Ordinal));
        var arrayEnd = text.IndexOf(']', arrayStart);
        if (arrayStart < 0 || arrayEnd < 0)
            return [];

        // Comment lines are dropped first: the reasons recorded alongside each entry quote the docs,
        // and a quoted sentence would otherwise be read as a member name.
        var body = string.Join(
            '\n',
            text[arrayStart..arrayEnd].Split('\n').Where(line => !line.TrimStart().StartsWith('#'))
        );

        return QuotedEntry()
            .Matches(body)
            .Select(match => match.Groups[1].Value)
            .Where(entry => entry.Contains(':', StringComparison.Ordinal) && !entry.Contains(' ', StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string? LocateDataFile()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "Data", DataFileName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    [GeneratedRegex("\"([^\"]+)\"")]
    private static partial Regex QuotedEntry();
}
