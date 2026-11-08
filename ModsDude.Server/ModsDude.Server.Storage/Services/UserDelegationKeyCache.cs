using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace ModsDude.Server.Storage.Services;

/// <summary>
/// The key every SAS link is signed with, fetched once a day rather than once per link. A key costs a
/// round trip to the identity service, and drawing a 2,000-mod catalog mints a link per mod.
/// </summary>
/// <remarks>
/// A key signs any link whose window lies inside its own, so a key lasting a day serves every link
/// minted in it but the last half hour's, at which point a new one is fetched.
/// </remarks>
internal sealed class UserDelegationKeyCache(Func<DateTimeOffset, DateTimeOffset, CancellationToken, Task<UserDelegationKey>> fetch)
    : IUserDelegationKeyCache, IDisposable
{
    public static readonly TimeSpan KeyLifetime = TimeSpan.FromDays(1);

    private readonly SemaphoreSlim _gate = new(1, 1);

    private UserDelegationKey? _current;


    public UserDelegationKeyCache(BlobServiceClient blobServiceClient)
        : this(async (startsOn, expiresOn, cancellationToken) =>
            (await blobServiceClient.GetUserDelegationKeyAsync(startsOn, expiresOn, cancellationToken)).Value)
    {
    }


    public async Task<UserDelegationKey> GetAsync(DateTimeOffset linkStartsOn, DateTimeOffset linkExpiresOn, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _current) is { } current && Covers(current, linkStartsOn, linkExpiresOn))
        {
            return current;
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            // Fetched by whoever held the gate before this caller, in which case it is reused.
            if (_current is { } fetched && Covers(fetched, linkStartsOn, linkExpiresOn))
            {
                return fetched;
            }

            var key = await fetch(linkStartsOn, linkStartsOn + KeyLifetime, cancellationToken);

            if (!Covers(key, linkStartsOn, linkExpiresOn))
            {
                throw new InvalidOperationException($"Storage issued a user delegation key valid {key.SignedStartsOn:O} to {key.SignedExpiresOn:O}, which does not cover a link valid {linkStartsOn:O} to {linkExpiresOn:O}.");
            }

            Volatile.Write(ref _current, key);

            return key;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _gate.Dispose();
    }


    private static bool Covers(UserDelegationKey key, DateTimeOffset linkStartsOn, DateTimeOffset linkExpiresOn)
    {
        return key.SignedStartsOn <= linkStartsOn && key.SignedExpiresOn >= linkExpiresOn;
    }
}
