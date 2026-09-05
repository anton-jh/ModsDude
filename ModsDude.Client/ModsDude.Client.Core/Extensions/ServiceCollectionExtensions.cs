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
        services.AddSingleton<Savegames.ISavegamePacker, Savegames.SavegamePacker>();

        // StateStore itself is registered by the host (App.xaml.cs), so this only names the seam the
        // binding store reads it through - which exists so a test can reach persisted state without
        // rewriting the developer's own state.json.
        services.AddSingleton<Savegames.IPersistedInstanceState, Savegames.StateStoreInstanceState>();
        services.AddSingleton<Savegames.SavegameBindingStore>();

        // The savegame engine and the one seam it needs: hydrating a savegame adapter takes the
        // repo's base settings, which an instance does not carry. TryAdd so a host that composes its
        // own - a test harness, or a shell that knows its repos by another route - keeps it.
        services.TryAddSingleton<Savegames.IInstanceSavegameAdapters, Savegames.RepoSavegameAdapters>();
        services.AddSingleton<Savegames.ISavegameService, Savegames.SavegameService>();

        // One per app: the drift answer is app-level, and every view reads the same one.
        services.AddSingleton<InstanceDriftMonitor>();

        return services;
    }
}
