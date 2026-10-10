using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Sas;

namespace ModsDude.Server.Storage.Services;

internal static class BlobSasLinks
{
    private static readonly TimeSpan _lifetime = TimeSpan.FromMinutes(30);

    /// <summary>
    /// How far a SAS is backdated to absorb the difference between this server's clock and the
    /// storage account's. Generous on purpose: the cost of too much is a credential usable slightly
    /// earlier than intended, and the cost of too little is an upload that fails outright.
    /// </summary>
    private static readonly TimeSpan _clockSkewAllowance = TimeSpan.FromMinutes(5);


    public static async Task<string> CreateAsync(
        BlobServiceClient blobServiceClient,
        BlobClient blobClient,
        BlobSasPermissions permissions,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Backdated, because the signature is checked against Azure's clock rather than ours. A key
        // starting at this instant is rejected outright by a storage node running a second behind -
        // "Signature not valid in the specified time frame" - and the failure lands on the client
        // mid-upload, where it reads as an authentication problem rather than as the clock difference
        // it is. The window still ends _lifetime from now, so nothing is valid for longer.
        var startsOn = now - _clockSkewAllowance;
        var expiresOn = now + _lifetime;

        var userDelegationKey = await blobServiceClient.GetUserDelegationKeyAsync(startsOn, expiresOn, cancellationToken);

        var sasBuilder = new BlobSasBuilder(permissions, expiresOn)
        {
            BlobContainerName = blobClient.BlobContainerName,
            BlobName = blobClient.Name,
            Resource = "b",
            StartsOn = startsOn,
            ExpiresOn = expiresOn
        };

        var uriBuilder = new BlobUriBuilder(blobClient.Uri)
        {
            Sas = sasBuilder.ToSasQueryParameters(
                userDelegationKey,
                blobClient
                    .GetParentBlobContainerClient()
                    .GetParentBlobServiceClient()
                    .AccountName)
        };

        return uriBuilder.ToUri().ToString();
    }
}
