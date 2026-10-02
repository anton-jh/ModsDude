using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Storage.Services;

namespace ModsDude.Server.Storage.Extensions;
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddStorage(this IServiceCollection services, string storageAccountName)
    {
        services.AddAzureClients(clientBuilder =>
        {
            clientBuilder.UseCredential(CreateCredential());
            clientBuilder.AddBlobServiceClient(new Uri($"https://{storageAccountName}.blob.core.windows.net"));
        });
        services.AddScoped<IModStorageService, ModStorageService>();
        services.AddScoped<IModImageStorageService, ModImageStorageService>();
        services.AddScoped<ISavegameStorageService, SavegameStorageService>();

        return services;
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
