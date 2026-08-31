using Loom.Config;

namespace Loom.Packages;

/// <summary>
///     Where a browser has to go for a registry to issue a token, or why it cannot go anywhere.
/// </summary>
/// <remarks>
///     A registry deliberately refuses to mint a token for a request carrying one, so that a leaked token cannot
///     be used to grow successors that outlive revoking it. That leaves no way for a command-line tool to obtain a
///     token by itself: a person has to sign in where a browser session can be established, and bring back what
///     they are shown. Every registry that has no sign-in configured — which is the ordinary state of a
///     self-hosted one — says so here, and its own words are what a person is told, since it knows who to ask for
///     a token and this does not.
/// </remarks>
/// <param name="BrowserLocation">Where to send a browser, or <see langword="null" /> when there is nowhere to send one.</param>
/// <param name="Unavailable">Why there is nowhere to send one, in the registry's own words.</param>
public sealed record RegistrySignIn(Uri? BrowserLocation, string? Unavailable)
{
    /// <summary>
    ///     Asks <paramref name="index" /> where a person signs in, or reports that the question could not be put
    ///     to it at all — which is different from a registry that answered and has no sign-in.
    /// </summary>
    /// <remarks>
    ///     The redirect is the answer, so it is deliberately not followed: following it would report on whichever
    ///     identity provider the registry uses instead of on the registry, and a browser is what has to make that
    ///     hop.
    /// </remarks>
    public static RegistrySignIn Begin(string index, out IReadOnlyList<ConfigDiagnostic> diagnostics, HttpMessageHandler? handler = null)
    {
        var client = new RegistryClient(index, handler, followRedirects: false);
        var endpoint = client.Endpoint("v1/auth/github");
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        using var response = client.Send(request, out diagnostics);
        if (response == null)
            return new RegistrySignIn(null, null);

        if (response.Headers.Location is { } location)
            return new RegistrySignIn(location.IsAbsoluteUri ? location : new Uri(endpoint, location), null);

        // a registry that serves its own sign-in page rather than redirecting to somebody else's has still
        // answered the question, and the page it served is where a person signs in
        return response.IsSuccessStatusCode
            ? new RegistrySignIn(endpoint, null)
            : new RegistrySignIn(null, client.Failure(response).Message);
    }
}
