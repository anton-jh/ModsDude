using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Storage.Services;

namespace ModsDude.Server.Storage.Extensions;
public static class ServiceCollectionExtensions
{
    private const string _backupClientName = "backups";


    /// <param name="backupStorageAccountName">
    /// The account <c>deploy/backup.sh</c> uploads database backups to, or empty when there is none.
    /// </param>
    public static IServiceCollection AddStorage(this IServiceCollection services, string storageAccountName, string? backupStorageAccountName)
    {
        var hasBackupStorage = !string.IsNullOrWhiteSpace(backupStorageAccountName);

        services.AddAzureClients(clientBuilder =>
        {
            clientBuilder.UseCredential(CreateCredential());
            clientBuilder.AddBlobServiceClient(GetBlobServiceUri(storageAccountName));

            if (hasBackupStorage)
            {
                clientBuilder.AddBlobServiceClient(GetBlobServiceUri(backupStorageAccountName!)).WithName(_backupClientName);
            }
        });
        services.AddScoped<IModStorageService, ModStorageService>();
        services.AddScoped<IModImageStorageService, ModImageStorageService>();
        services.AddScoped<ISavegameStorageService, SavegameStorageService>();
        services.AddScoped<IBackupStorageService>(sp => new BackupStorageService(
            hasBackupStorage ? sp.GetRequiredService<IAzureClientFactory<BlobServiceClient>>().CreateClient(_backupClientName) : null,
            sp.GetRequiredService<ILogger<BackupStorageService>>()));

        return services;
    }

    private static Uri GetBlobServiceUri(string storageAccountName)
    {
        return new Uri($"https://{storageAccountName}.blob.core.windows.net");
    }

    /// <summary>
    /// The server signs in with the service principal in the AZURE_CLIENT_ID, AZURE_TENANT_ID and
    /// AZURE_CLIENT_SECRET environment variables, a developer with their Azure CLI login. Two links in the
    /// chain are excluded because they only ever add noise: the Visual Studio credential offers whatever
    /// account VS is signed in with - typically a personal one the tenant rejects with AADSTS50020 - and
    /// the managed identity credential waits on 169.254.169.254, which only answers inside Azure.
    /// </summary>
    private static TokenCredential CreateCredential()
    {
        return new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ExcludeVisualStudioCredential = true,
            ExcludeManagedIdentityCredential = true
        });
    }
}
