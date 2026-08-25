using System.Text;

namespace Loom.Config;

/// <summary>
///     Writes a dependency into a manifest's <c>[dependencies]</c> table as a text edit rather than by
///     re-serializing the manifest: <c>loom-config.toml</c> is a file its author wrote, and the comments, key order
///     and spacing in it are theirs. Reading a manifest is <see cref="ConfigReader" />'s job; this only ever changes
///     the one line the dependency is written on, or adds one.
/// </summary>
/// <remarks>
///     Nothing here parses TOML in general — it finds a table header and a key line, which is all an edit to one
///     entry needs, and answers <see langword="null" /> for anything it cannot change without touching more than
///     that line. The alternative, reading the document and writing it back out, loses every comment in the file
///     to add a dependency to it.
/// </remarks>
public static class ManifestEditor
{
    private const string DependenciesTable = "dependencies";

    /// <summary>
    ///     <paramref name="manifest" /> with <paramref name="name" /> declared at <paramref name="requirement" />:
    ///     the entry rewritten when <c>[dependencies]</c> already names the package, a line added to the table when
    ///     it does not, and the table itself added when the manifest has none. <see langword="null" /> with the
    ///     <paramref name="diagnostics" /> saying why when the existing entry is not something a one-line edit can
    ///     replace.
    /// </summary>
    public static string? WithDependency(
        string manifest,
        PackageName name,
        VersionRequirement requirement,
        bool isDevelopmentOnly,
        out IReadOnlyList<ConfigDiagnostic> diagnostics
    )
    {
        diagnostics = [];
        var newline = manifest.Contains("\r\n") ? "\r\n" : "\n";
        var value = Value(requirement, isDevelopmentOnly);

        var lines = manifest.Split('\n').ToList();
        var header = FindTable(lines, DependenciesTable);
        if (header < 0)
            return WithTable(manifest, newline, name, value);

        var end = SectionEnd(lines, header);
        var entry = FindEntry(lines, header + 1, end, name);
        if (entry < 0)
        {
            lines.Insert(InsertionPoint(lines, header, end), $"{Key(name)} = {value}" + (newline == "\r\n" ? "\r" : string.Empty));
            return string.Join('\n', lines);
        }

        var rewritten = Rewrite(lines[entry], value);
        if (rewritten == null)
        {
            diagnostics =
            [
                new ConfigDiagnostic(
                    $"the [dependencies] entry for '{name}' is not written as a single line, so it cannot be rewritten; edit {ConfigReader.ConfigFileName} by hand.",
                    entry + 1
                )
            ];

            return null;
        }

        lines[entry] = rewritten;
        return string.Join('\n', lines);
    }

    /// <summary>How a dependency's key is written: quoted only when it has to be, which is when it names a scope.</summary>
    private static string Key(PackageName name) => name.IsScoped ? $"\"{name}\"" : name.ToString();

    /// <summary>
    ///     The value side of the entry. A development-only dependency needs the table form to carry its flag; every
    ///     other one is written as the requirement alone, which is what an author writing it by hand would write.
    /// </summary>
    private static string Value(VersionRequirement requirement, bool isDevelopmentOnly) =>
        isDevelopmentOnly ? $"{{ version = \"{requirement}\", dev = true }}" : $"\"{requirement}\"";

    /// <summary>
    ///     The manifest with a <c>[dependencies]</c> table added at the end, which is the one place a new table can
    ///     go without changing which table anything already written belongs to.
    /// </summary>
    private static string WithTable(string manifest, string newline, PackageName name, string value)
    {
        var builder = new StringBuilder(manifest);
        if (manifest.Length > 0 && !manifest.EndsWith('\n'))
            builder.Append(newline);

        if (manifest.Length > 0 && !manifest.EndsWith(newline + newline))
            builder.Append(newline);

        return builder.Append('[').Append(DependenciesTable).Append(']').Append(newline)
            .Append(Key(name)).Append(" = ").Append(value).Append(newline)
            .ToString();
    }

