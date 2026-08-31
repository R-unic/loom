using System.Text.Json;
using Loom.Config;

namespace Loom.Packages;

/// <summary>
///     The HTTP a registry is spoken to over: where its endpoints are, sending a request without throwing, and
///     turning a refusal into a diagnostic. Everything about <em>being</em> an index is
///     <see cref="RemotePackageIndex" />'s; everything about the wire is here.
/// </summary>
/// <remarks>
///     Deliberately synchronous, through <see cref="HttpClient.Send(HttpRequestMessage)" /> rather than by waiting
///     on the asynchronous overload. <see cref="IPackageIndex" /> is synchronous because resolution is, and
///     blocking on a task is the shape that deadlocks the moment a caller with a synchronization context — a
///     language server — resolves through it. There is a genuinely synchronous send, so nothing here has to
///     choose between the two.
///     <para>
///         One <see cref="HttpClient" /> is shared by every index in the process, since a client per index is how
///         a long-lived host exhausts its sockets. A test hands in its own handler instead and gets a client of
///         its own, which is the only reason the constructor takes one.
///     </para>
/// </remarks>
/// <param name="index">The index as the manifest spells it, which is how a diagnostic names it.</param>
/// <param name="followRedirects">
///     Whether a redirect is followed. Off for the sign-in probe, whose whole answer <em>is</em> the redirect: a
///     browser has to be sent where the registry points, and following it here would report on GitHub instead.
/// </param>
internal sealed class RegistryClient(string index, HttpMessageHandler? handler = null, bool followRedirects = true)
{
    /// <summary>Long enough for a slow registry, short enough that a build does not appear to have hung.</summary>
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);

    private static readonly HttpClient _shared = new() { Timeout = _timeout };

    private readonly HttpClient _client = handler != null
        ? new HttpClient(handler) { Timeout = _timeout }
        : followRedirects
            ? _shared
            : new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = _timeout };

    private readonly string _base = index.TrimEnd('/');

    public string Index => index;

    /// <summary>
    ///     The endpoint <paramref name="path" /> names, under whatever path the index itself is served at — a
    ///     registry behind a prefix is as ordinary as one at the root of a host.
    /// </summary>
    public Uri Endpoint(string path) => new($"{_base}/{path}");

    /// <summary>One path segment, escaped, so a name is never read as more of the path than it is.</summary>
    public static string Segment(string value) => Uri.EscapeDataString(value);

    /// <summary>
    ///     The response, or <see langword="null" /> having said why there is none. A refusal is a response and
    ///     comes back as one: only the request never arriving is reported here, since which statuses mean what
    ///     belongs to the caller that knows what it asked for.
    /// </summary>
    public HttpResponseMessage? Send(HttpRequestMessage request, out IReadOnlyList<ConfigDiagnostic> diagnostics)
    {
        diagnostics = [];
        try
        {
            return _client.Send(request, HttpCompletionOption.ResponseHeadersRead);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException)
        {
            diagnostics = [Unreachable(exception.Message)];
            return null;
        }
        catch (OperationCanceledException)
        {
            diagnostics = [Unreachable($"the request timed out after {_timeout.TotalSeconds:0} seconds")];
            return null;
        }
    }

    /// <summary>
    ///     What <paramref name="response" /> refused with, as a diagnostic. The registry's own <c>detail</c> is
    ///     the whole message when it states one: it is written to be read by whoever ran the command, and
    ///     restating it in this side's words would only lose what it knows — which package is squatted, which
    ///     scope belongs to someone else.
    /// </summary>
    public ConfigDiagnostic Failure(HttpResponseMessage response) =>
        new(Detail(response) ?? $"'{index}' answered {(int)response.StatusCode} {response.ReasonPhrase}.");

    /// <summary>The <c>detail</c>s of an error envelope, or null when the body is not one.</summary>
    private static string? Detail(HttpResponseMessage response)
    {
        try
        {
            using var document = JsonDocument.Parse(response.Content.ReadAsStream());
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("errors", out var errors)
                || errors.ValueKind != JsonValueKind.Array)
                return null;

            var details = errors.EnumerateArray()
                .Where(error => error.ValueKind == JsonValueKind.Object)
                .Select(error => error.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String ? detail.GetString() : null)
                .OfType<string>()
                .ToArray();

            return details.Length == 0 ? null : string.Join(" ", details);
        }
        catch (Exception exception) when (exception is JsonException or IOException or HttpRequestException or NotSupportedException)
        {
            return null;
        }
    }

    private ConfigDiagnostic Unreachable(string reason) => new($"could not reach '{index}': {reason}.");
}
