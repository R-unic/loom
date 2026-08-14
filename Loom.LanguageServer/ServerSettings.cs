using Microsoft.Extensions.Configuration;

namespace Loom.LanguageServer;

/// <summary>
///     What the user has configured, read from the client's <c>loom</c> settings section. The server asks for
///     the section on initialize and the client re-sends it whenever the user changes something, so a setting
///     read here is the setting as it is now rather than as it was when the server started.
/// </summary>
/// <remarks>
///     Every setting has a default that stands on its own, and an absent or malformed value falls back to it
///     rather than being reported: a server that will not answer because a preference is misspelled is worse
///     than one that answers the ordinary way. No editor extension lives in this repository, so the
///     <c>package.json</c> that declares these settings to the user belongs wherever that extension does -
///     the names here are the contract it has to match.
/// </remarks>
public sealed class ServerSettings(IConfiguration? configuration = null)
{
    /// <summary>The section the client is asked for, and the prefix every key below is read under.</summary>
    public const string Section = "loom";

    /// <summary>Whether a declaration is annotated with how many places refer to it. <c>loom.codeLens.references</c>.</summary>
    public bool CodeLensReferences => Flag("codeLens:references", fallback: true);

    /// <summary>Whether a trait is annotated with how many types implement it. <c>loom.codeLens.implementations</c>.</summary>
    public bool CodeLensImplementations => Flag("codeLens:implementations", fallback: true);

    /// <summary>Whether there is any lens to draw at all, which is what decides if the file is walked for them.</summary>
    public bool CodeLensEnabled => CodeLensReferences || CodeLensImplementations;

    private bool Flag(string key, bool fallback) =>
        configuration?[$"{Section}:{key}"] is { Length: > 0 } value && bool.TryParse(value, out var parsed) ? parsed : fallback;
}
