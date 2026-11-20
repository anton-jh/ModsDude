using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Authentication;

namespace ModsDude.Client.Core.Tests.Authentication;

public class TokenSessionTests : IDisposable
{
    private readonly TestTimeProvider _time = new();
    private readonly FakeTokenEndpoint _endpoint = new();
    private readonly MemoryTokenStore _store = new();
    private readonly TokenSession _session;


    public TokenSessionTests()
    {
        _session = new TokenSession(_endpoint, _store, _time, NullLogger<TokenSession>.Instance);
    }


    public void Dispose() => _session.Dispose();


    [Fact]
    public async Task Nobody_signed_in_needs_a_sign_in()
    {
        await Assert.ThrowsAsync<SignInRequiredException>(() => _session.GetAccessTokenAsync(CancellationToken.None));
    }

    [Fact]
    public async Task An_adopted_sign_in_is_stored_and_its_token_used_as_it_is()
    {
        await _session.AdoptAsync(TestTokens.Set("access", _time.GetUtcNow().AddMinutes(10), "refresh"), CancellationToken.None);

        Assert.Equal("access", await _session.GetAccessTokenAsync(CancellationToken.None));
        Assert.Equal("refresh", _store.RefreshToken);
        Assert.Equal(0, _endpoint.Refreshes);
        Assert.Equal("user-1", _session.Identity?.Subject);
    }

    [Fact]
    public async Task A_token_about_to_run_out_is_refreshed_and_the_new_refresh_token_stored()
    {
        await _session.AdoptAsync(TestTokens.Set("old", _time.GetUtcNow().AddMinutes(10), "refresh-1"), CancellationToken.None);
        _endpoint.Next = TestTokens.Set("new", _time.GetUtcNow().AddMinutes(20), "refresh-2");

        _time.Advance(TimeSpan.FromMinutes(10) - TokenSession.RefreshMargin);

        Assert.Equal("new", await _session.GetAccessTokenAsync(CancellationToken.None));
        Assert.Equal(["refresh-1"], _endpoint.RefreshedWith);
        Assert.Equal("refresh-2", _store.RefreshToken);
    }

    [Fact]
    public async Task Requests_needing_a_refresh_at_once_share_one()
    {
        await _session.AdoptAsync(TestTokens.Set("old", _time.GetUtcNow().AddMinutes(10), "refresh-1"), CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(10));

        var release = new TaskCompletionSource();
        _endpoint.Gate = release.Task;
        _endpoint.Next = TestTokens.Set("new", _time.GetUtcNow().AddMinutes(10), "refresh-2");

        var requests = Enumerable.Range(0, 5).Select(_ => _session.GetAccessTokenAsync(CancellationToken.None)).ToList();
        release.SetResult();

        Assert.All(await Task.WhenAll(requests), x => Assert.Equal("new", x));
        Assert.Equal(1, _endpoint.Refreshes);
    }

    [Fact]
    public async Task A_refused_refresh_needs_a_sign_in_clears_the_store_and_is_not_tried_again()
    {
        await _session.AdoptAsync(TestTokens.Set("old", _time.GetUtcNow(), "refresh-1"), CancellationToken.None);
        _endpoint.Reject = true;

        var exception = await Assert.ThrowsAsync<SignInRequiredException>(() => _session.GetAccessTokenAsync(CancellationToken.None));
        await Assert.ThrowsAsync<SignInRequiredException>(() => _session.GetAccessTokenAsync(CancellationToken.None));

        Assert.IsType<TokenGrantRejectedException>(exception.InnerException);
        Assert.Null(_store.RefreshToken);
        Assert.Equal(1, _endpoint.Refreshes);
        Assert.Equal("user-1", _session.Identity?.Subject);
    }

