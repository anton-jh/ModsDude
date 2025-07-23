using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.ModsDudeServer;
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddModsDudeClient(this IServiceCollection services)
    {
        services.AddHttpClient<IReposClient, ReposClient>();
        services.AddHttpClient<IProfilesClient, ProfilesClient>();
        services.AddHttpClient<IUsersClient, UsersClient>();
        services.AddHttpClient<IMembersClient, MembersClient>();

        return services;
    }
}
