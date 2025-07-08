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
            clientBuilder.AddBlobServiceClient(new Uri($"https://{storageAccountName}.blob.core.windows.net"));
        });
        services.AddScoped<IModStorageService, ModStorageService>();

        return services;
    }
}
