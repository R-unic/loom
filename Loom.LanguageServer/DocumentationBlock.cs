namespace Loom.LanguageServer;

/// <summary>
///     A <c>###</c> doc comment split into the prose about the declaration as a whole and the tagged lines
///     about its individual pieces. Signature help wants one parameter's line on its own, so the tags cannot
///     stay buried in the summary; hover wants all of it, in an order the author did not have to get right.
/// </summary>
public sealed record DocumentationBlock(string Summary, IReadOnlyList<ParameterDocumentation> Parameters, string? Returns)
{
    public static readonly DocumentationBlock Empty = new("", [], null);

    public bool IsEmpty => Summary.Length == 0 && Parameters.Count == 0 && Returns == null;

    public string? ForParameter(string name) => Parameters.FirstOrDefault(parameter => parameter.Name == name)?.Text;

    /// <summary>
    ///     Reads <c>@param &lt;name&gt; &lt;text&gt;</c> and <c>@returns &lt;text&gt;</c> out of a raw doc comment.
    ///     A line that opens no tag continues the one before it, so a description may wrap; everything above
    ///     the first tag is the summary.
    /// </summary>
    public static DocumentationBlock Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Empty;

        var summary = new List<string>();
        var parameters = new List<(string Name, List<string> Text)>();
        var returns = new List<string>();
        var current = summary;

        foreach (var line in raw.Split('\n'))
        {
            var tag = line.TrimStart();
            if (SplitTag(tag, "@param") is { } parameter)
            {
                parameters.Add((parameter.Name, parameter.Text.Length == 0 ? [] : [parameter.Text]));
                current = parameters[^1].Text;
            }
            else if (ReturnsTag(tag) is { } returned)
            {
                current = returns;
                if (returned.Length > 0)
                    current.Add(returned);
            }
            else
            {
                current.Add(line);
            }
        }

        return new DocumentationBlock(
            string.Join('\n', summary).Trim(),
            parameters.ConvertAll(parameter => new ParameterDocumentation(parameter.Name, Join(parameter.Text))).AsReadOnly(),
            returns.Count == 0 ? null : Join(returns)
        );
    }

    /// <summary>The whole block as markdown, tags rendered as a list under the prose.</summary>
    public string ToMarkdown()
    {
        var sections = new List<string>();
        if (Summary.Length > 0)
            sections.Add(Summary);

        if (Parameters.Count > 0)
            sections.Add(string.Join('\n', Parameters.Select(parameter => $"- `{parameter.Name}` — {parameter.Text}")));

        if (Returns != null)
            sections.Add($"**Returns** — {Returns}");

        return string.Join("\n\n", sections);
    }

    /// <summary>Splits <c>@param name the rest</c> into its name and text, or null when the line opens no such tag.</summary>
    private static (string Name, string Text)? SplitTag(string line, string tag)
    {
        if (!line.StartsWith(tag, StringComparison.Ordinal))
            return null;

        var rest = line[tag.Length..];
        if (rest.Length == 0 || !char.IsWhiteSpace(rest[0]))
            return null;

        rest = rest.TrimStart();
        var end = rest.IndexOfAny([' ', '\t']);
        return rest.Length == 0 ? null : end < 0 ? (rest, "") : (rest[..end], rest[(end + 1)..].Trim());
    }

    /// <summary>The text after a <c>@returns</c> (or <c>@return</c>) tag, or null when the line opens neither.</summary>
    private static string? ReturnsTag(string line)
    {
        foreach (var tag in new[] { "@returns", "@return" })
        {
            if (!line.StartsWith(tag, StringComparison.Ordinal))
                continue;

            var rest = line[tag.Length..];
            if (rest.Length == 0 || char.IsWhiteSpace(rest[0]))
                return rest.Trim();
        }

        return null;
    }

    private static string Join(List<string> lines) => string.Join('\n', lines).Trim();
}

public sealed record ParameterDocumentation(string Name, string Text);
