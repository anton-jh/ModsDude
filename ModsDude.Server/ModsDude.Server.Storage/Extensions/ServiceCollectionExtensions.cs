using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.DependencyInjection;
using ModsDude.Server.Application.Dependencies;
using ModsDude.Server.Storage.Services;

namespace ModsDude.Server.Storage.Extensions;
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddStorage(this IServiceCollection services, string storageAccountName, bool isDevelopment)
    {
        services.AddAzureClients(clientBuilder =>
        {
            clientBuilder.UseCredential(CreateCredential(isDevelopment));
            clientBuilder.AddBlobServiceClient(new Uri($"https://{storageAccountName}.blob.core.windows.net"));
        });
        services.AddScoped<IModStorageService, ModStorageService>();
        services.AddScoped<IModImageStorageService, ModImageStorageService>();

        return services;
    }

    /// <summary>
    /// Spelled out rather than left to the plain <see cref="DefaultAzureCredential"/>, because two links
    /// in its chain are pure noise on a developer machine: the Visual Studio credential offers whatever
    /// account VS is signed in with - typically a personal one the tenant rejects with AADSTS50020 - and
    /// the managed identity credential waits on 169.254.169.254, which only answers inside Azure. Both
    /// log an error per token request before the chain moves on to the Azure CLI login that actually
    /// works. In Azure the managed identity is the one that must be kept.
    /// </summary>
    private static TokenCredential CreateCredential(bool isDevelopment)
    {
        return new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ExcludeVisualStudioCredential = true,
            ExcludeManagedIdentityCredential = isDevelopment
        });
    }
}
