using Microsoft.Extensions.DependencyInjection;
using ModsDude.WindowsClient.ApiClient.Generated;

namespace ModsDude.WindowsClient.Model.ModsDudeServer;
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddModsDudeClient(this IServiceCollection services)
    {
        services.AddHttpClient<IReposClient, ReposClient>();
        services.AddHttpClient<IProfilesClient, ProfilesClient>();

        return services;
    }
}
