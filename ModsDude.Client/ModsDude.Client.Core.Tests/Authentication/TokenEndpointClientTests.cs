using ModsDude.Client.Core.Authentication;
using System.Net;
using System.Text;

namespace ModsDude.Client.Core.Tests.Authentication;

public class TokenEndpointClientTests
{
    private readonly TestTimeProvider _time = new();
    private readonly StubHandler _handler = new();
    private readonly TokenEndpointClient _client;


    public TokenEndpointClientTests()
    {
        _client = new TokenEndpointClient(
            new SingleClientFactory(new HttpClient(_handler) { BaseAddress = new Uri("https://server.test/") }),
            new OidcClientOptions(new Uri("https://server.test/"), "client"),
            _time);
    }


    [Fact]
    public async Task A_refresh_posts_the_token_and_reads_the_new_set()
    {
        _handler.Respond(HttpStatusCode.OK, $$"""
            {"access_token":"access","expires_in":600,"refresh_token":"next","id_token":"{{TestTokens.IdentityToken(new { sub = "user-1", name = "N", email = "e@x" })}}"}
            """);

        var tokens = await _client.RefreshAsync("current", CancellationToken.None);

        Assert.Equal("access", tokens.AccessToken);
        Assert.Equal("next", tokens.RefreshToken);
        Assert.Equal(_time.GetUtcNow().AddSeconds(600), tokens.AccessTokenExpiresAt);
        Assert.Equal("user-1", tokens.Identity.Subject);

        Assert.Equal("https://server.test/connect/token", _handler.LastRequestUri?.ToString());
        Assert.Contains("grant_type=refresh_token", _handler.LastBody);
        Assert.Contains("refresh_token=current", _handler.LastBody);
        Assert.Contains("client_id=client", _handler.LastBody);
    }

    [Fact]
    public async Task A_code_exchange_sends_the_verifier_and_redirect()
    {
        _handler.Respond(HttpStatusCode.OK, $$"""
            {"access_token":"a","expires_in":60,"refresh_token":"r","id_token":"{{TestTokens.IdentityToken(new { sub = "u" })}}"}
            """);

        await _client.ExchangeCodeAsync("the-code", "the-verifier", new Uri("http://localhost:5000/callback/"), CancellationToken.None);

        Assert.Contains("grant_type=authorization_code", _handler.LastBody);
        Assert.Contains("code=the-code", _handler.LastBody);
        Assert.Contains("code_verifier=the-verifier", _handler.LastBody);
        Assert.Contains("redirect_uri=http%3A%2F%2Flocalhost%3A5000%2Fcallback%2F", _handler.LastBody);
    }

    [Fact]
    public async Task An_invalid_grant_is_a_rejection()
    {
        _handler.Respond(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"Revoked."}""");

        var exception = await Assert.ThrowsAsync<TokenGrantRejectedException>(() => _client.RefreshAsync("current", CancellationToken.None));

        Assert.Equal("invalid_grant", exception.Error);
    }

    [Fact]
    public async Task Any_other_failure_is_an_http_error_rather_than_a_rejection()
    {
        _handler.Respond(HttpStatusCode.BadGateway, "<html>bad gateway</html>");

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _client.RefreshAsync("current", CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
    }

    [Fact]
    public async Task A_response_without_a_refresh_token_is_refused()
    {
        _handler.Respond(HttpStatusCode.OK, """{"access_token":"a","expires_in":60}""");

        await Assert.ThrowsAsync<FormatException>(() => _client.RefreshAsync("current", CancellationToken.None));
    }


    private sealed class StubHandler : HttpMessageHandler
    {
        private HttpStatusCode _status;
        private string _body = "";

        public Uri? LastRequestUri { get; private set; }
        public string LastBody { get; private set; } = "";

        public void Respond(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            LastBody = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
