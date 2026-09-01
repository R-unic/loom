using System.Net;
using Loom.Packages;

namespace Loom.Testing.Packages;

/// <summary>
///     Where a browser has to go for a registry to issue a token. A registry refuses to mint one for a request that
///     already carries one, so no command-line tool can obtain a token by itself — the most it can do is find out
///     where a person signs in, and say so when there is nowhere to send them.
/// </summary>
[Collection("Assembly")]
public class RegistrySignInTest
{
    private const string Registry = "https://registry.test";

    /// <remarks>
    ///     The redirect <em>is</em> the answer, so it is deliberately not followed: following it would report on
    ///     whichever identity provider the registry uses instead of on the registry, and a browser is what has to
    ///     make that hop.
    /// </remarks>
    [Fact]
    public void Begin_AnswersWhereTheRegistryRedirectsTo()
    {
        var handler = new StubHttpMessageHandler(_ => Redirect("https://github.com/login/oauth/authorize?client_id=loom"));

        var signIn = RegistrySignIn.Begin(Registry, out var diagnostics, handler);

        Assert.Empty(diagnostics);
        Assert.Null(signIn.Unavailable);
        Assert.Equal("https://github.com/login/oauth/authorize?client_id=loom", signIn.BrowserLocation?.ToString());
        Assert.Equal("/v1/auth/github", handler.Requests.Single().Path);
    }

    [Fact]
    public void Begin_ResolvesARelativeRedirect_AgainstTheEndpointItAsked()
    {
        var signIn = RegistrySignIn.Begin(Registry, out _, new StubHttpMessageHandler(_ => Redirect("/auth/start?next=publish")));

        Assert.Equal("https://registry.test/auth/start?next=publish", signIn.BrowserLocation?.ToString());
    }

    /// <remarks>A registry serving its own sign-in page rather than redirecting has still answered the question.</remarks>
    [Fact]
    public void Begin_AnswersTheEndpointItself_WhenTheRegistryServesTheSignInPage()
    {
        var signIn = RegistrySignIn.Begin(Registry, out _, StubHttpMessageHandler.Answering(HttpStatusCode.OK, "<html>sign in</html>"));

        Assert.Equal("https://registry.test/v1/auth/github", signIn.BrowserLocation?.ToString());
    }

    /// <remarks>
    ///     The ordinary state of a self-hosted registry, and not a failure: a token issued some other way is stored by
    ///     exactly the same paste. The registry knows who to ask for one and this side does not, so its own words are
    ///     what a person is told.
    /// </remarks>
    [Fact]
    public void Begin_ReportsARegistryWithNoSignIn_InItsOwnWords()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Refusal(HttpStatusCode.ServiceUnavailable, "this registry issues tokens with 'loomreg token'"));

        var signIn = RegistrySignIn.Begin(Registry, out var diagnostics, handler);

        Assert.Empty(diagnostics);
        Assert.Null(signIn.BrowserLocation);
        Assert.Equal("this registry issues tokens with 'loomreg token'", signIn.Unavailable);
    }

    /// <remarks>
    ///     Different again from a registry that answered and has no sign-in — nothing was asked, so nothing was said
    ///     about whether one exists.
    /// </remarks>
    [Fact]
    public void Begin_ReportsARegistryThatCouldNotBeAsked()
    {
        var signIn = RegistrySignIn.Begin(Registry, out var diagnostics, StubHttpMessageHandler.Unreachable());

        Assert.Contains("could not reach 'https://registry.test'", Assert.Single(diagnostics).Message);
        Assert.Null(signIn.BrowserLocation);
        Assert.Null(signIn.Unavailable);
    }

    private static HttpResponseMessage Redirect(string location) =>
        new(HttpStatusCode.Found) { Headers = { Location = new Uri(location, UriKind.RelativeOrAbsolute) } };
}
