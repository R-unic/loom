using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Loom.Config;

namespace Loom.Packages;

/// <summary>
///     An index that is a registry, spoken to over HTTP: what it publishes comes from its index endpoint, a
///     version is installed by fetching and unpacking a tarball, and publishing is an upload.
/// </summary>
/// <remarks>
///     The same shape <see cref="LocalPackageIndex" /> answers in, and the two differences a network makes are
///     both answered here rather than anywhere downstream. An index that cannot say what it publishes reports it
///     instead of answering empty, since empty means <em>no such package</em> and nothing else. And nothing that
///     arrives is trusted: the order is sorted on receipt, the bytes are checked against the checksum the index
///     states before they are unpacked, and the archive is read as though it were written to escape.
///     <para>
///         What is published is memoized for the life of the index, not written anywhere: resolution asks about
///         the same package once a round for as many rounds as it takes to settle, which is a request each over a
///         network, while a stale cache of what is published is exactly the bug an index must not have. One index
///         is one resolution, so the memo is one consistent answer rather than a guess at how long one keeps.
///         <see cref="Publish" /> is the one thing that makes its own memo wrong and is the one thing that clears
///         it — keeping the entity tag, so the request that follows is a revalidation rather than a refetch.
///     </para>
/// </remarks>
/// <param name="index">The registry's base URL, as the manifest spells it.</param>
/// <param name="source">What a lock file records as where a version came from; <see langword="null" /> records none.</param>
public sealed class RemotePackageIndex(
    string index,
    string? source = null,
    HttpMessageHandler? handler = null,
    RegistryCredentials? credentials = null
) : IPackageIndex
{
    /// <summary>
    ///     A ceiling on a download, well above what any registry accepts. The same kind of guard as the one on
    ///     unpacking, for the same reason: nothing that arrives over a network states its own size honestly.
    /// </summary>
    private const int MaximumDownloadBytes = 32 * 1024 * 1024;

    private readonly RegistryClient _client = new(index, handler);

    private readonly RegistryCredentials _credentials = credentials ?? new RegistryCredentials();

    private readonly Dictionary<PackageName, Publication> _memo = [];

    private readonly Uri _address = new(index);

    public string Description => index;

    /// <inheritdoc />
    /// <remarks>
    ///     One request answers everything about a package, because the endpoint states each version's
    ///     dependencies alongside it — so resolution walks a graph without fetching a single package, which is
    ///     what makes resolving over a network cost one request per package rather than one per version.
    /// </remarks>
    public IReadOnlyList<PublishedPackage> Publications(PackageName package, out IReadOnlyList<ConfigDiagnostic> diagnostics)
    {
        diagnostics = [];
        _memo.TryGetValue(package, out var known);
        if (known is { Fresh: true })
            return known.Publications;

        using var request = new HttpRequestMessage(HttpMethod.Get, _client.Endpoint($"v1/index/{Route(package)}"));
        if (known?.ETag is { Length: > 0 } tag)
            request.Headers.TryAddWithoutValidation("If-None-Match", tag);

        using var response = _client.Send(request, out diagnostics);
        if (response == null)
            return [];

        switch (response.StatusCode)
        {
            // the memo was cleared by a publish and nothing else about the package has changed since
            case HttpStatusCode.NotModified when known != null:
                known.Fresh = true;
                return known.Publications;

            // the one answer that is a resolution failure rather than an index failure, and the only one that
            // may come back empty with nothing said about it
            case HttpStatusCode.NotFound:
                _memo[package] = new Publication();
                return [];

            case HttpStatusCode.OK:
                var published = IndexDocument.Read(response.Content.ReadAsStream(), package, index, source, out diagnostics);
                if (published == null)
                    return [];

                _memo[package] = new Publication { Publications = published, ETag = response.Headers.ETag?.ToString() };
                return published;

            default:
                diagnostics = [_client.Failure(response)];
                return [];
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Nothing here asks whether the version was yanked. A yank withdraws a version from being chosen, which
    ///     already happened wherever this one was chosen; a lock pinning a yanked version installs it exactly as
    ///     it did before, or a project would stop building because of a decision taken after it was made.
    /// </remarks>
    public bool Install(PublishedPackage package, string directory, out IReadOnlyList<ConfigDiagnostic> diagnostics)
    {
        // a directory on disk is its own evidence, but bytes off a network are only as good as what they are
        // measured against - so a version an index states no checksum for is not installed rather than installed
        // unverified, which would be the one failure a checksum exists to catch
        if (package.Checksum is not { Length: > 0 } stated)
        {
            diagnostics = [new ConfigDiagnostic($"'{index}' states no checksum for '{package}', so what it serves cannot be verified.")];
            return false;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            _client.Endpoint($"v1/packages/{Route(package.Name)}/{RegistryClient.Segment(package.Version.ToString())}/download")
        );

        using var response = _client.Send(request, out diagnostics);
        if (response == null)
            return false;

        if (!response.IsSuccessStatusCode)
        {
            diagnostics = [_client.Failure(response)];
            return false;
        }

        var content = Download(response, out diagnostics);
        if (content == null)
            return false;

        var computed = PackageChecksum.Of(content);
        if (!PackageChecksum.Same(stated, computed))
        {
            diagnostics =
            [
                new ConfigDiagnostic(
                    $"'{package}' does not match the checksum '{index}' states for it ({stated} expected, {computed} served), so it was not installed."
                )
            ];

            return false;
        }

        return PackageArchive.Extract(content, directory, out diagnostics);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     No metadata is sent alongside the archive. The registry reads the manifest out of it, the way
    ///     <see cref="LocalPackageIndex.Publications" /> reads the manifest of a version directory — an index
    ///     deriving what a version is from anywhere but the version itself would publish something the package
    ///     does not say about itself.
    /// </remarks>
    public bool Publish(PackagePayload payload, out IReadOnlyList<ConfigDiagnostic> diagnostics)
    {
        var token = Token(out diagnostics);
        if (token == null)
            return false;

        var content = PackageArchive.Create(payload, out diagnostics);
        if (content == null)
            return false;

        using var request = new HttpRequestMessage(HttpMethod.Post, _client.Endpoint("v1/publish"));
        request.Content = new ByteArrayContent(content) { Headers = { ContentType = new MediaTypeHeaderValue("application/gzip") } };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = _client.Send(request, out diagnostics);
        if (response == null)
            return false;

        if (!response.IsSuccessStatusCode)
        {
            diagnostics = [_client.Failure(response)];
            return false;
        }

        // what this asked to be published is now published, so what the memo says about the package is known to
        // be a version out of date; the entity tag is kept, so asking again costs a revalidation
        if (_memo.TryGetValue(payload.Name, out var known))
            known.Fresh = false;

        var stated = Property(response, "checksum");
        var computed = PackageChecksum.Of(content);
        if (stated == null || PackageChecksum.Same(stated, computed))
            return true;

        // the version is published either way - this is not a failure to publish but a failure to agree about
        // what was published, and a publisher who is not told would go on believing it shipped what it built
        diagnostics =
        [
            new ConfigDiagnostic(
                $"'{index}' accepted '{payload}' but reports it as {stated} rather than the {computed} that was sent; check what is published before depending on it."
            )
        ];

        return false;
    }

    /// <summary>
    ///     The token to publish with, or <see langword="null" /> having said why there is none to send. Read here
    ///     rather than when the index is opened: resolving and installing are unauthenticated, so a build never
    ///     opens the file the tokens are in.
    /// </summary>
    private string? Token(out IReadOnlyList<ConfigDiagnostic> diagnostics)
    {
        diagnostics = [];
        if (!RegistryCredentials.MayCarryToken(_address))
        {
            diagnostics = [new ConfigDiagnostic($"'{index}' is not served over https, so a token cannot be sent to it without publishing the token as well.")];
            return null;
        }

        if (_credentials.TokenFor(_address) is { Length: > 0 } token)
            return token;

        diagnostics =
        [
            new ConfigDiagnostic(
                $"there is no token for '{RegistryCredentials.HostOf(_address)}'; run 'loom login' to sign in,"
                + $" or set {RegistryCredentials.EnvironmentVariable}."
            )
        ];

        return null;
    }

    /// <summary>The body, up to what a package may be; longer than that is not read at all.</summary>
    private byte[]? Download(HttpResponseMessage response, out IReadOnlyList<ConfigDiagnostic> diagnostics)
    {
        diagnostics = [];
        try
        {
            using var body = response.Content.ReadAsStream();
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            while (true)
            {
                var read = body.Read(chunk, 0, chunk.Length);
                if (read == 0)
                    return buffer.ToArray();

                if (buffer.Length + read > MaximumDownloadBytes)
                {
                    diagnostics = [new ConfigDiagnostic($"'{index}' served more than {MaximumDownloadBytes / (1024 * 1024)} MB for one package.")];
                    return null;
                }

                buffer.Write(chunk, 0, read);
            }
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException)
        {
            diagnostics = [new ConfigDiagnostic($"could not read what '{index}' served: {exception.Message}")];
            return null;
        }
    }

    /// <summary>One string off a response body, or null when the body does not state it.</summary>
    private static string? Property(HttpResponseMessage response, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(response.Content.ReadAsStream());
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or HttpRequestException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>A package as the path of an endpoint about it: a scope is a segment of its own, not part of a name.</summary>
    private static string Route(PackageName package) =>
        package.Scope == null
            ? RegistryClient.Segment(package.Name)
            : $"{RegistryClient.Segment(package.Scope)}/{RegistryClient.Segment(package.Name)}";

    /// <summary>What is published about one package, and what the registry was told to ask about it again with.</summary>
    private sealed class Publication
    {
        public IReadOnlyList<PublishedPackage> Publications { get; init; } = [];

        public string? ETag { get; init; }

        /// <summary>Whether the memo may still be answered from, which only a publish of this package clears.</summary>
        public bool Fresh { get; set; } = true;
    }
}
