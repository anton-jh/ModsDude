using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Accounts;
using ModsDude.Client.Core.Builds;
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
        services.AddSingleton<IServerCompatibility>(new ServerCompatibility(BuildNumber.Current));
        services.AddTransient(sp => new BuildHeaderHandler(
            BuildNumber.Current,
            sp.GetRequiredService<IServerCompatibility>(),
            sp.GetRequiredService<ILogger<BuildHeaderHandler>>()));
        services.AddSingleton<IAccountStatus, AccountStatus>();
        services.AddTransient<AccountStatusHandler>();

        AddClient<IReposClient>(services, (configuration, http) => new ReposClient(configuration, http) { BaseUrl = serverBaseUrl });
        AddClient<IUsersClient>(services, (configuration, http) => new UsersClient(configuration, http) { BaseUrl = serverBaseUrl });
        AddClient<IInvitesClient>(services, (configuration, http) => new InvitesClient(configuration, http) { BaseUrl = serverBaseUrl });
        AddClient<ITrustCodesClient>(services, (configuration, http) => new TrustCodesClient(configuration, http) { BaseUrl = serverBaseUrl });
        AddClient<IMembersClient>(services, (configuration, http) => new MembersClient(configuration, http) { BaseUrl = serverBaseUrl });
        AddClient<IProfilesClient>(services, (configuration, http) => new ProfilesClient(configuration, http) { BaseUrl = serverBaseUrl });
        AddClient<IModDependenciesClient>(services, (configuration, http) => new ModDependenciesClient(configuration, http) { BaseUrl = serverBaseUrl });
        AddClient<IModsClient>(services, (configuration, http) => new ModsClient(configuration, http) { BaseUrl = serverBaseUrl });
        AddClient<ISavegamesClient>(services, (configuration, http) => new SavegamesClient(configuration, http) { BaseUrl = serverBaseUrl });
        AddClient<IFilesClient>(services, (configuration, http) => new FilesClient(configuration, http) { BaseUrl = serverBaseUrl });
        AddClient<IModHubClient>(services, (configuration, http) => new ModHubClient(configuration, http) { BaseUrl = serverBaseUrl });
        AddClient<IImagesClient>(services, (configuration, http) => new ImagesClient(configuration, http) { BaseUrl = serverBaseUrl });
        AddClient<IActivityClient>(services, (configuration, http) => new ActivityClient(configuration, http) { BaseUrl = serverBaseUrl });

        return services;
    }


    private static void AddClient<TClient>(IServiceCollection services, Func<ClientConfiguration, HttpClient, TClient> create)
        where TClient : class
    {
        services.AddHttpClient(typeof(TClient).Name)
            .AddHttpMessageHandler<BuildHeaderHandler>()
            .AddHttpMessageHandler<AccountStatusHandler>()
            .AddTypedClient((http, sp) => create(sp.GetRequiredService<ClientConfiguration>(), http));
    }
}
