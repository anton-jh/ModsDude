using Microsoft.Extensions.DependencyInjection;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.ModsDudeServer;
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// The generated clients each hard-code a localhost base url, and the file is regenerated
    /// wholesale, so the configured one is applied here instead.
    /// </summary>
    public static IServiceCollection AddModsDudeClient(this IServiceCollection services, string serverBaseUrl)
    {
        services.AddHttpClient<IReposClient, ReposClient>()
            .AddTypedClient<IReposClient>((http, sp) => new ReposClient(sp.GetRequiredService<ClientConfiguration>(), http) { BaseUrl = serverBaseUrl });

        services.AddHttpClient<IUsersClient, UsersClient>()
            .AddTypedClient<IUsersClient>((http, sp) => new UsersClient(sp.GetRequiredService<ClientConfiguration>(), http) { BaseUrl = serverBaseUrl });

        services.AddHttpClient<IMembersClient, MembersClient>()
            .AddTypedClient<IMembersClient>((http, sp) => new MembersClient(sp.GetRequiredService<ClientConfiguration>(), http) { BaseUrl = serverBaseUrl });

        services.AddHttpClient<IProfilesClient, ProfilesClient>()
            .AddTypedClient<IProfilesClient>((http, sp) => new ProfilesClient(sp.GetRequiredService<ClientConfiguration>(), http) { BaseUrl = serverBaseUrl });

        services.AddHttpClient<IModDependenciesClient, ModDependenciesClient>()
            .AddTypedClient<IModDependenciesClient>((http, sp) => new ModDependenciesClient(sp.GetRequiredService<ClientConfiguration>(), http) { BaseUrl = serverBaseUrl });

        services.AddHttpClient<IModsClient, ModsClient>()
            .AddTypedClient<IModsClient>((http, sp) => new ModsClient(sp.GetRequiredService<ClientConfiguration>(), http) { BaseUrl = serverBaseUrl });

        services.AddHttpClient<IFilesClient, FilesClient>()
            .AddTypedClient<IFilesClient>((http, sp) => new FilesClient(sp.GetRequiredService<ClientConfiguration>(), http) { BaseUrl = serverBaseUrl });

        return services;
    }
}
