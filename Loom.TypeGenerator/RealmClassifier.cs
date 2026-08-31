using System.Text.RegularExpressions;
using Loom.TypeGenerator.ApiTypes;

namespace Loom.TypeGenerator;

/// <summary>
///     Decides which classes and members of the Roblox API only work from one realm at the engine level, so
///     <see cref="Generators.ClassGenerator" /> can write the same <c>[server]</c>/<c>[client]</c> attribute
///     onto them that a Loom declaration already carries for the resolver's import-narrowing check - the
///     type checker's realm-restriction check reads it the same way regardless of which side wrote it.
/// </summary>
/// <remarks>
///     <para>
///         Roblox publishes nothing that says "client-only" or "server-only" any more than it publishes
///         "throws" (see <see cref="FallibilityClassifier" />), so there is no dump tag to seed this from -
///         it is read entirely from <c>Data/realm.toml</c>, hand-written against creator docs.
///     </para>
///     <para>
///         The direction of a mistake is asymmetric, the same way it is for fallibility: a member wrongly
///         marked restricted costs an override at the call site that trips it; one wrongly left unrestricted
///         restores the exact runtime surprise this exists to catch. An unlisted member is still
///         unrestricted, though, rather than the other way around - unlike fallibility's dump tags, there is
///         no seed here to default away from, and treating the whole API as restricted would make every
///         ordinary call site pay for the handful that actually are.
///     </para>
/// </remarks>
internal sealed partial class RealmClassifier
{
    private const string DataFileName = "realm.toml";

    private readonly HashSet<string> _serverClasses;
    private readonly HashSet<string> _serverMembers;
    private readonly HashSet<string> _clientClasses;
    private readonly HashSet<string> _clientMembers;

    public RealmClassifier(string? dataFilePath = null)
    {
        var path = dataFilePath ?? LocateDataFile();
        if (path == null || !File.Exists(path))
        {
            _serverClasses = [];
            _serverMembers = [];
            _clientClasses = [];
            _clientMembers = [];
            Log.Info($"no {DataFileName} found; classifying no Roblox API surface as realm-restricted");
            return;
        }

        var text = File.ReadAllText(path);
        _serverClasses = ReadArray(text, "server_classes", "classes");
        _serverMembers = ReadArray(text, "server_members", "members");
        _clientClasses = ReadArray(text, "client_classes", "classes");
        _clientMembers = ReadArray(text, "client_members", "members");
        Log.Info(
            $"read {_serverClasses.Count + _clientClasses.Count} restricted classes and "
            + $"{_serverMembers.Count + _clientMembers.Count} restricted members from {DataFileName}"
        );
    }

    /// <summary>The realm attribute to write on every member of <paramref name="rbxClass" />, or null when the class carries no restriction of its own.</summary>
    public string? ClassAttribute(Class rbxClass)
    {
        if (_serverClasses.Contains(rbxClass.Name))
            return "server";

        return _clientClasses.Contains(rbxClass.Name) ? "client" : null;
    }

    /// <summary>The realm attribute to write on this one member, or null when nothing singles it out beyond whatever <see cref="ClassAttribute" /> already says.</summary>
    public string? MemberAttribute(Class rbxClass, MemberBase member)
    {
        var key = $"{rbxClass.Name}:{member.Name}";
        if (_serverMembers.Contains(key))
            return "server";

        return _clientMembers.Contains(key) ? "client" : null;
    }

    /// <summary>
    ///     Reads one <c>&lt;arrayKeyword&gt; = [ … ]</c> array out of a <c>[section]</c> table. Deliberately
    ///     not a TOML parse, the same way <see cref="FallibilityClassifier" />'s reader is not one: the file
    ///     is written for this one reader, and taking a dependency to read four string arrays is not worth
    ///     it.
    /// </summary>
    /// <remarks>
    ///     Both the section header and the array keyword are searched for as whole lines
    ///     (<c>"\n[section]"</c>, <c>"\n{arrayKeyword} ="</c>), never as a bare substring:
    ///     <c>server_classes</c> and <c>client_classes</c> both hold a <c>classes = [ … ]</c> array, so a
    ///     bare search for "classes" would find the section header itself instead of the array beneath it -
    ///     and a doc-link comment is free to mention another section by name
    ///     (<c>"...the server's half is in [client_members] above"</c>), which a bare search for
    ///     "[client_members]" would find before the real header it is talking about.
    ///     <para>
    ///         Comments are stripped a whole line at a time, and the array's closing <c>]</c> is found only
    ///         after that - a comment quoting a section name (as above) contains a <c>]</c> of its own,
    ///         which finding the array's end before stripping comments would mistake for the array's.
    ///     </para>
    /// </remarks>
    private static HashSet<string> ReadArray(string text, string section, string arrayKeyword)
    {
        var start = text.IndexOf($"\n[{section}]", StringComparison.Ordinal);
        if (start < 0)
            return [];

        // Bounded by the next section header (or end of file), so a keyword mentioned only in a later
        // section's own array is never mistaken for this section's.
        var nextSection = text.IndexOf("\n[", start + 1, StringComparison.Ordinal);
        var sectionEnd = nextSection < 0 ? text.Length : nextSection;

        var assignment = text.IndexOf($"\n{arrayKeyword} =", start, StringComparison.Ordinal);
        if (assignment < 0 || assignment >= sectionEnd)
            return [];

        var arrayStart = text.IndexOf('[', assignment);
        if (arrayStart < 0 || arrayStart >= sectionEnd)
            return [];

        var withoutComments = string.Join(
            '\n',
            text[arrayStart..sectionEnd].Split('\n').Select(line => line.Contains('#') ? line[..line.IndexOf('#')] : line)
        );

        var arrayEnd = withoutComments.IndexOf(']');
        if (arrayEnd < 0)
            return [];

        return QuotedEntry()
            .Matches(withoutComments[..arrayEnd])
            .Select(match => match.Groups[1].Value)
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
