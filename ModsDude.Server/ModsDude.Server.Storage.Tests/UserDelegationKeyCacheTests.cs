using Azure.Storage.Blobs.Models;
using ModsDude.Server.Storage.Services;

namespace ModsDude.Server.Storage.Tests;

/// <summary>
/// Every SAS link is signed with this key, so a key that does not cover a link's window is a link that
/// fails at storage, and a key fetched per link is a round trip per mod in a 2,000-mod catalog.
/// </summary>
public class UserDelegationKeyCacheTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan _linkLifetime = TimeSpan.FromMinutes(35);

    private int _fetches;


    [Fact]
    public async Task One_key_signs_every_link_its_window_covers()
    {
        var cache = new UserDelegationKeyCache(Fetch);

        for (var minutes = 0; minutes < 60 * 23; minutes += 7)
        {
            await cache.GetAsync(_now.AddMinutes(minutes), _now.AddMinutes(minutes) + _linkLifetime, CancellationToken.None);
        }

        Assert.Equal(1, _fetches);
    }

    [Fact]
    public async Task A_link_reaching_past_the_key_gets_a_new_key()
    {
        var cache = new UserDelegationKeyCache(Fetch);

        await cache.GetAsync(_now, _now + _linkLifetime, CancellationToken.None);

        var later = _now + UserDelegationKeyCache.KeyLifetime - TimeSpan.FromMinutes(10);
        var key = await cache.GetAsync(later, later + _linkLifetime, CancellationToken.None);

        Assert.Equal(2, _fetches);
        Assert.True(key.SignedExpiresOn >= later + _linkLifetime);
    }

    [Fact]
    public async Task Concurrent_callers_share_one_fetch()
    {
        var release = new TaskCompletionSource();
        var cache = new UserDelegationKeyCache(async (startsOn, expiresOn, cancellationToken) =>
        {
            await release.Task;
            return await Fetch(startsOn, expiresOn, cancellationToken);
        });

        var callers = Enumerable.Range(0, 10)
            .Select(_ => cache.GetAsync(_now, _now + _linkLifetime, CancellationToken.None))
            .ToList();

        release.SetResult();
        await Task.WhenAll(callers);

        Assert.Equal(1, _fetches);
    }

    [Fact]
    public async Task A_cancelled_fetch_leaves_the_next_caller_to_fetch()
    {
        using var cancellation = new CancellationTokenSource();
        var first = true;
        var cache = new UserDelegationKeyCache(async (startsOn, expiresOn, cancellationToken) =>
        {
            if (first)
            {
                first = false;
                await cancellation.CancelAsync();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return await Fetch(startsOn, expiresOn, cancellationToken);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.GetAsync(_now, _now + _linkLifetime, cancellation.Token));

        var key = await cache.GetAsync(_now, _now + _linkLifetime, CancellationToken.None);

        Assert.Equal(1, _fetches);
        Assert.True(key.SignedStartsOn <= _now);
    }

    [Fact]
    public async Task A_key_that_does_not_cover_the_link_is_refused()
    {
        var cache = new UserDelegationKeyCache((startsOn, _, _) => Task.FromResult(Key(startsOn, startsOn.AddMinutes(1))));

        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetAsync(_now, _now + _linkLifetime, CancellationToken.None));
    }


    private Task<UserDelegationKey> Fetch(DateTimeOffset startsOn, DateTimeOffset expiresOn, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _fetches);

        return Task.FromResult(Key(startsOn, expiresOn));
    }

    private static UserDelegationKey Key(DateTimeOffset startsOn, DateTimeOffset expiresOn)
        => BlobsModelFactory.UserDelegationKey("oid", "tid", startsOn, expiresOn, "b", "2021-08-06", "c2VjcmV0");
}
