using System.Text;
using Loom.Config;
using Tomlyn;

namespace Loom.Packages;

/// <summary>
///     The tokens a publisher has signed in with, one per registry host, kept outside every project so that
///     nothing which is committed, packaged or published can carry one.
/// </summary>
/// <remarks>
///     Keyed by host and port rather than by the index URL, and looked up by the host actually being contacted:
///     a token belongs to the registry that issued it, and there is no spelling of an index that can make one
///     registry's token reach another. <c>LOOM_TOKEN</c> is read first, for a build machine that has no business
///     writing a file to hold it.
///     <para>
///         Unlike a manifest, this file is written only by <c>loom login</c> — nobody keeps comments or a key
///         order in it worth preserving — so it is rewritten whole rather than edited in place, which is the one
///         place in this repository where re-serializing a TOML file is the right thing to do.
///     </para>
/// </remarks>
/// <param name="directory">Where the file is kept; the user's own configuration directory when none is given.</param>
public sealed class RegistryCredentials(string? directory = null)
{
    /// <summary>Supplies the token for whichever registry the command is contacting, in place of the file.</summary>
    public const string EnvironmentVariable = "LOOM_TOKEN";

    public const string FileName = "credentials.toml";

    private const string Table = "registry";

    private readonly string _directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "loom");

    /// <summary>The file the tokens are in, whether or not it exists yet.</summary>
    public string FilePath => Path.Combine(_directory, FileName);

    /// <summary>
    ///     How a registry is named among the credentials: its host, and its port when that is not the scheme's
    ///     own. A registry being developed against on <c>localhost:8080</c> is therefore a different registry from
    ///     the one in production, which is exactly what it is.
    /// </summary>
    public static string HostOf(Uri index) => index.IsDefaultPort ? index.Host.ToLowerInvariant() : $"{index.Host.ToLowerInvariant()}:{index.Port}";

    /// <summary>
    ///     Whether a token may be sent to <paramref name="index" /> at all. A bearer token is a password in a
    ///     header, so cleartext carries it to everyone on the path; the exception is a loopback address, which is
    ///     the registry someone is developing against and never leaves the machine.
    /// </summary>
    public static bool MayCarryToken(Uri index) => index.Scheme == Uri.UriSchemeHttps || index.IsLoopback;

    /// <summary>
    ///     The token to send to <paramref name="index" />, or <see langword="null" /> when there is none to send —
    ///     including when there is one stored but the connection may not carry it.
    /// </summary>
    public string? TokenFor(Uri index)
    {
        if (!MayCarryToken(index))
            return null;

        if (Environment.GetEnvironmentVariable(EnvironmentVariable) is { Length: > 0 } supplied)
            return supplied;

        return Read(out _).GetValueOrDefault(HostOf(index));
    }

    /// <summary>
    ///     Records <paramref name="token" /> as the token for <paramref name="index" />, replacing whatever was
    ///     stored for that host, and answers whether it is now stored.
    /// </summary>
    public bool Store(Uri index, string token, out IReadOnlyList<ConfigDiagnostic> diagnostics)
    {
        var tokens = Read(out diagnostics);
        if (diagnostics.Count > 0)
            return false;

        tokens[HostOf(index)] = token;
        return Write(tokens, out diagnostics);
    }

    /// <summary>
    ///     Every token stored, by host. A file that cannot be read is reported rather than treated as empty:
    ///     overwriting it would throw away every other registry's token to store one.
    /// </summary>
    private Dictionary<string, string> Read(out IReadOnlyList<ConfigDiagnostic> diagnostics)
    {
        diagnostics = [];
        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(FilePath))
            return tokens;

        string text;
        try
        {
            text = File.ReadAllText(FilePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics = [new ConfigDiagnostic($"could not read '{FilePath}': {exception.Message}")];
            return tokens;
        }

        CredentialsDocument? document;
        try
        {
            document = TomlSerializer.Deserialize(text, CredentialsContext.Default.CredentialsDocument);
        }
        catch (TomlException)
        {
            diagnostics = [new ConfigDiagnostic($"'{FilePath}' cannot be read; sign in again to replace it.")];
            return tokens;
        }

        foreach (var entry in document?.Registries ?? [])
        {
            if (entry is { Host: { Length: > 0 } host, Token: { Length: > 0 } token })
                tokens[host] = token;
        }

        return tokens;
    }

    /// <summary>
    ///     Writes every token, through a temporary file so that an interrupted write cannot leave the others lost,
    ///     and readable only by the user who owns it.
    /// </summary>
    /// <remarks>
    ///     The mode is set on Unix, where a file is otherwise created readable by everyone on the machine. Windows
    ///     has no one-call equivalent and needs none here: the directory is under the user's own profile, which
    ///     carries an access-control list saying the same thing.
    /// </remarks>
    private bool Write(Dictionary<string, string> tokens, out IReadOnlyList<ConfigDiagnostic> diagnostics)
    {
        diagnostics = [];
        var builder = new StringBuilder();
        builder.Append("# Written by 'loom login'. Anyone who can read this file can publish as you.\n");
        foreach (var (host, token) in tokens.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            builder.Append($"\n[[{Table}]]\nhost = \"{Escape(host)}\"\ntoken = \"{Escape(token)}\"\n");

        var temporary = FilePath + ".tmp";
        try
        {
            Directory.CreateDirectory(_directory);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            File.WriteAllText(temporary, builder.ToString());
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            File.Move(temporary, FilePath, true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics = [new ConfigDiagnostic($"could not write '{FilePath}': {exception.Message}")];
            return false;
        }
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
