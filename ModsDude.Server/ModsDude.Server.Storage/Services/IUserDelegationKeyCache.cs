using Azure.Storage.Blobs.Models;

namespace ModsDude.Server.Storage.Services;

internal interface IUserDelegationKeyCache
{
    /// <summary>A key valid for at least the whole window of the link it will sign.</summary>
    Task<UserDelegationKey> GetAsync(DateTimeOffset linkStartsOn, DateTimeOffset linkExpiresOn, CancellationToken cancellationToken);
}