    /// <summary>
    ///     The line the table <paramref name="table" /> is declared on, or <c>-1</c>. An array-of-tables header and a
    ///     table whose name has more than one part are both something else with a similar spelling, so neither
    ///     matches.
    /// </summary>
    private static int FindTable(List<string> lines, string table)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            var content = Content(lines[index]);
            if (content is not ['[', .., ']'] || content.StartsWith("[["))
                continue;

            if (Unquote(content[1..^1].Trim()) == table)
                return index;
        }

        return -1;
    }

    /// <summary>
    ///     Where the section a header opens stops: the next table header, since every key between the two belongs to
    ///     the table above it.
    /// </summary>
    private static int SectionEnd(List<string> lines, int header)
    {
        for (var index = header + 1; index < lines.Count; index++)
        {
            if (Content(lines[index]).StartsWith('['))
                return index;
        }

        return lines.Count;
    }

    /// <summary>
    ///     The line in a section declaring <paramref name="name" />, or <c>-1</c>. Compared as package names rather
    ///     than as text: <c>Math</c>, <c>math</c> and <c>"math"</c> are one dependency, and adding a second entry
    ///     for it would make the manifest unreadable rather than updated.
    /// </summary>
    private static int FindEntry(List<string> lines, int from, int to, PackageName name)
    {
        for (var index = from; index < to; index++)
        {
            var content = Content(lines[index]);
            if (content.Length == 0 || content.StartsWith('#'))
                continue;

            var assignment = content.IndexOf('=');
            if (assignment < 0)
                continue;

            if (PackageName.TryParse(Unquote(content[..assignment].Trim()), out var key) && key == name)
                return index;
        }

        return -1;
    }

    /// <summary>
    ///     Where a new entry goes: after the last entry in the table. Blank lines and a run of comments at the end of
    ///     a section are left below the insertion, since a comment written just above the next table heads what
    ///     follows it rather than trailing what came before.
    /// </summary>
    private static int InsertionPoint(List<string> lines, int header, int end)
    {
        var point = end;
        while (point > header + 1)
        {
            var content = Content(lines[point - 1]);
            if (content.Length != 0 && !content.StartsWith('#'))
                break;

            point--;
        }

        return point;
    }

    /// <summary>
    ///     The line with its value replaced, keeping the key as written, the indentation and any comment after the
    ///     value. <see langword="null" /> when the value does not end on this line — an unterminated string or inline
    ///     table — since replacing it would take the rest of it with it.
    /// </summary>
    private static string? Rewrite(string line, string value)
    {
        var carriageReturn = line.EndsWith('\r');
        var content = line.TrimEnd('\r');
        var assignment = content.IndexOf('=');
        if (assignment < 0)
            return null;

        var comment = CommentStart(content, assignment + 1);
        if (comment == null)
            return null;

        var trailing = comment.Value < 0 ? string.Empty : "  " + content[comment.Value..].TrimEnd();
        return content[..assignment].TrimEnd() + " = " + value + trailing + (carriageReturn ? "\r" : string.Empty);
    }

    /// <summary>
    ///     Where the comment after a value starts, <c>-1</c> when there is none, or <see langword="null" /> when the
    ///     value is still open at the end of the line and nothing on it can be replaced in isolation.
    /// </summary>
    private static int? CommentStart(string content, int from)
    {
        var quote = '\0';
        var depth = 0;
        for (var index = from; index < content.Length; index++)
        {
            var character = content[index];
            if (quote != '\0')
            {
                if (character == '\\' && quote == '"')
                    index++;
                else if (character == quote)
                    quote = '\0';

                continue;
            }

            switch (character)
            {
                case '"' or '\'':
                    quote = character;
                    break;
                case '{' or '[':
                    depth++;
                    break;
                case '}' or ']':
                    depth--;
                    break;
                case '#':
                    return index;
            }
        }

        return quote == '\0' && depth == 0 ? -1 : null;
    }

    /// <summary>The line without the carriage return a CRLF manifest leaves on it, and without its indentation.</summary>
    private static string Content(string line) => line.TrimEnd('\r').Trim();

    private static string Unquote(string text) =>
        text is ['"', .., '"'] or ['\'', .., '\''] && text.Length >= 2 ? text[1..^1] : text;
}
