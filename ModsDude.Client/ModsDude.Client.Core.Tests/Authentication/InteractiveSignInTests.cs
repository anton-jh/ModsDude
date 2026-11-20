using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Authentication;
using System.Net;
using System.Web;

namespace ModsDude.Client.Core.Tests.Authentication;

public class InteractiveSignInTests
{
    private static readonly OidcClientOptions _options = new(new Uri("https://server.test/"), "client");


    [Fact]
    public void The_authorize_address_asks_for_a_code_with_pkce_and_always_prompts()
    {
        var url = InteractiveSignIn.AuthorizeUrl(_options, new Uri("http://localhost:5000/callback/"), "state-1", "challenge-1", SignInPrompt.SignIn);
        var query = HttpUtility.ParseQueryString(url.Query);

        Assert.Equal("https://server.test/connect/authorize", url.GetLeftPart(UriPartial.Path));
        Assert.Equal("client", query["client_id"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("http://localhost:5000/callback/", query["redirect_uri"]);
        Assert.Equal(OidcClientOptions.Scopes, query["scope"]);
        Assert.Equal("state-1", query["state"]);
        Assert.Equal("challenge-1", query["code_challenge"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("login", query["prompt"]);
    }

    [Fact]
    public void Creating_an_account_prompts_for_the_sign_up_page()
    {
        var url = InteractiveSignIn.AuthorizeUrl(_options, new Uri("http://localhost:5000/callback/"), "s", "c", SignInPrompt.CreateAccount);
        var query = HttpUtility.ParseQueryString(url.Query);

        Assert.Equal("create", query["prompt"]);
    }

    [Fact]
    public async Task The_code_from_the_browser_is_exchanged_with_the_verifier_behind_the_challenge()
    {
        var tokens = new RecordingTokenEndpoint();
        var browser = new RedirectingBrowser(query => $"code=the-code&state={query["state"]}");

        var result = await SignIn(browser, tokens).SignInAsync(SignInPrompt.SignIn, CancellationToken.None);

        Assert.Same(tokens.Issued, result);
        Assert.Equal("the-code", tokens.Code);
        Assert.Equal(browser.Query!["code_challenge"], Pkce.ChallengeFor(tokens.Verifier!));
        Assert.Equal(browser.Query["redirect_uri"], tokens.RedirectUri?.ToString());
    }

    [Fact]
    public async Task A_sign_in_turned_down_on_the_page_gives_nothing()
    {
        var tokens = new RecordingTokenEndpoint();
        var browser = new RedirectingBrowser(query => $"error=access_denied&state={query["state"]}");

        var result = await SignIn(browser, tokens).SignInAsync(SignInPrompt.SignIn, CancellationToken.None);

        Assert.Null(result);
        Assert.Null(tokens.Code);
    }

    [Fact]
    public async Task A_callback_with_another_state_is_ignored()
    {
        var tokens = new RecordingTokenEndpoint();
        var browser = new RedirectingBrowser(
            _ => "code=forged&state=somebody-else",
            query => $"code=the-code&state={query["state"]}");

        await SignIn(browser, tokens).SignInAsync(SignInPrompt.SignIn, CancellationToken.None);

        Assert.Equal("the-code", tokens.Code);
        Assert.Equal(HttpStatusCode.BadRequest, browser.Statuses[0]);
    }

    [Fact]
    public async Task Cancelling_while_the_browser_is_open_ends_the_wait()
    {
        using var cancellation = new CancellationTokenSource();
        var browser = new CancellingBrowser(cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => SignIn(browser, new RecordingTokenEndpoint()).SignInAsync(SignInPrompt.SignIn, cancellation.Token));
    }


    private static InteractiveSignIn SignIn(IBrowserLauncher browser, ITokenEndpointClient tokens)
        => new(_options, browser, tokens, NullLogger<InteractiveSignIn>.Instance);


    /// <summary>Plays the server: sends the loopback listener the callbacks a sign-in page would.</summary>
    private sealed class RedirectingBrowser(params Func<System.Collections.Specialized.NameValueCollection, string>[] callbacks) : IBrowserLauncher
    {
        public System.Collections.Specialized.NameValueCollection? Query { get; private set; }
        public List<HttpStatusCode> Statuses { get; } = [];

        public void Open(Uri address)
        {
            Query = HttpUtility.ParseQueryString(address.Query);
            var redirect = Query["redirect_uri"]!;

            _ = Task.Run(async () =>
            {
                using var client = new HttpClient();

                foreach (var callback in callbacks)
                {
                    using var response = await client.GetAsync($"{redirect}?{callback(Query)}");
                    Statuses.Add(response.StatusCode);
                }
            });
        }
    }

    private sealed class CancellingBrowser(CancellationTokenSource cancellation) : IBrowserLauncher
    {
        public void Open(Uri address) => cancellation.CancelAfter(TimeSpan.FromMilliseconds(50));
    }

    private sealed class RecordingTokenEndpoint : ITokenEndpointClient
    {
        public TokenSet Issued { get; } = TestTokens.Set("access", DateTimeOffset.UnixEpoch, "refresh");
        public string? Code { get; private set; }
        public string? Verifier { get; private set; }
        public Uri? RedirectUri { get; private set; }

        public Task<TokenSet> ExchangeCodeAsync(string code, string codeVerifier, Uri redirectUri, CancellationToken cancellationToken)
        {
            Code = code;
            Verifier = codeVerifier;
            RedirectUri = redirectUri;

            return Task.FromResult(Issued);
        }

        public Task<TokenSet> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
