using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModsDude.Client.Core.Authentication;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Imagery;
using ModsDude.Client.Core.Import;
using ModsDude.Client.Core.ModsDudeServer;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Core.Extensions;
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCore<TAccessTokenAccessor>(this IServiceCollection services, string serverBaseUrl)
        where TAccessTokenAccessor : IAccessTokenAccessor
    {
        services.AddSingleton<IAccessTokenAccessor>(sp => sp.GetRequiredService<TAccessTokenAccessor>());
        services.AddModsDudeClient(serverBaseUrl);
        services.AddGameAdapters(typeof(IGameAdapter).Assembly);

        // Their own HttpClients: they talk to blob storage over a SAS, not to the API, and must not
        // carry the access token the generated clients attach.
        services.AddHttpClient<IModFileUploader, BlockBlobModFileUploader>();
        services.AddHttpClient<IModFileDownloader, HttpModFileDownloader>();

        // Only if nothing else has: a client that can decode mod archives registers a real
        // publisher, and this is called after the app has composed its own services.
        services.TryAddSingleton<IModImagePublisher, NullModImagePublisher>();
        services.AddSingleton<ModImportService>();

        services.AddSingleton<IContentStoreProvider, ContentStoreProvider>();
        services.AddSingleton<IRecycleBin, ShellRecycleBin>();
        services.AddSingleton<SyncManifestStore>();
        services.AddSingleton<InstanceDriftService>();
        services.AddSingleton<ModSyncService>();

        // One per app: the drift answer is app-level, and every view reads the same one.
        services.AddSingleton<InstanceDriftMonitor>();

        return services;
    }
}
