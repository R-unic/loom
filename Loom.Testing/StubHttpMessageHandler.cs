using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Loom.Testing;

/// <summary>
///     A registry that is not there: every request is answered by a function of what was asked, and every request is
///     recorded, so a test can assert on what was sent as well as on what was made of the answer.
/// </summary>
/// <remarks>
///     The synchronous <see cref="Send" /> is the one that has to be overridden. Everything speaking to a registry
///     goes through <c>HttpClient.Send</c> — resolution is synchronous, and blocking on a task is what deadlocks a
///     host holding a synchronization context — and the base implementation of that overload throws rather than
///     falling back to <see cref="SendAsync" />, so a handler overriding only the asynchronous half would answer
///     nothing at all.
/// </remarks>
internal sealed class StubHttpMessageHandler(Func<StubRequest, HttpResponseMessage> answer) : HttpMessageHandler
{
    /// <summary>Every request that reached this handler, in the order it was sent.</summary>
    public List<StubRequest> Requests { get; } = [];

    public StubRequest LastRequest => Requests[^1];

    /// <summary>A handler answering everything the same way, for a test that only cares what one answer becomes.</summary>
    public static StubHttpMessageHandler Answering(HttpStatusCode status, string body = "", string? entityTag = null) =>
        new(_ => Json(status, body, entityTag));

    /// <summary>A handler that never reaches a registry at all, which is the failure no directory on disk can have.</summary>
    public static StubHttpMessageHandler Unreachable(string reason = "the connection timed out") =>
        new(_ => throw new HttpRequestException(reason));

    public static HttpResponseMessage Json(HttpStatusCode status, string body = "", string? entityTag = null)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (entityTag != null)
            response.Headers.ETag = new EntityTagHeaderValue(entityTag);

        return response;
    }

    public static HttpResponseMessage Bytes(HttpStatusCode status, byte[] body) =>
        new(status) { Content = new ByteArrayContent(body) { Headers = { ContentType = new MediaTypeHeaderValue("application/gzip") } } };

    /// <summary>An error envelope in the shape a registry states one, so its own wording is what a test sees.</summary>
    public static HttpResponseMessage Refusal(HttpStatusCode status, string detail) => Json(status, $$"""{"errors":[{"detail":"{{detail}}"}]}""");

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) => Answer(request);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(Answer(request));

    private HttpResponseMessage Answer(HttpRequestMessage request)
    {
        var recorded = StubRequest.Of(request);
        Requests.Add(recorded);
        return answer(recorded);
    }
}

/// <summary>One request as it was sent, read eagerly so it can still be asserted on after the response is disposed.</summary>
internal sealed record StubRequest(HttpMethod Method, Uri Address, string? IfNoneMatch, string? Authorization, string? ContentType, byte[] Body)
{
    public string Path => Address.AbsolutePath;

    public static StubRequest Of(HttpRequestMessage request)
    {
        using var body = new MemoryStream();
        request.Content?.ReadAsStream().CopyTo(body);
        return new StubRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.TryGetValues("If-None-Match", out var tags) ? string.Join(", ", tags) : null,
            request.Headers.Authorization?.ToString(),
            request.Content?.Headers.ContentType?.MediaType,
            body.ToArray()
        );
    }
}
