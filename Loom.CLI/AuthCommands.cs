using System.Diagnostics;
using System.Text;
using Loom.Core.Diagnostics;
using Loom.Core.Pipeline;
using Loom.Packages;

namespace Loom.CLI;

/// <summary>
///     <c>login</c>: the verb that gives this machine a token for a registry, so that <c>publish</c> has one to
///     send.
/// </summary>
/// <remarks>
///     A registry deliberately refuses to mint a token for a request that already carries one, so that a leaked
///     token cannot be used to grow successors outliving its revocation. There is therefore no flow this can drive
///     to completion by itself: what it can do is find out where a person signs in, send a browser there, and take
///     what they are brought back. A registry with no sign-in configured — the ordinary state of a self-hosted one
///     — is not a failure here, since a token from <c>loomreg token</c> is stored by exactly the same paste.
/// </remarks>
internal static class AuthCommands
{
    public static int Login(LoginOptions options)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var index = Registry(options);
        if (index == null)
            return 1;

        if (!Uri.TryCreate(index, UriKind.Absolute, out var address) || address.Scheme is not ("http" or "https"))
        {
            Log.Fatal($"'{index}' is not a registry to sign in to; a registry is named by an http or https URL.");
            return 1;
        }

        // said before a token is asked for rather than after it is pasted: there is no point taking a token that
        // this would then refuse to send
        if (!RegistryCredentials.MayCarryToken(address))
        {
            Log.Fatal($"'{index}' is not served over https, so a token sent to it would be readable by everyone on the way.");
            return 1;
        }

        var token = options.Token == null ? Prompted(index) : Supplied(options.Token);
        if (string.IsNullOrEmpty(token))
        {
            Log.Fatal("no token was given, so nothing was stored.");
            return 1;
        }

        var credentials = new RegistryCredentials();
        if (!credentials.Store(address, token, out var diagnostics))
        {
            Projects.Report(diagnostics);
            return 1;
        }

        Log.Info(
            $"Signed in to {Colors.Bold}{Colors.White}{RegistryCredentials.HostOf(address)}{Colors.Reset}"
            + $" {Colors.Dim}(stored in {credentials.FilePath}){Colors.Reset}."
        );

        return 0;
    }

    /// <summary>
    ///     The registry being signed in to: the one named, or the one the project resolves from, which is where a
    ///     publish from that project would go.
    /// </summary>
    private static string? Registry(LoginOptions options)
    {
        if (options.Registry is { Length: > 0 } named)
            return named.Contains("://", StringComparison.Ordinal) ? named : $"https://{named}";

        if (!Projects.TryLocate(options.Directory, out var config))
            return null;

        if (config.Registry is { Index.Length: > 0 } registry)
            return registry.Index;

        Log.Fatal("the project names no [registry] index to sign in to; name the registry instead: loom login <url>.");
        return null;
    }

    /// <summary>The token a script supplied, read off standard input when it is written <c>-</c>.</summary>
    private static string Supplied(string token)
    {
        if (token == "-")
            return Console.In.ReadToEnd().Trim();

        Log.Info($"{Colors.Dim}A token written on the command line is kept in your shell's history; '--token -' reads it from standard input.{Colors.Reset}");
        return token;
    }

    /// <summary>
    ///     Asks the registry where a person signs in, sends a browser there, and reads back what they are shown.
    /// </summary>
    /// <remarks>
    ///     Nothing here is fatal on its own. A registry that has no sign-in, or that cannot be reached to be asked,
    ///     still leaves a token that was issued some other way perfectly storable — and storing one is the whole
    ///     of what this verb does.
    /// </remarks>
    private static string? Prompted(string index)
    {
        var signIn = RegistrySignIn.Begin(index, out var diagnostics);
        foreach (var diagnostic in diagnostics)
            Log.Info($"{Colors.Yellow}{diagnostic}{Colors.Reset}");

        if (signIn.Unavailable is { Length: > 0 } unavailable)
            Log.Info($"{Colors.Yellow}{unavailable}{Colors.Reset}");

        if (signIn.BrowserLocation is { } location)
            Open(location);

        return Prompt.Secret("Paste the token:");
    }

    /// <summary>
    ///     Opens the registry's sign-in page. The address is printed before it is opened, since it is the
    ///     registry's answer rather than this side's — whoever is signing in should see where they are being sent,
    ///     and has the address to open by hand if nothing here can open one for them.
    /// </summary>
    private static void Open(Uri location)
    {
        Log.Info($"Sign in at {Colors.Cyan}{location}{Colors.Reset}");
        try
        {
            Process.Start(new ProcessStartInfo(location.ToString()) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is SystemException)
        {
            Log.Info($"{Colors.Dim}Could not open a browser here ({exception.Message}); open the address above.{Colors.Reset}");
        }
    }
}