    [Fact]
    public async Task Signing_in_again_after_a_refusal_works()
    {
        await _session.AdoptAsync(TestTokens.Set("old", _time.GetUtcNow(), "refresh-1"), CancellationToken.None);
        _endpoint.Reject = true;
        await Assert.ThrowsAsync<SignInRequiredException>(() => _session.GetAccessTokenAsync(CancellationToken.None));

        await _session.AdoptAsync(TestTokens.Set("again", _time.GetUtcNow().AddMinutes(10), "refresh-2"), CancellationToken.None);

        Assert.Equal("again", await _session.GetAccessTokenAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_network_failure_while_refreshing_leaves_the_session_as_it_was()
    {
        await _session.AdoptAsync(TestTokens.Set("old", _time.GetUtcNow(), "refresh-1"), CancellationToken.None);
        _endpoint.Fail = new HttpRequestException("No connection.");

        await Assert.ThrowsAsync<HttpRequestException>(() => _session.GetAccessTokenAsync(CancellationToken.None));

        _endpoint.Fail = null;
        _endpoint.Next = TestTokens.Set("new", _time.GetUtcNow().AddMinutes(10), "refresh-2");

        Assert.Equal("new", await _session.GetAccessTokenAsync(CancellationToken.None));
        Assert.Equal(["refresh-1", "refresh-1"], _endpoint.RefreshedWith);
    }

    [Fact]
    public async Task Resuming_without_a_stored_token_needs_a_sign_in()
    {
        Assert.False(await _session.TryResumeAsync(CancellationToken.None));
        Assert.Equal(0, _endpoint.Refreshes);
    }

    [Fact]
    public async Task Resuming_with_a_stored_token_signs_in_silently()
    {
        _store.RefreshToken = "stored";
        _endpoint.Next = TestTokens.Set("access", _time.GetUtcNow().AddMinutes(10), "rotated");

        Assert.True(await _session.TryResumeAsync(CancellationToken.None));

        Assert.Equal("access", await _session.GetAccessTokenAsync(CancellationToken.None));
        Assert.Equal("rotated", _store.RefreshToken);
    }

    [Fact]
    public async Task Resuming_with_a_refused_token_forgets_it()
    {
        _store.RefreshToken = "stored";
        _endpoint.Reject = true;

        Assert.False(await _session.TryResumeAsync(CancellationToken.None));
        Assert.Null(_store.RefreshToken);
    }

    [Fact]
    public async Task Cancelling_a_wait_for_the_refresh_leaves_the_session_usable()
    {
        await _session.AdoptAsync(TestTokens.Set("old", _time.GetUtcNow(), "refresh-1"), CancellationToken.None);

        var release = new TaskCompletionSource();
        _endpoint.Gate = release.Task;
        _endpoint.Next = TestTokens.Set("new", _time.GetUtcNow().AddMinutes(10), "refresh-2");

        var first = _session.GetAccessTokenAsync(CancellationToken.None);
        using var cancelled = new CancellationTokenSource();
        var second = _session.GetAccessTokenAsync(cancelled.Token);

        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);

        release.SetResult();
        Assert.Equal("new", await first);
    }


    [Fact]
    public async Task Cancelling_the_request_that_is_refreshing_still_keeps_the_new_tokens()
    {
        await _session.AdoptAsync(TestTokens.Set("old", _time.GetUtcNow(), "refresh-1"), CancellationToken.None);

        var release = new TaskCompletionSource();
        _endpoint.Gate = release.Task;
        _endpoint.Next = TestTokens.Set("new", _time.GetUtcNow().AddMinutes(10), "refresh-2");

        using var cancelled = new CancellationTokenSource();
        var refreshing = _session.GetAccessTokenAsync(cancelled.Token);

        cancelled.Cancel();
        release.SetResult();

        Assert.Equal("new", await refreshing);
        Assert.Equal("refresh-2", _store.RefreshToken);
        Assert.Equal("new", await _session.GetAccessTokenAsync(CancellationToken.None));
        Assert.Equal(1, _endpoint.Refreshes);
    }

    [Fact]
    public async Task A_refresh_token_that_cannot_be_kept_is_still_used()
    {
        await _session.AdoptAsync(TestTokens.Set("old", _time.GetUtcNow(), "refresh-1"), CancellationToken.None);
        _endpoint.Next = TestTokens.Set("new", _time.GetUtcNow().AddMinutes(10), "refresh-2");
        _store.FailSaves = true;

        Assert.Equal("new", await _session.GetAccessTokenAsync(CancellationToken.None));
        Assert.Equal("refresh-1", _store.RefreshToken);
    }


    private sealed class FakeTokenEndpoint : ITokenEndpointClient
    {
        private int _refreshes;

        public TokenSet? Next { get; set; }
        public bool Reject { get; set; }
        public Exception? Fail { get; set; }
        public Task Gate { get; set; } = Task.CompletedTask;
        public List<string> RefreshedWith { get; } = [];
        public int Refreshes => Volatile.Read(ref _refreshes);

        public Task<TokenSet> ExchangeCodeAsync(string code, string codeVerifier, Uri redirectUri, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public async Task<TokenSet> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _refreshes);
            lock (RefreshedWith)
            {
                RefreshedWith.Add(refreshToken);
            }

            await Gate;

            if (Fail is Exception failure)
            {
                throw failure;
            }

            return Reject
                ? throw new TokenGrantRejectedException("invalid_grant", null)
                : Next ?? throw new InvalidOperationException("No token set was arranged.");
        }
    }

    private sealed class MemoryTokenStore : ITokenStore
    {
        public string? RefreshToken { get; set; }
        public bool FailSaves { get; set; }

        public Task<string?> LoadRefreshTokenAsync(CancellationToken cancellationToken) => Task.FromResult(RefreshToken);

        public Task SaveRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
        {
            if (FailSaves)
            {
                throw new IOException("The disk is full.");
            }

            RefreshToken = refreshToken;
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken)
        {
            RefreshToken = null;
            return Task.CompletedTask;
        }
    }
}
